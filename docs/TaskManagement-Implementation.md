# TaskManagement — as-built

The module is functionally complete: entities, EF Core mapping, migrations, services,
validators, repositories, REST endpoints, the recurring-task scheduler, the due-event handler,
and the notification and SMS paths.

Two things are deliberately **not** finished, and are not claimed to be: real SMS delivery has
no gateway to talk to, and phone numbers cannot be resolved because the shared `User` entity
has no phone column. Both are explained in §9.

---

## 1. Shared infrastructure vs. business modules

### What actually blocked real foreign keys

Not the architecture tests — they only police `.csproj` references, and depending on NexusCore
was always allowed. The real constraints were a **convention** (the doc comment on
`Project.cs`, which drew no line between shared infrastructure and a business module) and a
**technical fact** (EF only emits a foreign key when the principal entity is in the same model).

### The minimum change

**No project reference was added and no architecture test was relaxed.**
`NexusCore.Application` already references `NexusCore.Domain`, and every module already
references `NexusCore.Application`, so `User` and `UserGroup` were already reachable.

The one technique needed, in `Configurations/SharedIdentityConfigurations.cs`:

```csharp
builder.ToTable("Users", "identity", table => table.ExcludeFromMigrations());
```

`TaskManagementDbContext` maps `User` and `UserGroup` so EF emits the constraints;
`ExcludeFromMigrations` keeps `NexusCoreDbContext` their only owner. Only the key and a display
column are mapped — roles, tokens, permissions and membership are `Ignore`d, so this module
cannot drift against the owner's schema.

### Tests: added, never removed

All 22 original assertions still pass untouched. `TaskManagementIsolationTests` adds five:

| Test | Enforces |
|---|---|
| `TaskManagement_DoesNotReferenceAnyOtherBusinessModule` | no reference to Chat, Ticketing, Notifications, Events, ProjectManagement, Workflow, Integrations |
| `NoOtherModule_ReferencesTaskManagement` | the reverse, for every csproj except the host, the module's own tests, and integration bridges |
| `TaskManagement_DependsOnSharedNexusCoreFoundation` | the shared exception stated positively — fails if someone copies `User` into the module |
| `TaskManagement_BorrowsIdentityTables_ButNeverOwnsThem` | every borrowed mapping is `ExcludeFromMigrations`; no migration touches `identity` |
| `TaskNotificationsIntegration_ReferencesBothSidesAndIsReferencedByNeither` | the bridge stays a bridge |

The fourth was verified by sabotage: removing one `ExcludeFromMigrations` makes it fail.

---

## 2. Task and RepetitiveTask

Every task — plain, project, recurring — is one row in `task_management.Tasks`.

```
Tasks  1 ──── 0..1  RepetitiveTasks      (FK TaskId, UNIQUE)
  ├── 1 ──── *  SubTasks                 (Cascade)
  ├── 1 ──── *  TaskAssignees            (Cascade)
  └── 1 ──── *  TaskComments             (Cascade)
```

`RepetitiveTasks` holds only schedule state — no Title, Description, Priority, AssignedUserId
or UserGroupId. **"Is this task recurring?"** is answered by `Recurrence is not null`, not a
stored flag, so the two can never disagree. `TaskItem.IsRecurring` is computed and `Ignore`d
in EF; the list endpoint filters on `Recurrence != null`.

> **Open question.** The UI keeps one `recurringConfig` per task, so this is one-to-zero-or-one
> with a unique index on `TaskId`. If several schedules per task are ever wanted, that index is
> the single thing to drop.

---

## 3. Structure

