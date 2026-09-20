# NexusCore + TaskManagement

NexusCore contains a modular .NET 8 API and a React/Vite administration UI.

This copy adds the **TaskManagement** module. It is an independent snapshot: it carries no git
history from, and no remote pointing at, the upstream NexusCore repository.

## TaskManagement

A task, project and recurring-task module built on the `Nexus.*` module template.

| Project | Contents |
|---|---|
| `Nexus.TaskManagement` | domain, application services, validators, endpoints, permissions |
| `Nexus.TaskManagement.Infrastructure` | `TaskManagementDbContext`, repositories, migrations, scheduler |
| `Nexus.Integrations.TaskNotifications` | bridge to the Notifications module |
| `Nexus.TaskManagement.Tests` | 85 unit and integration tests |

Every task — plain, project and recurring — is a row in `task_management.Tasks`, distinguished
by `IsProject`. `RepetitiveTasks` holds only the recurrence schedule and links back through a
unique `TaskId`. The module owns its own schema and holds real foreign keys into the shared
`identity` schema without owning those tables.

API surface: 30 routes under `/api/task-management`, all permission-gated and visible in Swagger.

See [docs/TaskManagement-Implementation.md](docs/TaskManagement-Implementation.md) for the full
design, the business rules, what is verified and what is not, and the remaining dependencies.

### Enabling the recurring-task scheduler

Off by default. In `Rozet.Api/appsettings.json`:

```jsonc
"TaskManagement": {
  "Scheduler": { "Enabled": true, "PollIntervalSeconds": 60, "BatchSize": 100 }
}
```

### Configuration

`appsettings.json` ships development defaults only: connection strings use Windows
authentication and the JWT `SigningKey` is a placeholder. Replace both before any real
deployment, and supply secrets through user-secrets, environment variables or a vault rather
than the settings file.

## Prerequisites

- .NET SDK 8
- Node.js and npm
- SQL Server with the connection strings configured in
  `NexusCore.Api/appsettings.json` or environment variables

## Run locally

1. Start the full application API (HTTP port 5151):
   `dotnet run --project Rozet.Api/Rozet.Api.csproj --launch-profile http`
2. In a second terminal, install the UI dependencies:
   `npm install`
3. Start the administration UI on port 3030:
   `npm run dev`

The UI development script points `VITE_API_BASE_URL` at
`http://localhost:5151`. Override it when the API runs elsewhere.

## Verify

- UI type-check: `npm run lint`
- UI production build: `npm run build`
- Backend build: `dotnet build NexusCore.sln`
- Backend tests: `dotnet test NexusCore.sln`
