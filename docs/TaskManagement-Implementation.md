# TaskManagement — as-built implementation

Covers the two corrections: shared-vs-business module dependency, and Task/RepetitiveTask.
Entities, EF Core configuration and the initial migration exist. **No API, no business-logic
service and no background job yet** — those are the next stage.

---

## 1. Shared vs. business modules

### What actually blocked real foreign keys

Not the architecture tests. They only police `.csproj` references, in two directions:
foundation must not reference a module, and a module must not reference a sibling. Depending on
NexusCore was always allowed.

The real constraints were:

1. **A convention**, written in `Nexus.ProjectManagement.Core/Domain/Project.cs`: *"no navigation
   properties, no FK into other modules' tables."* It draws no line between shared infrastructure
   and a business module — that is the wording your correction fixes.
2. **A technical fact**: EF Core only emits a foreign key when the principal entity is part of the
   same model, and `User` lives in `NexusCoreDbContext`.

### The minimum change

**No project reference was added and no architecture test was relaxed.**
`NexusCore.Application` already references `NexusCore.Domain`, and every module already references
`NexusCore.Application` — so `User` and `UserGroup` were already reachable from every module.

The one technique needed is in `Configurations/SharedIdentityConfigurations.cs`:

```csharp
builder.ToTable("Users", "identity", table => table.ExcludeFromMigrations());
```

`TaskManagementDbContext` maps `User` and `UserGroup` so EF can emit the constraints, and
`ExcludeFromMigrations` keeps `NexusCoreDbContext` their only owner. Verified against the
generated DDL: **zero** `CREATE TABLE [identity]` statements.

Only the key and a display column are mapped; roles, tokens, permissions and membership are
`Ignore`d, so this module cannot drift against the owner's schema.

### Tests: added, never removed

All 22 original assertions still pass untouched. `TaskManagementIsolationTests` adds four:

| Test | Enforces |
|---|---|
| `TaskManagement_DoesNotReferenceAnyOtherBusinessModule` | no reference to Chat, Ticketing, Notifications, Events, ProjectManagement, Workflow, Integrations |
| `NoOtherModule_ReferencesTaskManagement` | the reverse, for every csproj in the solution except the host |
| `TaskManagement_DependsOnSharedNexusCoreFoundation` | the shared exception, stated positively — fails if someone copies `User` into the module |
| `TaskManagement_BorrowsIdentityTables_ButNeverOwnsThem` | every borrowed mapping is `ExcludeFromMigrations`, and no migration creates or drops anything in `identity` |

The last one was verified by sabotage: removing one `ExcludeFromMigrations` makes it fail, and
restoring it makes it pass.

---

## 2. Task and RepetitiveTask, as built

Every task — plain, project, recurring — is one row in `task_management.Tasks`.

```
Tasks  1 ──── 0..1  RepetitiveTasks      (FK RepetitiveTasks.TaskId, UNIQUE)
  │
  ├── 1 ──── *  SubTasks                 (Cascade)
  └── 1 ──── *  TaskAssignees            (Cascade)
```

`RepetitiveTasks` holds **only** schedule state:

```
Id, TenantId, TaskId, Frequency, IntervalWeeks, StartTime, EndTime,
WeeklyDays, MonthlyDays, NthOccurrence, NthWeekday, StartDate, EndDate,
NextExecutionAtUtc, LastExecutionAtUtc, IsActive, + 4 audit columns
```

No Title, Description, Priority, AssignedUserId or UserGroupId — those live on the task and are
read through `TaskId`.

**"Is this task recurring?"** is answered by `Recurrence is not null`, not by a stored flag. A
`Task.IsRecurring` column would be derived state that can silently drift from the row it
describes. `TaskItem.IsRecurring` exists as a computed property and is `Ignore`d in EF.

> **One open question.** The UI allows exactly one recurrence configuration per task
> (`TaskFormModal.tsx` keeps a single `recurringConfig`), so this is modelled one-to-zero-or-one
> with a unique index on `TaskId`. If several schedules per task are ever wanted, that unique
> index is the single thing to drop.

### Background job seam

`RepetitiveTasks` is indexed on `(TenantId, IsActive, NextExecutionAtUtc)` for the "what is due
now" scan. `MarkExecuted(executedAtUtc, nextExecutionAtUtc)` advances the schedule and
deactivates it when nothing further is due.

For notifications and SMS the module raises a domain event, `RepetitiveTaskDue`, dispatched by
the existing `DomainEventDispatchInterceptor` after `SaveChanges`. TaskManagement therefore
reaches Notifications without referencing it. **Nothing handles the event yet** — no job, no SMS,
no notification code was written, as instructed.

