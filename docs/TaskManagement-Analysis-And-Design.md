# TaskManagement module — architecture analysis and database design

Status: **partly superseded — see the box below.**

> **SUPERSEDED IN PART.** Two corrections were issued after this document was written and
> they take precedence over anything below that contradicts them:
>
> 1. **Shared vs. business modules.** NexusCore Identity (User, Role, Permission, UserGroup,
>    Tenant) is *shared infrastructure*, not another business module. TaskManagement uses
>    **real foreign keys and navigation properties** into it. Conflict **C2 is resolved that
>    way** — the earlier "Guid reference, no FK" recommendation no longer applies. Module
>    isolation still holds between *business* modules.
> 2. **Task / RepetitiveTask.** Every task — plain, project and recurring — lives in the
>    `Tasks` table. `RepetitiveTask` now holds **only the recurrence schedule** and links back
>    with a real `TaskId` FK (one-to-zero-or-one, unique). Conflict **C1 is resolved that way**.
>    The junction tables therefore drop `RepetitiveTaskId` and carry only `TaskId` / `SubTaskId`.
>
> See `TaskManagement-Implementation.md` for the as-built design.


---

# Part 1 — NexusCore architecture

## 1.1 Real structure

The solution holds **74 projects** and ~19,700 lines of C# (~17,000 excluding migrations). The
folder list in the brief is close but not exact. What is actually there:

**Foundation (`NexusCore.*`)**

| Project | Contents |
|---|---|
| `NexusCore.SharedKernel` | `Entity<TId>`, `AggregateRoot<TId>`, `AuditableEntity<TId>`, `IDomainEvent`, `IDomainEventHandler<T>`, `ICurrentUserContext`, `IUnitOfWork`, `Result`/`Error`/`PagedResult` |
| `NexusCore.Domain` | Identity (`User`, `Role`, `Permission`, `UserGroup`, `Tenant`, tokens), `AuditLog`, `SystemSetting` |
| `NexusCore.Application` | Identity services, permission catalog, approvals contract, endpoint helpers |
| `NexusCore.Infrastructure` | `NexusCoreDbContext`, interceptors, `ModuleSchemaInitializer`, seeding, security |
| `NexusCore.Api` / `NexusCore.AdminUI` | Secondary hosts |
| `NexusCore.Tests`, `Nexus.CompositionTests` | Unit + architecture tests |

**Capability modules — two different styles coexist.**

*Style A — `Nexus.*` (the current, test-enforced style, ~30 projects):*
ProjectManagement (Core, Waterfall, Agile, Team, Deliverables, Kpi, Risk, Stakeholder, Progress,
Documents), Workflow, Knowledge, Strategy, Organization, Calendar, Actions, Portfolio, Reporting,
Integrations.

*Style B — `Chat.*`, `Events.*`, `Ticketing.*`, `Notifications.*`:*
MediatR CQRS (`IRequest`/`IRequestHandler`, Commands/Queries folders) with their own `*.Api` host
projects.

Product hosts compose the reusable NexusCore modules they need. In this repository, reusable
modules remain in Core while product-specific hosts can live in their own repositories.

> **Decision: TaskManagement follows Style A (`Nexus.*`).** It is the style the architecture tests
> police, it is what every recent module uses, and `Nexus.ProjectManagement.Core` is the closest
> functional sibling. Style B is treated as legacy.

## 1.2 The module template

Every Style A capability is exactly two projects:

```
Nexus.<Module>/
  Domain/            entities, enums, domain events
  Application/       Dtos/, I<X>Service + <X>Service, I<X>Repository,
                     I<X>UnitOfWork, Validators/, EventHandlers/
  Endpoints/         <X>Endpoints.cs  (minimal API)
  Permissions/       <X>Permissions.cs + <X>PermissionCatalog
  DependencyInjection.cs        → Add<Module>()

Nexus.<Module>.Infrastructure/
  <Module>DbContext.cs          → also implements I<X>UnitOfWork
  Configurations/               → IEntityTypeConfiguration<T>
  Migrations/
  <X>Repository.cs
  DependencyInjection.cs        → Add<Module>Infrastructure(IConfiguration)
```

## 1.3 Rules that constrain the design