```
Nexus.TaskManagement/
  Domain/         TaskItem, SubTask, RepetitiveTask, TaskFileAsset, TaskFile,
                  Tag, TaskTag, Note, TaskAssignee, TaskComment, Enums, TaskEvents
  Application/    Abstractions (repositories + services), TaskService,
                  SupportingServices (recurrence, tags, files, notes, comments),
                  RecurrenceCalculator, RepetitiveTaskDueHandler,
                  OutboundContracts, TaskSmsOptions, Dtos/, Validators/
  Endpoints/      TaskManagementEndpoints
  Permissions/    TaskManagementPermissions

Nexus.TaskManagement.Infrastructure/
  TaskManagementDbContext, Repositories, TaskActivityService,
  RepetitiveTaskSchedulerService, Configurations/, Migrations/

Nexus.Integrations.TaskNotifications/   bridge to Notifications
Nexus.TaskManagement.Tests/             50 unit tests
```

### Services

| Service | Covers |
|---|---|
| `TaskService` | task CRUD, status, priority, assignment, subtask CRUD |
| `RepetitiveTaskService` | schedule CRUD, enable/disable |
| `TagService` | tag CRUD, attach/detach to task and subtask |
| `TaskFileService` | upload, download, delete, list |
| `NoteService` | personal notes, scoped to the caller |
| `TaskCommentService` | comments, author-only edit and delete |
| `TaskActivityService` | task history, read from the shared `AuditLog` |

**No CQRS layer was added.** The `Nexus.*` family uses Service + Repository + `Result<T>`, not
MediatR — MediatR appears only in the older `Chat`/`Events`/`Ticketing`/`Notifications`
modules. Adding handlers here would have been the parallel architecture the brief forbids.

**Repositories were added** because `Nexus.ProjectManagement.Core` has `IProjectRepository`;
they expose intent (`GetSubTasksAsync`, `ClearLinksForTaskAsync`), not a generic EF wrapper.

### Endpoints

All under `/api/task-management`, all `RequireAuthorization` with a permission, all in Swagger.

| Method | Route | Permission |
|---|---|---|
| GET | `/tasks` (search, status, priority, isProject, isRecurring, assignee, team, tag, due range, overdue, sort, page) | `Tasks.View` |
| GET/POST | `/tasks`, `/tasks/{id}` | `Tasks.View` / `Tasks.Create` |
| PUT/DELETE | `/tasks/{id}` | `Tasks.Edit` / `Tasks.Delete` |
| PATCH | `/tasks/{id}/status`, `/priority` | `Tasks.Edit` |
| PATCH | `/tasks/{id}/assigned-user`, `/assigned-user-group` | `Tasks.Assign` |
| GET/POST | `/tasks/{id}/subtasks` | `Tasks.View` / `Tasks.Edit` |
| GET/PUT/PATCH/DELETE | `/subtasks/{id}`, `/status` | `Tasks.View` / `Tasks.Edit` |
| GET/POST/PUT/DELETE | `/repetitive-tasks` | `Tasks.View` / `Tasks.ManageRecurring` |
| POST | `/repetitive-tasks/{id}/enable`, `/disable` | `Tasks.ManageRecurring` |
| GET/POST/PUT/DELETE | `/tags` | `Tasks.View` / `Tasks.ManageTags` |
| POST/DELETE | `/tasks/{id}/tags`, `/subtasks/{id}/tags` | `Tasks.ManageTags` |
| POST | `/files/tasks/{id}`, `/files/subtasks/{id}` (multipart) | `Tasks.UploadFiles` |
| GET/DELETE | `/files/{id}/content`, `/files/{linkId}` | `Tasks.View` / `Tasks.UploadFiles` |
| GET/POST/PUT/DELETE | `/notes` | `Notes.Manage` |
| GET/POST | `/tasks/{id}/comments` | `Tasks.View` / `Tasks.Comment` |
| PUT/DELETE | `/comments/{id}` | `Tasks.Comment` |
| GET | `/tasks/{id}/activity` | `Tasks.View` |

Uploads are **multipart, not JSON**: the 200 KB ceiling is measured from the bytes that arrive,
and base64 in a JSON body would inflate them by a third.

---

## 4. Business rules enforced in the backend

