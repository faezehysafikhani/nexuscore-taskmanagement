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

### Phase 1 — added to the backend

| Where | What | Why (UI need) |
|---|---|---|
| Calendar | `workHoursPerDay`, `applyOfficialHolidays` on work calendars; `GET .../{id}/days`; `GET .../official-holidays`; optional `IOfficialHolidayProvider`; new plug-in `Nexus.Calendar.IranianHolidays`; Waterfall calendar integration honours it | The UI's calendar screen has work hours and an official-holidays switch, and computed holidays in the browser (`getOfficialHolidayReason`) — that logic now lives in the backend |
| Organization | `path` on units; `activeOnly`; cycle guard when moving a unit; unit membership (`GET /api/organization/members`, `PUT /api/organization/users/{id}/unit`) | The UI keeps each user's `owningUnit` as a path string and builds the tree itself; the backend now owns membership and paths |
| Calendar | create/update accept `exceptions` (replace-all); one default calendar per tenant; `DELETE` calendar (permission `work_calendars.delete`) refused while in use via `ICalendarUsageChecker` (Actions, projects) | The UI saves a whole calendar at once, keeps one default, and blocks deleting an assigned calendar — rules that lived in the browser |
| Projects | `ListProjectsRequest.WorkCalendarId` filter (internal) | lets the project-calendar integration answer "is this calendar used?" |
| Calendar | the default calendar cannot be deleted (409) | the UI hid the delete button for it - now the backend's rule |
| Organization | `code` optional on create (auto `U0001`...); `DELETE` deactivates the whole branch and refuses while people are in it | the UI generated codes and walked the subtree itself when deleting |
| Identity (host config) | `PostBank.Api/appsettings.json` seeds roles **Project Manager** and **Project Team** with permission sets (created once, never overwritten) | the UI's three roles are backend roles; the "role" dropdown assigns them |
| Host | `PostBank.Api` registers the Iranian holidays and the project-calendar integration | So calendars actually affect scheduling |
| SQL | `2026-10-10-add-calendar-policy.sql`, `2026-10-10-add-organization-members.sql` | schema |