| # | Rule | Evidence |
|---|---|---|
| 1 | **No FK or navigation property into another module's tables.** Cross-module links are bare `Guid` values only. | `Project.cs` doc comment: *"Only Guid references to OrganizationUnit, WorkCalendar, and Users — no navigation properties, no FK into other modules' tables."* |
| 2 | Modules must not reference each other. | `ArchitectureDependencyTests` reads `.csproj` files and fails the build on a stray reference |
| 3 | One **DbContext per module**, all on the same `DefaultConnection`, isolated by **schema**. | `Program.cs` comment + `ToTable("Projects", "project_management")` |
| 4 | Guid PKs, `ValueGeneratedNever()` — ids are created in the domain constructor. | `ProjectConfiguration` |
| 5 | Every aggregate carries `TenantId`; every index is tenant-first. | `HasIndex(x => new { x.TenantId, x.Code })` |
| 6 | `AuditableEntity<Guid>` supplies `CreatedAtUtc`, `CreatedByUserId`, `ModifiedAtUtc`, `ModifiedByUserId`, filled by `AuditingInterceptor`. | `AuditableEntity.cs` |
| 7 | Errors flow through `Result` / `Result<T>` — **not** exceptions. Endpoints call `.ToApiResult()`. | `EndpointResults.cs` |
| 8 | Validation is **FluentValidation**, registered per request DTO. | `AddProjectManagementCore()` |
| 9 | Authorization is permission-based: `PermissionDefinition` + `IPermissionCatalog` singleton, one policy per permission, `RequireAuthorization("Area.Action")`. | `ProjectPermissions.cs` |
| 10 | **No MediatR** in Style A. Service + Repository + UnitOfWork. | no MediatR reference in any `Nexus.*` csproj |
| 11 | **No global soft-delete and no global query filter.** Tenant scoping is explicit in queries. | no `HasQueryFilter` anywhere |
| 12 | Entities are `sealed`, private setters, private parameterless ctor for EF, behaviour methods. | all Style A domain files |

## 1.4 Host registration — the six touch points

Adding a module means editing exactly these places:

1. `NexusCore.sln` — add both module projects
2. Package-generation classification — add the module projects to `eng/pack-core-modules.ps1`
3. Product host application tier — `builder.Services.AddTaskManagement();`
4. Product host infrastructure tier — `builder.Services.AddTaskManagementInfrastructure(builder.Configuration);`
5. Product host endpoints — `app.MapTaskManagementEndpoints();`
6. Product host seed block — `await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<TaskManagementDbContext>(), ct);`

## 1.5 Migrations — an important caveat

`ModuleSchemaInitializer` creates tables through `IRelationalDatabaseCreator.CreateTablesAsync()`,
swallowing SQL error 2714 ("already exists"). Its own doc comment explains why: *"With no EF Core
Migrations tooling available in this environment … A production deployment with dotnet ef tooling
available should replace this with real per-module Migrations."*

Consequences:

- At runtime the host **does not run migrations**. Six projects contain migrations, but they are
  not applied automatically by a product host.
- `CreateTablesAsync` emits the model, **including CHECK constraints** declared via
  `ToTable(t => t.HasCheckConstraint(...))`. So the two mandated CHECK constraints will exist
  either way.
- `dotnet ef 10.0.8` **is** available on this machine, so real migrations can be generated now.

**Plan:** generate a real migration *and* register the `EnsureCreatedAsync` call, matching how
`Nexus.ProjectManagement.Core` already ships both.

## 1.6 Core entities to reuse

| Entity | Shape | Use in TaskManagement |
|---|---|---|
| `User` | `AuditableEntity<Guid>`; TenantId, Email, DisplayName, PasswordHash, IsActive, LastLoginAtUtc, Roles | Referenced by **Guid only** |
| `UserGroup` | TenantId, Name, NormalizedName, Description, IsActive, Permissions, **Members** (`UserGroupMember`: UserGroupId + UserId + JoinedAtUtc) | This is the UI's **Team**. Referenced by Guid only |
| `Tenant` | Name, Slug, Description, IsActive | Source of `TenantId` |
| `Role`, `Permission` | standard | Permission policies |

No new `User`, `Role` or `Team` table is created. **No Core entity needs a new column.**

