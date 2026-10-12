# Connecting the PMPB UI to the NexusCore backend

UI repository: `faezehysafikhani/pmpb` (React 19 + Vite, an AI Studio export). Backend: this repository (`PostBank.Api` host).

## Decisions (from the product owner)

1. **Backend is the only place for business logic.** The UI renders, collects input and calls the API. Everything it currently computes
   itself (schedule/CPM, progress roll-up, planned progress, performance, S-curve, visibility, approval routing, recurrence expansion,
   validation rules) moves to — or is already in — the backend.
2. **The backend Workflow module is the single source of truth for approvals.** The UI's per-record stage/approver/history fields go away; the
   approval centre, delegation and pending lists come from Workflow.
3. **Where the UI has a field or feature the backend lacks, extend the backend** (additive, nullable, with a SQL script) and report it at the end.
4. AI features (UI `server.ts` + Gemini) are out of scope and stay untouched.
5. Nothing existing may break: the UI keeps working while it is migrated module by module.

## What the UI is today

* A single-page app whose whole state lives in `App.tsx` (`useState` + `localStorage`, seeded from `data.ts`). It makes **no** calls to a business API;
  its only server calls are the AI endpoints in its own `server.ts`. Login is a mock (admin/admin).
* IDs are numbers, `Project` is one big nested object (risks, stakeholders, schedule, kanban tasks, documents, contracts, invoices, status updates,
  delay reasons, snapshots, per-project workflow/tab/permission settings), approvals are embedded in every record.
* ~1,460 lines of business logic in `utils.ts` and large handler blocks in `App.tsx`.

## Architecture

* `server.ts` (UI BFF) proxies `/api/*` to the backend (`VITE_API_BASE_URL`, default `http://localhost:5151`), as NexusCore's own UI does — no CORS problems.
* `services/api.ts` — HTTP client (JWT, refresh, `ApiResult` unwrapping, Persian error messages) and one typed service file per backend area.
* IDs become strings (backend Guids) in the UI types; no id-mapping registry.
* Data access through small hooks per area (`useProjects`, `useRisks`, ...) instead of one global `App.tsx` state; mutations call the API and refresh.
* Mock/localStorage code is deleted as each area is migrated (not kept as a second source of truth).

## Phases

| # | Area | UI features | Backend work known so far |
|---|---|---|---|
| 1 | Foundation | real login, current user/permissions, users & roles, org-unit tree, calendars (+holidays), reference tables, settings | UI roles (Admin / Project Manager / Project Team) as permission sets; official-holiday list; user `owningUnit` as an Organization unit reference |
| 2 | Projects | create/edit charter, portfolio, details, delete, dashboards hub, per-project tab/permission settings | charter fields (constraints & assumptions, goal, requirements, ...), `approver1`, enabled tabs, per-project workflow overrides |
| 3 | Registers | risks (+response execution/verification, definitions, matrix colours), stakeholders (+strategies text), status updates, delay reasons, documents | risk response workflow fields, status-update entity, stakeholder strategy text, document `date` |
| 4 | Schedule & agile | WBS/activities, dependencies (FS/SS/FF/SF + lag), weights (manual/time/cost/complexity/combined), baselines & snapshots, S-curve, MS Project import/export, kanban + sprints, progress submission/approval | activity cost / man-hours / importance / complexity / approvers 1-3, weighting modes, kanban approvers |
| 5 | Actions & strategy | actions (+recurrence with weekdays / day-of-month / months, occurrences, source risk/stakeholder), strategies, alignment matrix, knowledge library | richer recurrence, action approvers 1-3, source links |
| 6 | Contracts | contracts, amendments, invoices, per-project permissions | contractor / supervisor / beneficiary names, amendment dates, invoice title & period, contract permissions |
| 7 | Governance | workflow stage editor (global + per project), approval centre, delegations, visibility settings | stage-based Workflow definitions with role placeholders (PM / owner), per-item delegation, visibility toggles |
| 8 | Reports | dashboards, performance, S-curve, exports | all calculations served by the backend (EVM, unit/PM reports already exist) |
| 9 | Clean-up | remove mock data/localStorage, end-to-end pass, docs | — |

Each phase: (a) gap analysis for the screens in it, (b) backend additions + tests + SQL script, (c) UI wiring, (d) verification, (e) commit.

## Backend additions log

(Filled in as each phase is built — this is the list to report at the end.)

### Compatibility rule (applies to every phase)

Other projects already use these modules. So: new fields/endpoints/tables are additive; nothing a module did before changes unless the
caller asks for it (a trailing optional parameter) or the host switches it on (an options object, off by default, switched on only in
`PostBank.Api`); a module never gains a required dependency on another (extension-point interfaces, integrations, or a separate opt-in
project). Guarded by `ArchitectureDependencyTests.SharedModules_KeepTheirOriginalDependencies` and by tests of the default (off) behaviour.

