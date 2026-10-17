# NexusCore platform packages

NexusCore is the shared .NET 8 platform for identity, tenants, permissions, audit, settings and
reusable modules. Product hosts consume it through internal NuGet packages instead of
referencing this source tree directly.

## Product hosts live outside this repository

This solution contains the platform and its modules only — no product host. Each product is a
separate repository that references the packages built here (`eng/pack-core-modules.ps1`):

| Product | Repository | Composes |
|---|---|---|
| Post Bank task management | `TMPB_API` | Core, Chat, Notifications, Task Management, `Nexus.Integrations.TaskNotifications` |
| Tejarat Bank task management | `TejaratBank_API` | the same, plus `Nexus.Organization`, `Nexus.OrgStructure` and `Nexus.Integrations.TaskReporting` |

Both use the same front end (TMPBUI), which shows each optional feature only when the host
answers for it. `NexusCore.Api` in this solution is the platform's own development host, not a
product.

To release a Core change to a product: pack (below), copy the new `.nupkg` files into the
product's package feed (its `packages/` folder or the internal feed), raise the package version
in its `.csproj`, build and deploy it.

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

## Package Core for the products

```powershell
pwsh ./eng/pack-core-modules.ps1 -Configuration Release -Version 0.1.0 -Output artifacts/packages
```

The version is `0.1.0.<Persian date><daily build>`, e.g. `0.1.0.1405072501`. Copy the produced
`.nupkg` files to the approved internal NuGet feed or to the product's offline package folder. Do
not restore products from public feeds in production networks.

## Verify Core

- UI type-check: `npm run lint`
- UI production build: `npm run build`
- Backend build: `dotnet build NexusCore.sln`
- Backend tests: `dotnet test NexusCore.sln`