---

# Part 2 — Requirements extracted from the UI

Source: `F:\Tm` (the TMPBUI task-manager frontend). `src/types.ts` is authoritative; forms confirm
which fields are actually written.

## 2.1 Feature inventory

| Feature | UI location | Backend need |
|---|---|---|
| Create / edit task | `TaskFormModal.tsx` | Task CRUD |
| Create / edit project | same modal, `isProject` toggle + subtask editor | Task + SubTask in one transaction |
| Delete task | `App.tsx` `handleDeleteTask` | Task delete |
| Task detail + comments + history | `TaskDetailModal.tsx` | **Comment + Log entities — not in the brief's entity list** |
| Subtask management | `TaskFormModal`, `TaskCard` | SubTask CRUD, completion toggle |
| Assign to user | `assignedUserId` | Guid reference |
| Assign to team | `assignedTeamId` + `teamMemberIds[]` | UserGroup Guid + assignee join table |
| Status / priority | enums | `TaskStatus`, `Priority` |
| Dates | `dueDate`, `actualCompletionDate`, charter start/end | date columns |
| Attachments | `Attachment` (base64 `dataUrl`) | File + TaskFile |
| Tags | `tags: string[]` | Tag + TaskTag |
| Personal notes | `PersonalNotesView.tsx` | Note |
| Recurring tasks | `RecurringConfig` | RepetitiveTask |
| Search / filter | `FilterBar.tsx` | list endpoint query params |
| Dashboard | `DashboardCharts.tsx` | aggregate endpoint |

## 2.2 Field mapping

### Task (`Task` in `types.ts`)

| UI field | Type | Maps to |
|---|---|---|
| `title` | string, required | `Title` nvarchar(200) |
| `description` | string | `Description` nvarchar(4000) |
| `dueDate` | YYYY-MM-DD | `DueDate` date |
| `actualCompletionDate` | ISO? | `ActualCompletionDateUtc` datetimeoffset null |
| `priority` | low/medium/high | `Priority` enum |
| `status` | todo/in_progress/paused/completed | `Status` enum |
| `isProject` | bool | **`IsProject` bit** |
| `user`, `ownerName`, `ownerAvatar` | string | `OwnerUserId` Guid null (name/avatar resolved at read time) |
| `assignedUserId` + name/avatar | string | `AssignedUserId` Guid null |
| `assignedTeamId` + name | string | `AssignedUserGroupId` Guid null |
| `teamMemberIds[]` | string[] | `TaskAssignee` join table |
| `allowAssigneeStatusUpdate` | bool | `AllowAssigneeStatusUpdate` bit, default 1 |
| `tags[]` | string[] | Tag ← TaskTag |
| `attachments[]` | Attachment[] | File ← TaskFile |
| `projectCharter` | {description, projectManager, startDate, endDate} | 4 owned columns `Charter*` |
| `isRecurring`, `recurringConfig` | — | **moves to RepetitiveTask** (see conflict C1) |
| `comments[]`, `logs[]` | — | out of scope (conflict C4) |
| `createdAt`, `updatedAt` | ISO | `AuditableEntity` |

### SubTask (`ProjectSubTask`)

UI has exactly: `id`, `title`, `startDate?`, `endDate?`, `importance` (low/medium/high, shown as
"وزن ۱/۲/۳"), `completed`, `createdAt`.

The brief also suggests description, status and assignee — **the UI has none of these**, so they
are not included. `SortOrder` is added because the brief explicitly asks for display ordering.

### RepetitiveTask (`RecurringConfig` + task fields)

| UI field | Maps to |
|---|---|
| `frequency` | `Frequency` enum: Daily, Weekly, Monthly, MonthlyDay, MonthlyNthWeekday |
| `intervalWeeks` | `IntervalWeeks` int null |
| `startTime`, `endTime` | `StartTime`, `EndTime` time null |
| `weeklyDays[]` (0=Sat…6=Fri) | `WeeklyDays` primitive collection |
| `monthlyDays[]` (1–31) | `MonthlyDays` primitive collection |
| `nthOccurrence` | `NthOccurrence` enum null |
| `nthWeekday` | `NthWeekday` int null |
| `startDate`, `endDate` | `StartDate` date, `EndDate` date null |
| title/description/priority/assignment | copied from the same form |