Switched on in the host: `CalendarOptions.SingleDefaultPerTenant`, `WaterfallOptions.ValidateActivityHierarchy`. Analytics lives in the
separate `Nexus.Reporting.Analytics` project.

### Phase 1 — added to the backend

| Where | What | Why (UI need) |
|---|---|---|
| Calendar | `workHoursPerDay`, `applyOfficialHolidays` on work calendars; `GET .../{id}/days`; `GET .../official-holidays`; optional `IOfficialHolidayProvider`; new plug-in `Nexus.Calendar.IranianHolidays`; Waterfall calendar integration honours it | The UI's calendar screen has work hours and an official-holidays switch, and computed holidays in the browser (`getOfficialHolidayReason`) — that logic now lives in the backend |
| Organization | `path` on units; `activeOnly`; cycle guard when moving a unit; unit membership (`GET /api/organization/members`, `PUT /api/organization/users/{id}/unit`) | The UI keeps each user's `owningUnit` as a path string and builds the tree itself; the backend now owns membership and paths |
| Calendar | create/update accept `exceptions` (replace-all); one default calendar per tenant (**opt-in**: `CalendarOptions.SingleDefaultPerTenant`, on only in the host); `DELETE` calendar (permission `work_calendars.delete`) refused while in use via `ICalendarUsageChecker` (Actions, projects) | The UI saves a whole calendar at once, keeps one default, and blocks deleting an assigned calendar — rules that lived in the browser |
| Projects | `ListProjectsRequest.WorkCalendarId` filter (internal) | lets the project-calendar integration answer "is this calendar used?" |
| Calendar | the default calendar cannot be deleted (409) | the UI hid the delete button for it - now the backend's rule |
| Organization | `code` optional on create (auto `U0001`...); `DELETE` refuses while people are in the unit; deactivating the whole branch is opt-in (`?includeBranch=true`) | the UI generated codes and walked the subtree itself when deleting |
| Identity (host config) | `PostBank.Api/appsettings.json` seeds roles **Project Manager** and **Project Team** with permission sets (created once, never overwritten) | the UI's three roles are backend roles; the "role" dropdown assigns them |
| Host | `PostBank.Api` registers the Iranian holidays and the project-calendar integration | So calendars actually affect scheduling |
| SQL | `2026-10-10-add-calendar-policy.sql`, `2026-10-10-add-organization-members.sql` | schema |


### Phase 2 — added to the backend (projects, charter, portfolio)

| Where | What | Why (UI need) |
|---|---|---|
| Projects | `PUT /projects/{id}/status` (Draft/Active/OnHold/Completed; Archived only via archive) | The UI lets a user set the project's work status; the backend had no way to change it |
| Projects | `DELETE /projects/{id}` + permission `Projects.Remove`, guarded by `IProjectDeletionParticipant` (409 with what still depends on it); `Nexus.Integrations.ProjectDeletion` for Team/Deliverables/KPIs/Actions | The UI has a "delete project" screen; this is also the deferred "hard delete guard" item for projects |
| Projects | `ProjectOptions.ValidateCharter` (opt-in, on in the host): required manager/unit/dates/goal/requirements + meaningless-text check, with per-rule error codes | The UI enforced these in the browser (including the "junk text" patterns) |
| Portfolio | `GET /api/portfolio/projects`: the projects the caller may see, in full | The UI filtered the project list itself (`isProjectVisibleToUser`); visibility now comes from the backend |
| Projects | `ProjectOptions.RequireUniqueName` (opt-in, on in the host), code `project.name_exists` | The UI refused a duplicate project name in the browser |
| Projects | `POST /projects/check` (dry run of create/update) | The form validated before the (AI) charter check and before saving; the same rules now answer from the backend without saving |
| Projects | `code` optional on create (automatic `P0001`...) | The project form has no code field |
| Team | `POST /team/members/for-projects`, `PUT /team/members/set` | The project form edits the team as a list of people: the backend now applies the list (no add/remove diffing in the browser) and the list screens read everybody's team in one call |
| Host | `AddProjectDeletionIntegration()`, `ValidateCharter = true`, `RequireUniqueName = true`; seeded roles Project Manager / Project Team also get `users.view` (the project forms list people) | |

Not added (deliberately, a later phase): the per-tenant on/off switches for the visibility sources (UI `visibilitySettings`) - phase 7.

### Phase 3 — added to the backend (registers)

