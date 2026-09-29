# Deployment Configuration — NexusCore API

What to set when installing this API at a new site (e.g. a bank environment). None of these
steps touch source code, and none require a rebuild.

## 1. Set the frontend's origin (CORS)

Edit `NexusCore.Api/appsettings.Production.json` — the only file this install step touches:

```json
{
  "Cors": {
    "AllowedOrigins": ["https://<bank-host-or-ip>:<frontend-port>"]
  }
}
```

Use the exact scheme + host + port the frontend is served from. If the frontend and this API
are served from the same public address (a reverse proxy in front of both), you can leave this
empty — same-origin requests don't need CORS at all.

This file is loaded automatically: `ASPNETCORE_ENVIRONMENT` defaults to `Production` when unset,
which is the normal case for a real install (outside an IDE launch profile).

## 2. Set the JWT signing key

Never put a real signing key in any `appsettings*.json` file — it must not be committed. Set it
as an environment variable instead:

```
Jwt__SigningKey=<a real secret, at least 32 characters>
```

Set it however this environment sets process environment variables for the app — an IIS
Application Pool's environment variables, a Windows service's environment, a systemd unit file,
etc. If this is missing, a known development placeholder, or shorter than 32 characters, **the
app refuses to start** — this is intentional (`JwtOptions.EnsureSafeForProduction()`) and is not
a bug; set the key and start it again.

## 3. Set the database connection string (if not using the local default)

`appsettings.json`'s default (`Server=.`, i.e. this same machine) works if SQL Server runs on
the same host. Otherwise, add to `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<db-host>;Database=NexusCore;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
    "Chat": "Server=<db-host>;Database=NexusCoreChat;Trusted_Connection=True;TrustServerCertificate=True",
    "Ticket": "Server=<db-host>;Database=NexusCoreTicket;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

## 4. AllowedHosts

`"AllowedHosts": "*"` in `appsettings.json` (the base file) accepts any Host header, which is
the standard ASP.NET Core default and fine behind a reverse proxy/firewall. Tighten it in
`appsettings.Production.json` only if this site's policy requires restricting it to a specific
host name.

## 5. Start/restart

Restart the API (IIS site recycle, service restart, etc.). No `dotnet build`, no source change.

---

### What NOT to do
- Don't edit `appsettings.json` (the base file) for a specific deployment — it's meant to stay
  environment-independent so it's identical across every install. Environment-specific values go
  in `appsettings.Production.json` (this environment) or `appsettings.Development.json`
  (developers' machines only).
- Don't hardcode an IP/domain in `Program.cs` — CORS and hosts are already fully
  configuration-driven; there's nothing to touch there.