`dailyTime` and `time` are legacy fallbacks in the UI; they collapse into `StartTime`.

### Note (`PersonalNote`)

`id`, `userId`, `title`, `content`, `color?`, `isPinned?`, `tags?`, `attachments?`, `createdAt`,
`updatedAt?`. The form writes title, content, color, isPinned, attachments.

---

# Part 3 — Conflicts requiring a decision

## C1 — Recurring tasks: UI model vs. mandated model

`TaskFormModal.tsx:473` shows the UI stores a recurring task **as a Task** with
`isRecurring = true`, and expands its occurrences into `projectSubTasks`. The brief mandates the
opposite: `RepetitiveTask` is a standalone table with **no FK to Task**.

The brief wins — but two consequences follow:

- The frontend must be changed to call separate endpoints; existing recurring data does not map 1:1.
- `Task.isRecurring` / `Task.recurringConfig` are **not** carried onto the Task entity.

## C2 — "FK to User / UserGroup" vs. the no-cross-module-FK rule

The brief asks for a real relationship from `Note` to `User` and from `Task` to `UserGroup`.
Architecture rule 1 forbids FKs across module boundaries, and the architecture tests enforce module
isolation.

**Recommendation:** plain `Guid` columns, indexed, no navigation property and no FK — exactly how
`Project` already stores `ManagerUserId` / `OwnerUserId`. Referential integrity is enforced in the
service layer. This also satisfies the brief's own rule that deleting a User must not cascade into
tasks.

## C3 — File size limit

The brief mandates 200 KB (204,800 bytes) for all attachments. The UI allows **20 MB** for task
attachments (`TaskDetailModal.tsx:210`) and only applies 200 KB to avatars.

**Recommendation:** enforce 204,800 in the backend as instructed, and flag that the UI must be
tightened or uploads will fail at the API.

## C4 — Comments and activity log are missing from the entity list

The UI has fully built `TaskComment` and `TaskLog` features (13 references in `TaskDetailModal`),
plus notifications derived from them. The mandated entity list omits both.

**Recommendation:** leave them out of this stage as instructed, and plan a follow-up. They are not
silently added.

## C5 — Cascade paths (SQL Server limitation)

`TaskFile` and `TaskTag` each hold FKs to `Task`, `SubTask` **and** `RepetitiveTask`, while
`Task → SubTask` is itself cascade. SQL Server rejects multiple cascade paths.

**Recommendation:** `Task → SubTask` = **Cascade**; every FK from `TaskFile` / `TaskTag` to the
three task types = **NoAction**, with the service deleting join rows inside the same transaction.

## C6 — UserGroups is switched off

The product host configuration had `"Features": { "UserGroups": { "Enabled": false } }`, and
`Program.cs` only maps the UserGroup endpoints when it is on. Team assignment therefore has no
management UI unless the flag is enabled.

**Recommendation:** enable the flag, or accept that `AssignedUserGroupId` cannot be populated yet.

---

# Part 4 — Database design

Schema: **`task_management`**. All PKs `uniqueidentifier`, `ValueGeneratedNever`.
All roots carry `TenantId` + the four `AuditableEntity` audit columns.

## 4.1 Tasks

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | No | PK |
| TenantId | uniqueidentifier | No | |
| Title | nvarchar(200) | No | |
| Description | nvarchar(4000) | Yes | |
| **IsProject** | bit | No | default 0 |
| Status | int | No | Todo=0, InProgress=1, Paused=2, Completed=3 |
| Priority | int | No | Low=0, Medium=1, High=2 |
| DueDate | date | No | |
| ActualCompletionDateUtc | datetimeoffset | Yes | |
| OwnerUserId | uniqueidentifier | Yes | Guid ref, no FK |
| AssignedUserId | uniqueidentifier | Yes | Guid ref, no FK |
| AssignedUserGroupId | uniqueidentifier | Yes | Guid ref, no FK |
| AllowAssigneeStatusUpdate | bit | No | default 1 |
| CharterDescription | nvarchar(4000) | Yes | |
| CharterProjectManager | nvarchar(200) | Yes | |
| CharterStartDate | date | Yes | |
| CharterEndDate | date | Yes | |
| CreatedAtUtc / CreatedByUserId / ModifiedAtUtc / ModifiedByUserId | | | audit |

