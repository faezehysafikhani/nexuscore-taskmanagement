# NexusCore platform packages + TMPB

NexusCore is the shared .NET 8 platform for identity, tenants, permissions, audit, settings and
reusable modules. Product hosts should consume it through internal NuGet packages instead of
referencing this source tree directly.

This branch adds the **TMPB** host as a separate .NET 8 project under `TMPB/TMPB.Api`. TMPB is not
added to `NexusCore.sln`; it is intentionally independent and consumes the approved Core packages
through `PackageReference`.

## TMPB composition

TMPB currently composes only the modules needed by this product:

| Area | Packages/modules |
|---|---|
| Shared Core | `NexusCore.Application`, `NexusCore.Infrastructure`, `NexusCore.SharedKernel` |
| Chat | `Chat.Api`, `Chat.Application`, `Chat.Infrastructure` |
| Notifications | `Notifications.Api`, `Notifications.Application`, `Notifications.Infrastructure` |
| Task Manager | `Nexus.TaskManagement`, `Nexus.TaskManagement.Infrastructure` |
| Integration | `Nexus.Integrations.TaskNotifications` |

Rozet and PostBank are product hosts and are not part of the TMPB package surface.

## TaskManagement

A task, project and recurring-task module built on the `Nexus.*` module template.

| Project | Contents |
|---|---|
| `Nexus.TaskManagement` | domain, application services, validators, endpoints, permissions |
| `Nexus.TaskManagement.Infrastructure` | `TaskManagementDbContext`, repositories, migrations, scheduler |
| `Nexus.Integrations.TaskNotifications` | bridge to the Notifications module |
| `Nexus.TaskManagement.Tests` | unit and integration tests |

Every task — plain, project and recurring — is a row in `task_management.Tasks`, distinguished by
`IsProject`. `RepetitiveTasks` holds only the recurrence schedule and links back through a unique
`TaskId`. The module owns its own schema and holds real foreign keys into the shared `identity`
schema without owning those tables.

See [docs/TaskManagement-Implementation.md](docs/TaskManagement-Implementation.md) for the full
design and business rules.

See [docs/Accounts-Teams-Chat-Notifications.md](docs/Accounts-Teams-Chat-Notifications.md) for
accounts, teams, chat, notification gateways and live updates.

See [docs/Core-Packaging-Government-Deployment.md](docs/Core-Packaging-Government-Deployment.md)
for the internal NuGet packaging and offline/bank-network deployment workflow.

## Package Core for TMPB

```powershell
pwsh ./eng/pack-core-modules.ps1 -Configuration Release -Version 0.1.0 -Output artifacts/packages
```

Copy the produced `.nupkg` files to the approved internal NuGet feed or to an offline package
share. Do not restore TMPB from public feeds in production networks.

## Restore and run TMPB

```powershell
dotnet restore TMPB/TMPB.Api/TMPB.Api.csproj --configfile TMPB/nuget.config
dotnet run --project TMPB/TMPB.Api/TMPB.Api.csproj
```

`TMPB/TMPB.Api/appsettings.json` contains development placeholders only. Replace connection
strings, JWT signing keys and CORS origins through environment variables or a vault for real
deployments.

## Verify Core

- UI type-check: `npm run lint`
- UI production build: `npm run build`
- Backend build: `dotnet build NexusCore.sln`
- Backend tests: `dotnet test NexusCore.sln`
