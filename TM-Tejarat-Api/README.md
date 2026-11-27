# TMTejarat API (Tejarat Bank)

The Tejarat Bank (TMTejarat) task-management API, built on the **NexusCore** platform packages — the same way
`TMPB-API` is built for Post Bank. It contains no Core source: every module comes as a NuGet
package from `./packages` (see `nuget.config`).

| Area | Packages |
|---|---|
| Identity, users, roles, permissions, SMS panel | `NexusCore.Infrastructure` (+ Application, Domain, SharedKernel) |
| Task management, recurring tasks, reminders | `Nexus.TaskManagement`, `Nexus.TaskManagement.Infrastructure` |
| Notifications, chat | `Notifications.*`, `Chat.*`, `Nexus.Integrations.TaskNotifications` |
| Deputies / units | `Nexus.Organization`, `Nexus.Organization.Infrastructure` |
| Companies, positions, managers, titles, org chart | `Nexus.OrgStructure`, `Nexus.OrgStructure.Infrastructure` |
| Task reports along the organization | `Nexus.Integrations.TaskReporting` |

Front end: **TMPBUI** (the same as Post Bank's). It shows the organization chart, the titles and
the «گزارش فعالیت‌ها» reports because this API answers for them.

## Run locally

```
dotnet run --launch-profile http
```

Opens `http://localhost:5113/swagger`. Development defaults: SQL Server on this machine, database
`TMTejarat` (created on first run), user `admin` / `Admin@12345`. Point TMPBUI at this address
(`VITE_DEV_API_BASE_URL=http://localhost:5113`).

## Deploy

See [DEPLOYMENT-CONFIG.md](DEPLOYMENT-CONFIG.md): CORS origin, JWT signing key, connection string,
the organization schema and how to take a newer NexusCore version.