Indexes: `(TenantId, Status)`, `(TenantId, DueDate)`, `(TenantId, AssignedUserId)`,
`(TenantId, AssignedUserGroupId)`, `(TenantId, IsProject)`.

## 4.2 SubTasks

| Column | Type | Null |
|---|---|---|
| Id | uniqueidentifier | No |
| TenantId | uniqueidentifier | No |
| **TaskId** | uniqueidentifier | No — FK → Tasks, **Cascade** |
| Title | nvarchar(200) | No |
| StartDate | date | Yes |
| EndDate | date | Yes |
| Importance | int | No (Low=0, Medium=1, High=2) |
| IsCompleted | bit | No |
| SortOrder | int | No |
| audit columns | | |

Indexes: `(TaskId, SortOrder)`, `(TenantId, IsCompleted)`.

## 4.3 RepetitiveTasks

| Column | Type | Null |
|---|---|---|
| Id, TenantId | uniqueidentifier | No |
| Title | nvarchar(200) | No |
| Description | nvarchar(4000) | Yes |
| Priority | int | No |
| Frequency | int | No |
| IntervalWeeks | int | Yes |
| StartTime / EndTime | time | Yes |
| WeeklyDays / MonthlyDays | nvarchar(max) JSON | Yes — EF 8 primitive collection |
| NthOccurrence | int | Yes |
| NthWeekday | int | Yes |
| StartDate | date | No |
| EndDate | date | Yes |
| OwnerUserId / AssignedUserId / AssignedUserGroupId | uniqueidentifier | Yes |
| AllowAssigneeStatusUpdate | bit | No |
| IsActive | bit | No |
| LastGeneratedAtUtc / NextRunAtUtc | datetimeoffset | Yes — for the future background job |
| audit columns | | |

**No `TaskId`, no `ParentTaskId`** — per the brief.
Index: `(TenantId, IsActive, NextRunAtUtc)` to drive the background job.

## 4.4 Files

| Column | Type | Null |
|---|---|---|
| Id, TenantId | uniqueidentifier | No |
| OriginalFileName | nvarchar(260) | No |
| StoredFileName | nvarchar(260) | No — server-generated, never the user's name |
| ContentType | nvarchar(150) | No |
| FileSizeBytes | int | No |
| StoragePath | nvarchar(1000) | No |
| UploadedByUserId | uniqueidentifier | Yes |
| CreatedAtUtc | datetimeoffset | No |

**CHECK `CK_Files_MaxSize`:** `FileSizeBytes > 0 AND FileSizeBytes <= 204800`

## 4.5 Tags

Id, TenantId, `Name` nvarchar(100) NOT NULL, `NormalizedName` nvarchar(100) NOT NULL,
`Color` nvarchar(30) NULL.
Unique index `(TenantId, NormalizedName)`.

## 4.6 TaskFiles — exactly one owner

| Column | Type | Null | FK |
|---|---|---|---|
| Id | uniqueidentifier | No | PK |
| FileId | uniqueidentifier | No | → Files, Cascade |
| TaskId | uniqueidentifier | Yes | → Tasks, **NoAction** |
| SubTaskId | uniqueidentifier | Yes | → SubTasks, **NoAction** |
| RepetitiveTaskId | uniqueidentifier | Yes | → RepetitiveTasks, **NoAction** |

```sql
CONSTRAINT CK_TaskFiles_ExactlyOneOwner CHECK ((
    CASE WHEN TaskId           IS NOT NULL THEN 1 ELSE 0 END +
    CASE WHEN SubTaskId        IS NOT NULL THEN 1 ELSE 0 END +
    CASE WHEN RepetitiveTaskId IS NOT NULL THEN 1 ELSE 0 END
) = 1)
```

Filtered unique indexes prevent duplicate links:
`(FileId, TaskId) WHERE TaskId IS NOT NULL`, and the same for the other two.

## 4.7 TaskTags — at least one owner

Same columns with `TagId` → Tags (Cascade) and the three optional FKs (NoAction).

