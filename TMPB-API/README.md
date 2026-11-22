# TMPB API (Post Bank)

The Post Bank (TMPB) task-management API, built on the **NexusCore** platform packages — the same way
`TejaratBank-API` is built for Tejarat Bank. It contains no Core source: every module comes as a NuGet
package from `./packages` (see `nuget.config`).

| Area | Packages |
|---|---|
| Identity, users, roles, permissions, SMS panel | `NexusCore.Infrastructure` (+ Application, Domain, SharedKernel) |
| Task management, recurring tasks, reminders | `Nexus.TaskManagement`, `Nexus.TaskManagement.Infrastructure` |
| Notifications, chat | `Notifications.*`, `Chat.*`, `Nexus.Integrations.TaskNotifications` |
| Deputies / units | `Nexus.Organization`, `Nexus.Organization.Infrastructure` |
| Companies, positions, managers, titles, org chart | `Nexus.OrgStructure`, `Nexus.OrgStructure.Infrastructure` |
| Task reports along the organization | `Nexus.Integrations.TaskReporting` |

Front end: **TMPBUI** (the same as Tejarat Bank's). It shows the organization chart, the titles and
the «گزارش فعالیت‌ها» reports because this API answers for them.

## Run locally

```
dotnet run --launch-profile http
```

Opens `http://localhost:5112/swagger`. Development defaults: SQL Server on this machine, database
`TMPB` (created on first run), user `admin` / `Admin@12345`. Point TMPBUI at this address
(`VITE_DEV_API_BASE_URL=http://localhost:5112`).

## Deploy

See [DEPLOYMENT-CONFIG.md](DEPLOYMENT-CONFIG.md): CORS origin, JWT signing key, connection string,
the organization schema and how to take a newer NexusCore version.