| Where | What | Why (UI need) |
|---|---|---|
| Risk | `ResponseStatus` (Planning/InProgress/OnHold/Completed) on a risk + `PUT /risks/{id}/response-status` | The UI tracks execution of a risk's response plan; the backend only had the plan text |
| Risk | low/medium/high `probabilityLevel`/`impactLevel`, `level` colour band in responses; levels accepted on create/update (stored as the 1/3/5 scores) | The UI speaks in three bands; the 1–5 scores/RPN stay the stored truth, the mapping lives in the backend |
| Risk | per-project settings (`GET/PUT /risks/settings`, `POST /settings/for-projects`): band definitions + matrix cell colours; table `RiskProjectSettings` | The UI lets a project manager reword the bands and recolour the matrix |
| Stakeholder | `quadrant` and resolved `strategy` in responses; per-project strategy wording per quadrant (`/stakeholders/settings`); table `StakeholderProjectSettings` | The UI showed a strategy text per quadrant (computed in the browser) and let a project edit it |
| Risk / Stakeholder / Progress / DelayReason / Documents | `POST .../for-projects` batch lists | Task centre, approval centre and portfolio screens read every project's items |
| Workflow | `POST /api/workflow/approval-center/for-subjects` | Each register item shows its approval history and approver; the browser no longer keeps its own approval log |
| Documents | `.DisableAntiforgery()` on the two file-upload endpoints (document and new version) | A multipart upload from the UI would be rejected by .NET 8's antiforgery check in a host that does not run the antiforgery middleware (the other upload endpoints already do this); bearer-token API, no cookie to forge |
| SQL | `2026-10-12-add-risk-response-and-settings.sql`, `2026-10-12-add-stakeholder-settings.sql` (+ `RiskSchemaUpgrade`, `StakeholderSchemaUpgrade`) | Additive, guarded, idempotent |

Not added (deliberately): AI-generated document fields (summary/relevance stay UI-local, AI untouched); status-update planned/actual numbers are supplied by the UI until phase 4 computes them from the schedule.

### Phase 4 — added to the backend (schedule, agile, progress)

| Where | What | Why (UI need) |
|---|---|---|
| Waterfall | Activity `cost`, `importance`, `complexity`, `position` | The schedule table edits them; cost and complexity drive the weighting modes, position keeps the order activities were placed in (WBS numbering) |
| Waterfall | Per-project settings (`/waterfall/settings`): weighting mode (manual/time/cost/complexity/combined) + factors, planned-progress mode (stored/continuous/zero-hundred); host defaults `DefaultPlannedProgress`, `RollUpSummaryWeights` | The UI computed weights and planned progress in the browser (`calculateScheduleTreeAndProgress`); the rules now live in the backend and the screens read the result |
| Waterfall | Schedule DTO: WBS code, level, effective weight, rolled-up cost/man-hours; `GET /schedule/plan`, `POST /schedule/plans` | The tree table and dashboards need the whole plan in one call; WBS codes were also computed in the browser |
| Waterfall | `PUT /dependencies/predecessor` | The screen gives an activity one prerequisite with a type and a lag; the backend replaces the links instead of the browser diffing them |
| Waterfall | `DELETE /activities/{id}?withSubActivities=true` | The "delete activity" screen deletes the whole branch |
| Waterfall | Saved copies (`baselines`) gain `kind` (baseline / progress information) and keep per-activity weight, progress, owner and outline code | The UI's "schedule snapshots" are full copies shown on a page of their own; both kinds are now stored by the backend |
| Waterfall | Activity progress reports (`progress-submissions`), decided in the approval workflow; permission `WaterfallProgress.Submit` | Team members report progress and an approver confirms it (optionally a different figure; a partial rejection keeps it) |
| Agile | Status requests (`status-submissions`), decided in the approval workflow; permission `AgileStatus.Submit` | Moving a card needs confirmation; the backend only had direct status changes |
| Agile | `PUT /sprints/count`, `POST` batch lists for tasks / sprints / requests | The project has a "number of sprints" setting; the batch lists feed the task centre and dashboards |
| Core | `POST /projects/progress` + `IProjectProgressSource` (registered by Waterfall and Agile) | Portfolio, dashboards and reports show planned / actual progress per project; the browser calculated them from its own copy of the schedule |
| Host | `PostBank.Api`: `DefaultPlannedProgress = Continuous`, `RollUpSummaryWeights = true`; seeded roles get the two new permissions | The UI's behaviour |
| SQL | `2026-10-13-add-waterfall-planning.sql`, `2026-10-13-add-agile-status-submissions.sql` (+ `WaterfallPlanningSchemaUpgrade`, `AgileTaskStatusSchemaUpgrade`) | Additive, guarded, idempotent |

Not added (deliberately): per-activity second/third approvers (the screens only ever use the first; stage-based approval is the Workflow module's job, phase 7); AI WBS generation stays as it is (its result is applied through the normal create calls).