```sql
CONSTRAINT CK_TaskTags_AtLeastOneOwner CHECK (
    TaskId IS NOT NULL OR SubTaskId IS NOT NULL OR RepetitiveTaskId IS NOT NULL
)
```

**No exactly-one constraint here** — all seven combinations are legal. Because one row can link a
tag to several owners, removing a tag from one owner must **null that column** (or split the row)
rather than delete the row, so the other links survive.

## 4.8 Notes

Id, TenantId, `UserId` uniqueidentifier NOT NULL (Guid ref, no FK),
`Title` nvarchar(200), `Content` nvarchar(max), `Color` nvarchar(30) NULL,
`IsPinned` bit NOT NULL default 0, audit columns.
Index `(TenantId, UserId, IsPinned)`.

## 4.9 TaskAssignees

Join table for `teamMemberIds[]`: Id, TaskId (FK → Tasks, Cascade), UserId (Guid ref).
Unique `(TaskId, UserId)`.

## 4.10 Relationship diagram

```
Tenant ──< Task ──< SubTask                (Task→SubTask: 1:N, Cascade)
             │
             ├──< TaskAssignee  (UserId → Guid ref to Identity.Users)
             │
   RepetitiveTask   (standalone — no FK to Task)
             │
   File ──< TaskFile >── Task | SubTask | RepetitiveTask     (exactly one)
   Tag  ──< TaskTag  >── Task | SubTask | RepetitiveTask     (one or more)

   Note ── UserId (Guid ref)

   Guid-only refs, no FK: OwnerUserId, AssignedUserId, UploadedByUserId,
                          Notes.UserId, TaskAssignee.UserId  → NexusCore.Users
                          AssignedUserGroupId                → NexusCore.UserGroups
```

## 4.11 Business rules for the service layer

- `Title` required, trimmed, ≤ 200.
- A project (`IsProject = true`) must have **≥ 1 SubTask**. Creation of project + initial subtasks
  happens in **one transaction**; a failed subtask rolls the project back.
- Deleting the last SubTask of a project is rejected.
- Flipping `IsProject` false→true requires at least one SubTask to exist.
- Normal tasks are never subject to the subtask rule.
- `EndDate >= StartDate`; `DueDate` required.
- File upload rejected above 204,800 bytes — checked against the **actual stream length**, not the
  client-reported size. Stored name is server-generated.
- Every query filters by `TenantId` from `ICurrentUserContext`.

## 4.12 Proposed folder layout

```
Nexus.TaskManagement/
  Domain/        TaskItem, SubTask, RepetitiveTask, TaskFile, TaskTag,
                 Note, TaskAssignee, TaskStatus, TaskPriority,
                 SubTaskImportance, RecurrenceFrequency, OccurrenceNth,
                 TaskEvents
  Application/   Dtos/, ITaskService/TaskService, INoteService/NoteService,
                 ITaskRepository, ITaskManagementUnitOfWork, Validators/
  Endpoints/     TaskEndpoints, SubTaskEndpoints, RepetitiveTaskEndpoints,
                 TagEndpoints, FileEndpoints, NoteEndpoints
  Permissions/   TaskManagementPermissions
  DependencyInjection.cs

Nexus.TaskManagement.Infrastructure/
  TaskManagementDbContext.cs
  Configurations/   one per entity
  Migrations/
  TaskRepository.cs
  DependencyInjection.cs
```

`TaskItem` is the C# class name (avoids `System.Threading.Tasks.Task`); the table stays `Tasks`.

## 4.13 Permissions

`Tasks.View`, `Tasks.Create`, `Tasks.Edit`, `Tasks.Delete`, `Tasks.Assign`,
`Tasks.ManageRecurring`, `Tasks.ManageTags`, `Tasks.UploadFiles`, `Notes.Manage`
— registered through a `TaskManagementPermissionCatalog : IPermissionCatalog`.

---

# Part 5 — Isolation status

| Item | Value |
|---|---|
| Working copy | `F:\NexusCore-TaskManagement` |
| Git remotes | **none** |
| History | fresh `git init`, 1 baseline commit, not a fork |
| Upstream commits / pushes / branches / PRs / tags | **none** |
| Read-only clone of the original | deleted |
| Secrets | none found — connection strings use `Trusted_Connection=True`, JWT key is a placeholder |