- A project must be created with at least one subtask — task, subtasks, schedule and tags all
  commit in **one** `SaveChanges`, so a failure leaves no half-built project.
- Deleting a project's **last** subtask is refused.
- Promoting a plain task to a project requires an existing subtask; demoting is always allowed;
  a plain task is never held to the rule.
- An assignee cannot change status when the owner set `AllowAssigneeStatusUpdate` to false.
- Assignee, collaborator and team ids are checked against `identity` before being stored.
- Notes and comments are owner-scoped; another user's id reads as *not found*, so the endpoint
  cannot be used to probe which ids exist.
- Files: size measured from the real byte array, stored name server-generated, client filename
  kept for display only.

---

## 5. Recurring tasks

`RepetitiveTaskSchedulerService` is a hosted `BackgroundService` on a timer, following the
existing precedent (`Events.Infrastructure`'s `EventReminderBackgroundService`). NexusCore has
no Hangfire or Quartz; adding one for a single job would introduce a dependency the platform
does not share.

Order of operations, and why:

1. claim the occurrence with a **conditional** `ExecuteUpdate` — it advances the row only while
   `NextExecutionAtUtc` still equals what this pass read
2. that save raises `RepetitiveTaskDue`, dispatched afterwards by `DomainEventDispatchInterceptor`

`DomainEventDispatchInterceptor` runs in `SavedChangesAsync`, i.e. **after** `SaveChanges`
returns, and the scheduler opens no explicit transaction — so nothing external fires before the
data is committed.

Advancing first means a notification failure costs one message, never the schedule. **Delivery
is therefore at-most-once**: a failed occurrence is not retried, because retrying would mean
holding the schedule back and risking a task that fires forever. At-least-once needs an outbox,
which NexusCore does not have.

Guards against the scenarios in the brief:

| Scenario | Behaviour |
|---|---|
| Two passes overlap in one process | `SemaphoreSlim(1,1)`, second pass skipped |
| Two processes race the same row | conditional update; exactly one wins, loser logs and stops |
| Same occurrence processed twice | impossible — the claim moves `NextExecutionAtUtc` first |
| Task deleted | schedule deactivated, not retried forever |
| Schedule inactive | filtered out of the scan |
| One bad row | logged and skipped; the batch continues |
| Notification fails | logged; schedule already advanced |
| SMS fails | logged; notification unaffected |
| Recurrence can never match (empty weekly days) | calculator gives up after a 2-year horizon instead of looping |

`RecurrenceCalculator` handles Daily, Weekly (with N-week intervals), Monthly, MonthlyDay and
MonthlyNthWeekday including *last*. Day numbering follows the UI's Jalali week
(0 = Saturday … 6 = Friday), converted explicitly — never cast from `DayOfWeek`.

---

## 6. Notifications

`RepetitiveTaskDueHandler` implements `IDomainEventHandler<RepetitiveTaskDue>`. Recipients are
the assignee, the collaborators and the owner — de-duplicated, and nobody else.

TaskManagement does **not** reference Notifications. It owns `ITaskNotificationPublisher`;
`Nexus.Integrations.TaskNotifications` implements it against `INotificationService`. That
follows the repo's own `Nexus.Integrations.*` convention and is what lets the architecture tests
keep the two business modules apart. Leave the integration out and the module still runs on its
no-op publisher.

(`Events.Infrastructure` references `Notifications.Application` directly. That coupling was left
alone — changing another module was out of scope — but it is not the pattern followed here.)

---

## 7. SMS

`ITaskSmsSender`, configured under `TaskManagement:Sms`, **disabled by default**.

The shipped `LoggingTaskSmsSender` contacts no gateway. That is deliberate, not an unfinished
stub: NexusCore has no SMS infrastructure and no gateway is named anywhere in its configuration,
so picking one would be a guess. It logs what it would have sent — with the number masked to its
last four digits — which keeps the whole path exercisable end to end.

No API key, sender number or provider name is hardcoded. To go live, implement `ITaskSmsSender`
against a real gateway and register it in place of this one; everything upstream is unchanged.

---

## 8. C3, C4, C6

### C3 — file size limit: **resolved**

Backend: `TaskFileAsset.MaxFileSizeBytes = 204_800`, enforced in the constructor, in
`UploadFileRequestValidator`, in `TaskFileService` against the real byte count, and by
`CK_Files_MaxSize` in SQL Server.

Frontend (`F:\Tm`): the two 20 MB checks in `TaskDetailModal.tsx` now use a shared
`MAX_ATTACHMENT_BYTES = 204800` from `types.ts`, and messages read ۲۰۰ کیلوبایت. `TaskFormModal`
and `PersonalNotesView` had **no size check at all** — both now have one. No layout or styling
was touched.

Verified against a live database: 204,800 bytes accepted, 204,801 rejected by `CK_Files_MaxSize`.

### C4 — comments and history: **resolved, differently for each**

- **`TaskComment` → new entity.** The UI has a real comment thread and nothing in NexusCore
  covers it: `AuditLog` records what the system did, not what a person wrote, and has no
  editable body. Comments hang off tasks only — the UI's comment box never appears on a subtask.
  Attachments on comments are not modelled: the UI keeps them as inline base64 and never uploads
  them separately, so there is nothing for the backend to own yet.
- **`TaskLog` → reuses `AuditLog`.** Its columns (`UserId`, `Action`, `EntityName`, `EntityId`,
  `Details`, `OccurredAtUtc`) already carry everything the history panel shows. A `TaskLog` table
  would have been a second copy of an existing concept. `TaskActivityService` writes with
  `EntityName = "TaskManagement.Task"` and reads back by task id. **No new table.**

### C6 — UserGroups: **resolved**

- *Cause:* `Features:UserGroups:Enabled` was `false` in `Rozet.Api/appsettings.json`.
  `AddUserGroupFeature` (already called from `AddInfrastructure`) registered a null permission
  provider and no group services, and `Program.cs` skipped `MapUserGroupEndpoints`.
- *Change:* the flag is now `true`. One line; no code change was needed.
- *Result:* `IUserGroupService`, `IUserGroupRepository`, the real permission provider and the
  UserGroup endpoints are live, and `FK_Tasks_UserGroups_AssignedUserGroupId` was verified in a
  real database.
- *Impact on other features:* group permissions now contribute to permission resolution, which
  is the feature working as designed. Nothing else changes.

---

## 9. Known remaining dependencies

**SMS cannot resolve phone numbers.** `NexusCore.Domain.Identity.User` has no phone column
(`TenantId`, `Email`, `DisplayName`, `PasswordHash`, `IsActive`, `LastLoginAtUtc` only), while
the UI's user model has `phoneNumber`. `UnavailableUserContactResolver` therefore returns none.

This was **not** fixed unilaterally. Adding a column to `identity.Users` alters a table every
module shares, and the runtime creates schemas with `EnsureCreated`, which cannot add a column
to a database that already exists — so it needs a deliberate Core migration and a deployment
decision. The minimal change, when you want it:

1. add `PhoneNumber` (nullable, `nvarchar(32)`) to `User` with an update method
2. map it in `NexusCore.Infrastructure/.../IdentityConfigurations.cs`
3. add a NexusCore migration and apply it to existing databases
4. map it in `SharedUserConfiguration` and replace `UnavailableUserContactResolver` with one
   that reads it

**NexusCore's migration history is stale.** Its `Init` migration predates the UserGroup feature
and does not create `identity.UserGroups` — not in the migration, not in the model snapshot. The
runtime is unaffected because `ModuleSchemaInitializer` builds from the current model, but
`dotnet ef database update` on an empty database produces a schema that TaskManagement's own
migration then cannot bind its `UserGroups` foreign key to. Pre-existing, in Core, untouched.

**Migration ordering.** Because TaskManagement has real foreign keys into `identity`, its
migration requires that schema to exist first. `Program.cs` already seeds NexusCore before
calling `ModuleSchemaInitializer` for each module, so the runtime path is correct.

**`QUOTED_IDENTIFIER` must be ON** for `TaskFiles` and `TaskTags` — they carry filtered unique
indexes. EF and SqlClient set it by default; raw `sqlcmd` does not.

---

## 10. Verification

| Check | Result |
|---|---|
| `dotnet build NexusCore.sln --no-incremental` | succeeded — **0 errors**; 3 warnings, all pre-existing (Events.Infrastructure, Ticketing.Domain) |
| `dotnet test NexusCore.sln` | **111/111 passed** (2 NexusCore.Tests + 24 CompositionTests + 85 TaskManagement.Tests) |
| Migrations | `InitTaskManagement`, `AddTaskComments` |
| Host starts with the module composed | yes — DI graph resolved, 0 resolution errors |
| Schema created in a real database | 10 tables in `task_management` |
| CHECK constraints in SQL Server | 3, all present |
| 204,800 / 204,801 byte boundary | accepted / rejected by `CK_Files_MaxSize` |
| `TaskFiles` with both or neither owner | rejected by `CK_TaskFiles_ExactlyOneOwner` |
| `TaskTags` with no owner | rejected by `CK_TaskTags_AtLeastOneOwner` |
| Cross-schema FKs into `identity` | 6, verified in `sys.foreign_keys` |
| Unique index on `RepetitiveTasks.TaskId` | present, `is_unique = 1` |
| UI (`F:\Tm`) typecheck and build | clean |
| Test database used | throwaway `TaskMgmt_MigrationVerify`, **dropped afterwards** |
| `TaskManagerDB` (the real local database) | **untouched** |
| Other modules changed | none |

### Integration tests

35 of the 85 tests run against a **real SQL Server database**, created per run and dropped
afterwards. They use the in-memory provider nowhere: CHECK constraints, filtered unique
indexes, cascade behaviour and cross-schema foreign keys do not exist in memory, so a green
in-memory run would prove nothing about them. They skip cleanly when no SQL Server is reachable.

### Not tested

HTTP-level behaviour — routing, model binding, the JWT pipeline and the permission policies as
ASP.NET applies them — is not covered. That needs a `WebApplicationFactory`. Authorization is
verified only at the service layer (tenant scoping, note and comment ownership) and by reading
the endpoint definitions. Real SMS delivery is untested because there is nothing to deliver to.

---

## 11. Running it

```bash
dotnet run --project Rozet.Api --launch-profile http     # API on :5151, Swagger at /swagger
cd <frontend> && npm install && npm run dev              # UI on :3030
```

### Enabling the scheduler

```jsonc
"TaskManagement": {
  "Scheduler": { "Enabled": true, "PollIntervalSeconds": 60, "BatchSize": 100 }
}
```

Off by default. The service registers unconditionally and exits immediately when disabled, so
"is the job running" has one answer in one place.

### Notifications

Working already: `AddTaskNotificationsIntegration()` is wired in `Program.cs` and needs no
configuration.

### SMS

```jsonc
"TaskManagement": {
  "Sms": { "Enabled": true, "Provider": "<your gateway>", "SenderNumber": "<number>" }
}
```

Supply `ApiKey` through user-secrets, an environment variable or a vault — **never**
`appsettings.json`. Until a real `ITaskSmsSender` is registered, enabling this only logs.

---

## 12. Git

| Item | Value |
|---|---|
| Working copy | `F:\NexusCore-TaskManagement` |
| Remotes | **none** — a push is not physically possible |
| History | fresh `git init`, not a fork |
| Commits / pushes / branches / PRs / tags on the original NexusCore | **none** |
| Frontend `F:\Tm` | edited locally, **not committed, not pushed** |
