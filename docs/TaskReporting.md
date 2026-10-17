# Task reports (optional integration)

`Nexus.Integrations.TaskReporting` reports on tasks along the organization structure:

- every user reports on the tasks they can already see (owner, responsible, on the access list,
  or ownerless), **plus every task created by or assigned to anyone below them** in
  `Nexus.OrgStructure`, at any depth and whatever their position - even tasks they are not on;
- `Tasks.ManageAll` holders report on the whole organization.

It only reads. Opening such a task still follows Task Management's own rules (the report marks
each row with `canOpen`).

## Turning it on

Needs Task Management and the organization structure (`Nexus.Organization` + `Nexus.OrgStructure`):

```csharp
builder.Services.AddTaskReporting();
app.MapTaskReportEndpoints();
```

No tables, no permissions of its own (`Tasks.View` is required). Products that don't reference
the package - TMPB, PostBank - have no report endpoints, and the TMPBUI «گزارش فعالیت‌ها» page
stays hidden there (it is shown only when `/api/task-reports/scope` answers).

## Endpoints

| | |
|---|---|
| `GET /api/task-reports/scope` | whose tasks the caller covers (self, direct, indirect - or all), plus positions and tags to filter by |
| `GET /api/task-reports/tasks` | the report: a summary (counts by status, overdue, type - over every match) and up to 5000 rows |

Filters of `/tasks`, all optional:

| Query | Meaning |
|---|---|
| `personId` | a person's tasks |
| `personRole` | `responsible` (default) or `creator`; applies to `personId` and `positionId` |
| `positionId` | tasks of everyone holding that position |
| `tag` | tag name |
| `type` | `normal`, `project` or `recurring` (a recurring project counts as recurring) |
| `statuses` | comma-separated: `todo`, `in_progress`, `paused`, `completed` |
| `dueFrom`, `dueTo` | due date range, inclusive, `yyyy-MM-dd` |

Overdue = not recurring, to do or in progress, due before today (Iran calendar day).