---

## 3. Tables created

Schema `task_management`, 9 tables:

| Table | Purpose |
|---|---|
| `Tasks` | all tasks; `IsProject` distinguishes project from plain task |
| `SubTasks` | FK `TaskId`, Cascade |
| `RepetitiveTasks` | schedule only; FK `TaskId`, unique |
| `Files` | uploads; `CK_Files_MaxSize` |
| `Tags` | unique per `(TenantId, NormalizedName)` |
| `TaskFiles` | `FileId` + `TaskId`/`SubTaskId`; `CK_TaskFiles_ExactlyOneOwner` |
| `TaskTags` | `TagId` + `TaskId`/`SubTaskId`; `CK_TaskTags_AtLeastOneOwner` |
| `Notes` | personal notes; FK `UserId` |
| `TaskAssignees` | the UI's `teamMemberIds` |

### CHECK constraints, from the generated DDL

```sql
CONSTRAINT [CK_Files_MaxSize]
    CHECK ([FileSizeBytes] > 0 AND [FileSizeBytes] <= 204800)

CONSTRAINT [CK_TaskFiles_ExactlyOneOwner]
    CHECK ((CASE WHEN [TaskId]    IS NOT NULL THEN 1 ELSE 0 END +
            CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END) = 1)

CONSTRAINT [CK_TaskTags_AtLeastOneOwner]
    CHECK ([TaskId] IS NOT NULL OR [SubTaskId] IS NOT NULL)
```

`RepetitiveTaskId` is gone from both junctions: a recurring task is a row in `Tasks`, so its
files and tags hang off `TaskId` like any other task's.

### Foreign keys into the shared identity schema

Six, all `Restrict` — removing a person never silently deletes their work:

| From | To |
|---|---|
| `Tasks.OwnerUserId` | `identity.Users` |
| `Tasks.AssignedUserId` | `identity.Users` |
| `Tasks.AssignedUserGroupId` | `identity.UserGroups` |
| `Notes.UserId` | `identity.Users` |
| `TaskAssignees.UserId` | `identity.Users` |
| `Files.UploadedByUserId` | `identity.Users` |

### Cascade paths

`Tasks → SubTasks` cascades. If `TaskFiles`/`TaskTags` also cascaded from both parents, SQL Server
would see two delete paths to the same row and reject the constraint. So both junctions use
**NoAction** on their task/subtask FKs, and the service must clear those rows in the same
transaction. `TaskFiles → Files` and `TaskTags → Tags` do cascade.

### Tag detachment

One `TaskTags` row may carry both `TaskId` and `SubTaskId`. Detaching the tag from the task must
null that column and keep the row while the other owner survives — deleting it would drop the
surviving link. `TaskTag.DetachTask()` / `DetachSubTask()` / `IsOrphaned` exist so a caller cannot
get that wrong.

The unique indexes are filtered so they forbid duplicates without forbidding the legal
"tagged on the task, and separately on one of its subtasks" pair.

---

## 4. Host registration

| Touch point | Status |
|---|---|
| `NexusCore.sln` | both projects added |
| `Rozet.Api.csproj` | two `ProjectReference`s |
| `AddTaskManagement()` | line 110 |
| `AddTaskManagementInfrastructure(...)` | line 143 |
| `ModuleSchemaInitializer.EnsureCreatedAsync(...)` | line 332 |
| `MapTaskManagementEndpoints()` | **not added** — no endpoints exist yet |

## 5. Verification

| Check | Result |
|---|---|
| `dotnet build NexusCore.sln` | succeeded, 0 errors |
| `dotnet test NexusCore.sln` | 25/25 passed (2 + 23) |
| Migration `InitTaskManagement` | generated |
| Tables created | 9, all in `task_management` |
| `CREATE TABLE [identity]` in DDL | 0 |
| CHECK constraints | 3, verified in generated SQL |
| Cross-schema FKs | 6, verified in generated SQL |
| Migration applied to a database | **no** — script generated and inspected only |
| Other modules changed | none |

The 3 build warnings are pre-existing, in `Ticketing.Domain` and `Events.Infrastructure`.

## 6. Not done, by instruction

Services, endpoints, DTOs, validators, repositories, the background job, notification and SMS
delivery. `Application/Dtos` and `Application/Validators` are empty placeholders.

Still outstanding from the earlier analysis: **C3** the UI allows 20 MB attachments against the
mandated 200 KB; **C4** `TaskComment` and `TaskLog` exist in the UI but are not in the entity
list; **C6** `UserGroups` is feature-flagged off, so `AssignedUserGroupId` has no management UI
until it is enabled.
