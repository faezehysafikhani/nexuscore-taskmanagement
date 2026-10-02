# NexusCore packaging for TMPB and restricted environments

TMPB must consume NexusCore as internal NuGet packages, not by referencing the Core source tree.
This keeps bank/government deployments reproducible and lets security teams approve a fixed set
of package artifacts before installation.

## Included modules

TMPB intentionally composes only these packages:

- `NexusCore.*` shared platform packages
- `Chat.*`
- `Notifications.*`
- `Nexus.TaskManagement.*`
- `Nexus.Integrations.TaskNotifications`

Project/product hosts such as Rozet and PostBank are not part of the Core package surface.

## Build packages

From a clean, reviewed Core checkout:

```powershell
pwsh ./eng/pack-core-modules.ps1 -Configuration Release -Version 0.1.0 -Output artifacts/packages
```

The output folder contains the `.nupkg` files that must be copied to the approved internal NuGet
feed or to an offline package share.

## TMPB restore in an offline network

`TMPB/nuget.config` clears public package sources and points restore to the local/internal package
source. In a bank network, replace the sample source with the approved internal NuGet feed URL.

```powershell
dotnet restore TMPB/TMPB.Api/TMPB.Api.csproj --configfile TMPB/nuget.config
```

Do not let production restore from public feeds. External dependencies must be mirrored and
approved in the internal feed first.

## Deployment controls

Before installing in a restricted organization:

1. Build packages from a signed/reviewed commit.
2. Store `.nupkg` files in the internal artifact repository.
3. Record package name, version, SHA-256 and source commit in the release ticket.
4. Restore TMPB only from the internal feed.
5. Provide secrets through environment variables, user-secrets for development, or a vault; never
   commit production connection strings or JWT signing keys.
6. Keep `Database:SeedOnStartup` disabled in production after the initial controlled schema setup,
   unless the deployment procedure explicitly requires it.

## Rate limiting

NexusCore packages include reusable ASP.NET Core rate-limit policies for the shared platform,
Identity, Task Management, Notifications and Chat modules. Product hosts must enable the shared
middleware once, before authentication/authorization:

```csharp
builder.Services.AddNexusCoreRateLimiting(builder.Configuration);

// ...

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseNexusCoreRateLimiting();
app.UseAuthorization();
```

Module endpoints carry policy metadata themselves, so product hosts do not need to repeat limits
on every route. Keep the defaults unless the organization approves a different capacity model.
Override them from the host's runtime configuration:

```json
{
  "NexusCore": {
    "RateLimiting": {
      "Enabled": true,
      "Global": { "PermitLimit": 600, "WindowMinutes": 1 },
      "Auth": { "PermitLimit": 10, "WindowMinutes": 1 },
      "PasswordRecovery": { "PermitLimit": 5, "WindowMinutes": 15 },
      "PasswordResetVerification": { "PermitLimit": 10, "WindowMinutes": 15 },
      "AuthenticatedApi": { "PermitLimit": 300, "WindowMinutes": 1 },
      "Write": { "PermitLimit": 120, "WindowMinutes": 1 },
      "Upload": { "PermitLimit": 20, "WindowMinutes": 5 },
      "ChatSend": { "PermitLimit": 60, "WindowMinutes": 1 },
      "Sms": { "PermitLimit": 5, "WindowMinutes": 5 },
      "Realtime": { "PermitLimit": 120, "WindowMinutes": 1 }
    }
  }
}
```

For bank deployments behind IIS or another reverse proxy, keep proxy-level request limits and
WebSocket controls enabled too. The application limiter trusts `X-Forwarded-For` when a proxy
sets it, so only allow trusted internal proxies to send or overwrite that header.

## Versioning

Use immutable versions. Do not overwrite an already-published package version. For hotfixes, publish
a new patch version, for example `0.1.1`.
