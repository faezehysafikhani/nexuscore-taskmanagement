# NexusCore — New Modules API Reference

This document covers every API endpoint added by the 20 new Nexus Modules composed into the
**NexusCore.Api** host (`Program.cs`). It does not cover the pre-existing NexusCore foundation
endpoints (Identity/Auth, Users, Roles, Tenants, Permissions, Platform/Settings/Audit) or the
pre-existing Chat/Ticketing/Notifications/Events modules — those were not part of this work.

**Base URL**: whatever host/port NexusCore.Api is running on (e.g. `http://localhost:5005` in the
default `dotnet run` dev profile).

**Live OpenAPI/Swagger JSON**: `GET /swagger/v1/swagger.json` — **Swagger UI**: `GET /swagger`
(both confirmed reachable by actually running the app during this work; see the final report for
details). The JSON document is the authoritative source for exact route templates, path/query
parameter binding, and request body shapes (all generated directly from the live route table).
It does **not** show response bodies, because every endpoint returns the shared `IResult`-typed
`ToApiResult()` helper rather than a strongly-typed `Results<Ok<T>, ...>` — Swashbuckle can't
infer a return schema from that, so every operation shows a bare `"200": { "description": "OK" }`
with no body schema. The response shapes documented below come directly from each module's own
DTO source and service interface, not from Swagger.

**Authentication**: every endpoint below requires a valid JWT bearer token (`Authorization: Bearer
<token>`, obtained from `POST /api/identity/auth/login` — a pre-existing NexusCore endpoint) *and*
the specific permission named under each module. Missing/invalid token → `401`. Valid token but
missing permission → `403`.

**Common response envelope**: every endpoint here goes through the same `Result` → `IResult`
mapping (`NexusCore.Application.Common.EndpointResults.ToApiResult()`):
- `Result<T>` success → `200 OK` with `T` as the JSON body.
- `Result` (no payload) success → `204 No Content` (empty body).
- Failure → `400/404/409` (see below) with an RFC 7807 Problem Details body:
  `{ "type": "...", "title": "<error code>", "status": <code>, "detail": "<message>" }`.
  Error-code → status mapping: `validation.error`→400, `not_found`→404, `conflict`→409,
  `unauthorized`→401, anything else→400.

**Enums**: every enum is serialized as its **raw integer** value (no string enum converter is
configured), ordinal 0-based in declaration order unless noted. See the [Enums Reference](#enums-reference)
at the end for every value's meaning.

---

## Table of Contents

1. [Organization](#1-organization)
2. [Calendar](#2-calendar)
3. [Workflow](#3-workflow) (definitions + approval center)
4. [Actions](#4-actions)
5. [Knowledge](#5-knowledge)
6. [Strategy](#6-strategy)
7. [Projects (ProjectManagement.Core)](#7-projects-projectmanagementcore)
8. [Waterfall Activities](#8-waterfall-activities)
9. [Agile Tasks](#9-agile-tasks)
10. [Project Team](#10-project-team) (members + governance roles)
11. [Deliverables](#11-deliverables)
12. [KPI](#12-kpi)
13. [Risks](#13-risks)
14. [Stakeholders](#14-stakeholders)
15. [Progress](#15-progress)
16. [Project Documents](#16-project-documents)
17. [Project Workflow (integration)](#17-project-workflow-integration)
18. [Project-Strategy Alignment (integration)](#18-project-strategy-alignment-integration)
19. [Portfolio](#19-portfolio)
20. [Reporting](#20-reporting)
21. [Enums Reference](#enums-reference)

---

## 1. Organization

Permission namespace: `Organization.*` (`View`, `Create`, `Update`, `Delete`). Base route: `/api/organization/units`.

### Module: Organization
Method: GET
Route: /api/organization/units
Description: List all organization units for a tenant (flat list; hierarchy is expressed via ParentId, not nesting).

Path Parameters: none
Query Parameters:
- tenantId (Guid, required)

Request Body: none

Response:
```json
[
  {
    "id": "guid",
    "tenantId": "guid",
    "name": "string",
    "code": "string",
    "parentId": "guid | null",
    "managerUserId": "guid | null",
    "isActive": true
  }
]
```

Status Codes: 200, 401, 403

---

### Module: Organization
Method: GET
Route: /api/organization/units/{id}
Description: Get one organization unit by id.

Path Parameters:
- id (Guid, required)

Query Parameters: none
Request Body: none

Response:
```json
{ "id": "guid", "tenantId": "guid", "name": "string", "code": "string", "parentId": "guid | null", "managerUserId": "guid | null", "isActive": true }
```

Status Codes: 200, 401, 403, 404

---

### Module: Organization
Method: POST
Route: /api/organization/units
Description: Create an organization unit.

Path Parameters: none
Query Parameters: none

Request Body:
```json
{
  "tenantId": "guid",
  "name": "string",
  "code": "string",
  "parentId": "guid | null"
}
```

Response: same shape as GET by id (the created unit, `isActive: true`, `managerUserId: null`).

Status Codes: 200, 400, 401, 403

---

### Module: Organization
Method: PUT
Route: /api/organization/units/{id}
Description: Update an organization unit's name/code/parent/manager/active flag.

Path Parameters:
- id (Guid, required)

Query Parameters: none

Request Body:
```json
{
  "name": "string",
  "code": "string",
  "parentId": "guid | null",
  "managerUserId": "guid | null",
  "isActive": true
}
```

Response: the updated unit (same shape as GET by id).

Status Codes: 200, 400, 401, 403, 404

---

### Module: Organization
Method: DELETE
Route: /api/organization/units/{id}
Description: Deactivate an organization unit (soft delete — sets IsActive=false, does not remove the row).

Path Parameters:
- id (Guid, required)

Query Parameters: none
Request Body: none
Response: empty body.

Status Codes: 204, 401, 403, 404

---

## 2. Calendar

Permission namespace: `Calendar.*` (`View`, `Create`, `Update`). Base route: `/api/calendar/work-calendars`.
`WorkingDays` is a `[Flags]` bitmask — see [Enums Reference](#enums-reference) for `DayOfWeekMask`.

### Module: Calendar
Method: GET
Route: /api/calendar/work-calendars
Description: List all work calendars for a tenant.

Query Parameters:
- tenantId (Guid, required)

Request Body: none

Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "name": "string", "description": "string | null",
    "workingDays": 79,
    "isDefault": true,
    "exceptions": [ { "id": "guid", "date": "2026-01-01", "isWorkingDay": false, "description": "string | null" } ]
  }
]
```

Status Codes: 200, 401, 403

---

### Module: Calendar
Method: GET
Route: /api/calendar/work-calendars/{id}
Description: Get one work calendar with its exception dates.

Path Parameters:
- id (Guid, required)

Response: same shape as one item of the list above.

Status Codes: 200, 401, 403, 404

---

### Module: Calendar
Method: GET
Route: /api/calendar/work-calendars/{id}/is-working-day
Description: Business-rule check — is the given date a working day under this calendar (accounts for the WorkingDays mask and any per-date exception override).

Path Parameters:
- id (Guid, required)

Query Parameters:
- date (DateOnly, required, format `YYYY-MM-DD`)

Request Body: none
Response:
```json
true
```
(a bare JSON boolean)

Status Codes: 200, 401, 403, 404

---

### Module: Calendar
Method: POST
Route: /api/calendar/work-calendars
Description: Create a work calendar.

Request Body:
```json
{ "tenantId": "guid", "name": "string", "workingDays": 79, "isDefault": false }
```

Response: the created calendar (same shape as GET by id, empty `exceptions`).

Status Codes: 200, 400, 401, 403

---

### Module: Calendar
Method: PUT
Route: /api/calendar/work-calendars/{id}
Description: Update a work calendar's name/description/working-days mask/default flag.

Path Parameters:
- id (Guid, required)

Request Body:
```json
{ "name": "string", "description": "string | null", "workingDays": 79, "isDefault": false }
```

Response: the updated calendar.

Status Codes: 200, 400, 401, 403, 404

---

### Module: Calendar
Method: POST
Route: /api/calendar/work-calendars/{id}/exceptions
Description: Add a date-specific exception (override a normally-working day as off, or vice versa).

Path Parameters:
- id (Guid, required)

Request Body:
```json
{ "date": "2026-03-20", "isWorkingDay": false, "description": "string | null" }
```

Response: the parent calendar with the new exception included.

Status Codes: 200, 400, 401, 403, 404

---

### Module: Calendar
Method: DELETE
Route: /api/calendar/work-calendars/{id}/exceptions/{exceptionId}
Description: Remove a date exception.

Path Parameters:
- id (Guid, required)
- exceptionId (Guid, required)

Response: the parent calendar without that exception.

Status Codes: 200, 401, 403, 404

---

## 3. Workflow

Generic, reusable approval-workflow engine — not specific to Project Management. Permission
namespace: `Workflow.*` (`View`, `Configure`, `Approve`, `Reject`). Two route groups:
`/api/workflow/definitions` (configuration) and `/api/workflow/approval-center` (acting on pending approvals).

A `WorkflowDefinition` applies either generically (`scopeType: "General"`, `scopeId: null`) or
scoped to one entity (e.g. one Project, via the [Project Workflow integration](#17-project-workflow-integration)).
When both exist for the same `subjectType`, the scoped one wins.

### Module: Workflow
Method: GET
Route: /api/workflow/definitions
Description: List workflow definitions for a tenant, optionally filtered by subject type (e.g. "Project", "Risk").

Query Parameters:
- tenantId (Guid, required)
- subjectType (string, optional)

Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "name": "string",
    "subjectType": "string", "scopeType": "General | Project", "scopeId": "guid | null",
    "isActive": true,
    "steps": [ { "id": "guid", "order": 1, "name": "string", "approverUserId": "guid | null", "approverRoleId": "guid | null" } ]
  }
]
```

Status Codes: 200, 401, 403

---

### Module: Workflow
Method: GET
Route: /api/workflow/definitions/{id}
Description: Get one workflow definition with its ordered steps.

Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Workflow
Method: POST
Route: /api/workflow/definitions
Description: Create a workflow definition (starts with zero steps — add them via the steps endpoint below).

Request Body:
```json
{ "tenantId": "guid", "name": "string", "subjectType": "string", "scopeType": "string | null", "scopeId": "guid | null" }
```

Response: the created definition, `steps: []`.
Status Codes: 200, 400, 401, 403

---

### Module: Workflow
Method: POST
Route: /api/workflow/definitions/{id}/steps
Description: Append an approval step (approver is either a specific user or anyone holding a given role).

Path Parameters: id (Guid, required)
Request Body:
```json
{ "name": "string", "approverUserId": "guid | null", "approverRoleId": "guid | null" }
```
Response: the definition with the new step appended.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Workflow
Method: DELETE
Route: /api/workflow/definitions/{id}/steps/{stepId}
Description: Remove a step from a definition.

Path Parameters: id (Guid, required), stepId (Guid, required)
Response: the definition without that step.
Status Codes: 200, 401, 403, 404

---

### Module: Workflow
Method: PUT
Route: /api/workflow/definitions/{id}/steps/{stepId}/move
Description: Reorder a step to a new position among its siblings.

Path Parameters: id (Guid, required), stepId (Guid, required)
Request Body:
```json
{ "newOrder": 2 }
```
Response: the definition with steps re-ordered.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Workflow
Method: POST
Route: /api/workflow/definitions/{id}/reset-to-default
Description: Deactivate/reset a Project-scoped override definition, so that subject type falls back to the General definition again.

Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

### Module: Workflow - Approval Center
Method: GET
Route: /api/workflow/approval-center
Description: List workflow instances currently pending a decision from the caller (resolved from the JWT's user id, not a query parameter).

Query Parameters: tenantId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "workflowDefinitionId": "guid",
    "subjectType": "string", "subjectId": "guid",
    "totalSteps": 3, "currentStepOrder": 2, "status": 0,
    "decisions": [ { "id": "guid", "stepOrder": 1, "decidedByUserId": "guid", "approved": true, "comment": "string | null", "decidedAtUtc": "2026-01-01T00:00:00Z" } ]
  }
]
```
`status` is `WorkflowInstanceStatus` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Workflow - Approval Center
Method: GET
Route: /api/workflow/approval-center/{id}
Description: Get one workflow instance with its full decision history.

Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Workflow - Approval Center
Method: POST
Route: /api/workflow/approval-center/{id}/approve
Description: Record an approval for the current step. On the final step this resolves the whole instance to Approved and fires the owning module's ApprovalGranted handler (e.g. a Project moves to Active).

Path Parameters: id (Guid, required)
Request Body:
```json
{ "comment": "string | null" }
```
Response: the updated instance (see shape above).
Status Codes: 200, 400, 401, 403, 404

---

### Module: Workflow - Approval Center
Method: POST
Route: /api/workflow/approval-center/{id}/reject
Description: Reject at the current step — resolves the whole instance to Rejected immediately (rejection short-circuits, does not require reaching the final step).

Path Parameters: id (Guid, required)
Request Body:
```json
{ "comment": "string | null" }
```
Response: the updated instance.
Status Codes: 200, 400, 401, 403, 404

---

## 4. Actions

Fully standalone action-item tracker — usable with or without Project Management installed
(`projectId` is always optional). Permission namespace: `Actions.*` (`View`, `Create`, `Edit`, `Submit`).
Base route: `/api/actions`.

Every action has a `priority` (see [ActionPriority](#actionpriority-actions): Low 0, Normal 1, High 2, Urgent 3), returned on
every action. Create takes an optional `priority` (omitted = Normal); update takes an optional `priority`
(omitted = leave unchanged). **Existing databases need `docs/upgrade/2026-10-03-add-action-priority.sql` first.**

### Module: Actions
Method: GET
Route: /api/actions
Description: List actions for a tenant, optionally filtered to one project.

Query Parameters: tenantId (Guid, required), projectId (Guid, optional)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "title": "string", "description": "string | null",
    "ownerUserId": "guid | null", "responsibleUserId": "guid | null", "status": 0,
    "organizationUnitId": "guid", "workCalendarId": "guid", "projectId": "guid | null",
    "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null", "approvalStatus": 0
  }
]
```
`status` is `ActionStatus`, `approvalStatus` is `ApprovalStatus` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Actions
Method: GET
Route: /api/actions/{id}
Description: Get one action.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Actions
Method: POST
Route: /api/actions
Description: Create an action. OrganizationUnitId and WorkCalendarId are required and validated to exist; ProjectId is optional.

Request Body:
```json
{
  "tenantId": "guid", "title": "string", "description": "string | null",
  "ownerUserId": "guid | null", "responsibleUserId": "guid | null",
  "organizationUnitId": "guid", "workCalendarId": "guid", "projectId": "guid | null",
  "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null"
}
```
Response: the created action, `status: 0 (Open)`, `approvalStatus: 0 (NotSubmitted)`.
Status Codes: 200, 400, 401, 403, 404 (if organizationUnitId/workCalendarId/projectId don't exist)

---

### Module: Actions
Method: PUT
Route: /api/actions/{id}
Description: Update an action's details.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`.
Response: the updated action.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Actions
Method: PUT
Route: /api/actions/{id}/status
Description: Change only the action's status.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "status": 1 }
```
Response: the updated action.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Actions
Method: POST
Route: /api/actions/{id}/submit-for-approval
Description: Submit for approval. If Workflow is installed and has an applicable definition (subjectType "Action"), routes through it; otherwise auto-approves.
Path Parameters: id (Guid, required)
Response: the updated action (approvalStatus reflects the outcome).
Status Codes: 200, 401, 403, 404

---

## 5. Knowledge

Standalone document/knowledge library — no dependency on Project Management. Permission
namespace: `Knowledge.*` (`View`, `Upload`, `Edit`, `Delete`). Base route: `/api/knowledge/documents`.

### Module: Knowledge
Method: GET
Route: /api/knowledge/documents
Description: Search knowledge documents by free-text title/description match and/or document type.

Query Parameters: tenantId (Guid, required), search (string, optional), documentType (DocumentType enum, optional)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "title": "string", "description": "string | null",
    "documentType": 0, "fileName": "string", "contentType": "string", "sizeBytes": 12345,
    "createdAtUtc": "2026-01-01T00:00:00Z"
  }
]
```
`documentType` is `KnowledgeDocumentType` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Knowledge
Method: GET
Route: /api/knowledge/documents/{id}
Description: Get one document's metadata (not its file content — see download below).
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Knowledge
Method: GET
Route: /api/knowledge/documents/{id}/download
Description: Download the actual file content.
Path Parameters: id (Guid, required)
Response: **binary file stream** (`Content-Type` matches the stored file's content type, `Content-Disposition` carries the original filename) — not JSON.
Status Codes: 200, 401, 403, 404

---

### Module: Knowledge
Method: POST
Route: /api/knowledge/documents
Description: Upload a new knowledge document.
Content-Type: **multipart/form-data** (file upload, not JSON)

Request Body (form fields):
```
file: <binary>            (the file itself)
tenantId: guid
title: string
description: string | null
documentType: int (KnowledgeDocumentType)
```
Response: the created document's metadata (shape as GET by id).
Status Codes: 200, 400, 401, 403

---

### Module: Knowledge
Method: PUT
Route: /api/knowledge/documents/{id}
Description: Update a document's title/description/type (does not replace the file content).
Path Parameters: id (Guid, required)
Request Body:
```json
{ "title": "string", "description": "string | null", "documentType": 0 }
```
Response: the updated document metadata.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Knowledge
Method: DELETE
Route: /api/knowledge/documents/{id}
Description: Delete a knowledge document (metadata and stored file).
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

## 6. Strategy

Standalone strategic-goal hierarchy — no dependency on Project Management (that link is the
separate [Project-Strategy Alignment integration](#18-project-strategy-alignment-integration)).
Permission namespace: `Strategy.*` (`View`, `Create`, `Edit`, `Delete`). Base route: `/api/strategy`.

### Module: Strategy
Method: GET
Route: /api/strategy
Description: List all strategies for a tenant (flat list; hierarchy via ParentStrategyId).
Query Parameters: tenantId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "name": "string", "description": "string | null", "weight": 1.0, "parentStrategyId": "guid | null" } ]
```
Status Codes: 200, 401, 403

---

### Module: Strategy
Method: GET
Route: /api/strategy/{id}
Description: Get one strategy.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Strategy
Method: POST
Route: /api/strategy
Description: Create a strategy (optionally nested under a parent).
Request Body:
```json
{ "tenantId": "guid", "name": "string", "description": "string | null", "weight": 1.0, "parentStrategyId": "guid | null" }
```
Response: the created strategy.
Status Codes: 200, 400, 401, 403

---

### Module: Strategy
Method: PUT
Route: /api/strategy/{id}
Description: Update a strategy.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "name": "string", "description": "string | null", "weight": 1.0, "parentStrategyId": "guid | null" }
```
Response: the updated strategy.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Strategy
Method: DELETE
Route: /api/strategy/{id}
Description: Delete a strategy.
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

## 7. Projects (ProjectManagement.Core)

The one entity ProjectManagement.Core owns — a minimal Project primitive. Does **not** implement
Waterfall/Agile itself (those are separate capabilities layered on top). Permission namespace:
`Projects.*` (`View`, `Create`, `Edit`, `Delete`, `Submit`). Base route: `/api/project-management/projects`.

### Module: Projects
Method: GET
Route: /api/project-management/projects
Description: List/search/paginate projects.

Query Parameters:
- tenantId (Guid, required)
- pageNumber (int, optional, default 1)
- pageSize (int, optional, default 20)
- search (string, optional)
- type (ProjectType, optional)
- status (ProjectStatus, optional)
- organizationUnitId (Guid, optional)
- managerUserId (Guid, optional)
- sortBy (ProjectSortBy, optional, default CreatedAtUtc — see [Enums Reference](#enums-reference))
- sortDescending (bool, optional, default true)

Response (paginated envelope):
```json
{
  "items": [
    {
      "id": "guid", "tenantId": "guid", "name": "string", "code": "string",
      "type": 0, "status": 0, "approvalStatus": 0,
      "ownerUserId": "guid | null", "managerUserId": "guid | null",
      "organizationUnitId": "guid | null", "workCalendarId": "guid | null",
      "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null", "cost": 0.0,
      "goal": "string | null", "requirements": "string | null", "constraints": "string | null",
      "assumptions": "string | null", "description": "string | null", "charter": "string | null",
      "createdAtUtc": "2026-01-01T00:00:00Z"
    }
  ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3
}
```
`type` is `ProjectType`, `status` is `ProjectStatus`, `approvalStatus` is `ApprovalStatus` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Projects
Method: GET
Route: /api/project-management/projects/{id}
Description: Get one project.
Path Parameters: id (Guid, required)
Response: one item shaped as the `items[]` entries above (not paginated).
Status Codes: 200, 401, 403, 404

---

### Module: Projects
Method: POST
Route: /api/project-management/projects
Description: Create a project. Starts in Draft status, NotSubmitted approval status.

Request Body:
```json
{
  "tenantId": "guid", "name": "string", "code": "string", "type": 0,
  "managerUserId": "guid | null", "ownerUserId": "guid | null",
  "organizationUnitId": "guid | null", "workCalendarId": "guid | null",
  "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null", "cost": 0.0,
  "goal": "string | null", "requirements": "string | null", "constraints": "string | null",
  "assumptions": "string | null", "description": "string | null", "charter": "string | null"
}
```
Response: the created project.
Status Codes: 200, 400, 401, 403, 409 (Code must be unique per tenant)

---

### Module: Projects
Method: PUT
Route: /api/project-management/projects/{id}
Description: Update project details.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`type` (Type is immutable after creation).
Response: the updated project.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Projects
Method: POST
Route: /api/project-management/projects/{id}/archive
Description: Archive a project (Status → Archived). Terminal state.
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

### Module: Projects
Method: POST
Route: /api/project-management/projects/{id}/submit-for-approval
Description: Submit for approval. Routes through Workflow if installed with an applicable "Project" definition; otherwise auto-approves and Status moves Draft → Active.
Path Parameters: id (Guid, required)
Response: the updated project (approvalStatus reflects the outcome).
Status Codes: 200, 401, 403, 404

---

## 8. Waterfall Activities

Requires ProjectManagement.Core. Permission namespace: `WaterfallActivities.*` (`View`, `Create`,
`Edit`, `Delete`, `Submit`) plus `WaterfallSchedule.Manage` for everything that shapes the plan rather
than one activity (dependencies, applying a schedule, baselines, progress snapshots, MS Project import).
Base route: `/api/project-management/waterfall/activities`; the scheduling features below live under
`/api/project-management/waterfall/...` and are mapped by the same `MapWaterfallEndpoints()` call.

**Existing databases need `docs/upgrade/2026-10-03-add-waterfall-scheduling.sql` first** (one new column
and four new tables).

Activity changes in this release: a `milestone` flag (`isMilestone`, in every response; optional on
create and update — on update, omitted means unchanged). A milestone has `durationDays: 0`, its end date
is its start date, and it is always a leaf. A parent must now exist in the same project, must not be a
milestone, must not be the activity itself or one of its own descendants, and must not carry dependency
links (checked only when the parent changes).

### Module: Waterfall Activities
Method: GET
Route: /api/project-management/waterfall/activities
Description: List the WBS activities for a project (flat list; hierarchy via ParentActivityId).
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "parentActivityId": "guid | null",
    "name": "string", "description": "string | null", "deliverableId": "guid | null",
    "responsibleUserId": "guid | null", "approverUserId": "guid | null",
    "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null",
    "durationDays": 5, "manHours": 40.0, "weight": 1.0,
    "plannedProgress": 0.0, "actualProgress": 0.0, "approvalStatus": 0, "isMilestone": false
  }
]
```
Status Codes: 200, 401, 403

---

### Module: Waterfall Activities
Method: GET
Route: /api/project-management/waterfall/activities/{id}
Description: Get one activity.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall Activities
Method: POST
Route: /api/project-management/waterfall/activities
Description: Create a WBS activity, optionally nested under a parent activity.
Request Body:
```json
{
  "tenantId": "guid", "projectId": "guid", "parentActivityId": "guid | null",
  "name": "string", "description": "string | null", "deliverableId": "guid | null",
  "responsibleUserId": "guid | null", "approverUserId": "guid | null",
  "startDate": "2026-01-01 | null", "endDate": "2026-01-01 | null",
  "durationDays": 5, "manHours": 40.0, "weight": 1.0
}
```
Response: the created activity, `plannedProgress: 0`, `actualProgress: 0`, `approvalStatus: 0`.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Waterfall Activities
Method: PUT
Route: /api/project-management/waterfall/activities/{id}
Description: Update activity details (not progress — see the dedicated progress endpoint).
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated activity.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Waterfall Activities
Method: PUT
Route: /api/project-management/waterfall/activities/{id}/progress
Description: Update planned/actual progress percentages.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "plannedProgress": 25.0, "actualProgress": 20.0 }
```
Response: the updated activity.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Waterfall Activities
Method: DELETE
Route: /api/project-management/waterfall/activities/{id}
Description: Delete an activity. Blocked if it has child activities.
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 400 (has children), 401, 403, 404

---

### Module: Waterfall Activities
Method: POST
Route: /api/project-management/waterfall/activities/{id}/submit-for-approval
Description: Submit an activity for approval (routes through Workflow if installed, else auto-approves).
Path Parameters: id (Guid, required)
Response: the updated activity.
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall Activities
Method: POST
Route: /api/project-management/waterfall/activities/generate-wbs
Description: **Optional integration point.** Ask a configured WBS generator to suggest a breakdown for a project goal. Returns `501` if no `IWbsGenerator` implementation is registered (none is, by default — this is a pure extension point).

Query Parameters: projectId (Guid, required), projectGoal (string, required)
Request Body: none (both inputs are query parameters, not a JSON body)
Response (when a generator is configured):
```json
[ { "name": "string", "description": "string | null", "durationDays": 5, "weight": 1.0 } ]
```
Response (when no generator is configured, i.e. always in this codebase's default installation):
RFC 7807 Problem Details, `status: 501`, `detail: "WBS generation is not configured for this deployment."`

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default — no provider registered)

---

### Module: Waterfall Dependencies
Method: GET
Route: /api/project-management/waterfall/dependencies
Description: A project's dependency links.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "predecessorActivityId": "guid", "successorActivityId": "guid", "type": 0, "lagDays": 0 } ]
```
`type`: see [DependencyType](#dependencytype-waterfall). `lagDays` is in working days; negative means the
successor may overlap the predecessor (a lead).

Status Codes: 200, 401, 403

---

### Module: Waterfall Dependencies
Method: POST
Route: /api/project-management/waterfall/dependencies
Description: Link two activities. Needs `WaterfallSchedule.Manage`.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "predecessorActivityId": "guid", "successorActivityId": "guid", "type": 0, "lagDays": 0 }
```
`type` defaults to FinishToStart (0) and `lagDays` to 0; `lagDays` must be between -365 and 365.
Rules: both activities must exist in the project (404) and must have no sub-activities (400 — a summary's
dates are the span of its children); an activity cannot depend on itself (400); a pair of activities has at most
one link (409) and a link may not close a loop (409).
Response: the created link.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Dependencies
Method: PUT
Route: /api/project-management/waterfall/dependencies/{id}
Description: Change a link's type and lag. Needs `WaterfallSchedule.Manage`.
Request Body: `{ "type": 1, "lagDays": 2 }`
Response: the updated link.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Waterfall Dependencies
Method: DELETE
Route: /api/project-management/waterfall/dependencies/{id}
Description: Remove a link. Needs `WaterfallSchedule.Manage`. (Deleting an activity removes its links too.)
Response: empty on success.
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall Schedule
Method: GET
Route: /api/project-management/waterfall/schedule
Description: Calculate the project's critical-path schedule. Changes nothing.
Query Parameters: projectId (Guid, required)
Response:
```json
{
  "projectId": "guid", "projectStart": "2026-03-02", "projectFinish": "2026-03-13", "projectDurationDays": 10,
  "usesWorkCalendar": true, "plannedProgress": 40.0, "actualProgress": 25.0,
  "criticalPath": ["guid", "guid"],
  "activities": [
    {
      "id": "guid", "parentActivityId": "guid | null", "name": "string", "isSummary": false, "isMilestone": false,
      "start": "2026-03-02", "finish": "2026-03-04", "durationDays": 3,
      "lateStart": "2026-03-02", "lateFinish": "2026-03-04", "totalFloatDays": 0, "isCritical": true,
      "plannedProgress": 40.0, "actualProgress": 25.0, "usedDefaultDuration": false
    }
  ],
  "warnings": ["string"], "applied": false
}
```
How it is calculated: durations, lags and float are in working days of the project's work calendar (every
day counts when the project has none, or the Calendar integration is not installed — `usesWorkCalendar`
says which). Each dependency type becomes a constraint on the successor (FS: starts after the predecessor
finishes; SS: starts after it starts; FF: finishes after it finishes; SF: finishes after it starts), plus the lag.
An activity with no predecessor starts on its own `startDate`, or on the project's start; an activity with
predecessors is driven by them alone (its stored dates are ignored). `totalFloatDays` is how long an activity can
slip without delaying the project; zero means critical, and `criticalPath` lists the critical leaf activities in
start order. Milestones sit on the day the work before them finishes. A summary spans its children, and its
float is its children's smallest. `plannedProgress`/`actualProgress` are the weighted roll-up of the stored
per-activity progress: by `weight` when any sibling has one, else by duration, else equally. An activity with
no duration and no dates is given one day (`usedDefaultDuration`, and a warning).

Errors: 404 unknown project; 409 the stored links contain a cycle; 400 a calendar with no working days or a duration over 36500 days.

Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Schedule
Method: POST
Route: /api/project-management/waterfall/schedule/apply
Description: Calculate the schedule and write each activity's start, end and duration back to it (summaries included). Idempotent: applying twice changes nothing the second time. Needs `WaterfallSchedule.Manage`.
Query Parameters: projectId (Guid, required)
Response: the same as the GET above, with `applied: true`.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Baselines
Method: GET
Route: /api/project-management/waterfall/baselines
Description: A project's baselines, in number order.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "number": 1, "name": "Approved plan", "note": "string | null",
    "projectStart": "2026-03-02", "projectFinish": "2026-06-30", "activityCount": 12, "createdAtUtc": "2026-03-01T09:00:00+00:00", "createdByUserId": "guid | null" } ]
```
A baseline is a frozen copy of the project's *calculated* schedule (dependencies and calendar applied, not just
stored dates), numbered 1, 2, 3… per project, never edited. It keeps describing activities as they were even if they
are later renamed, moved or deleted.

Status Codes: 200, 401, 403

---

### Module: Waterfall Baselines
Method: GET
Route: /api/project-management/waterfall/baselines/{id}
Description: One baseline with its per-activity rows.
Path Parameters: id (Guid, required)
Response: `{ "baseline": { …as above… }, "activities": [ { "activityId": "guid", "parentActivityId": "guid | null", "name": "string", "isSummary": false, "isMilestone": false, "startDate": "2026-03-02", "endDate": "2026-03-04", "durationDays": 3 } ] }`
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall Baselines
Method: POST
Route: /api/project-management/waterfall/baselines
Description: Freeze the project's current calculated schedule as the next baseline. Needs `WaterfallSchedule.Manage`.
Request Body: `{ "tenantId": "guid", "projectId": "guid", "name": "Approved plan", "note": "string | null" }`
Response: the created baseline with its rows. A project holds at most 50 baselines (409); a project with no activities has nothing to baseline (400). A new baseline takes the highest existing number plus one.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Baselines
Method: GET
Route: /api/project-management/waterfall/baselines/{id}/variance
Description: Compare the project's current calculated schedule with a baseline.
Path Parameters: id (Guid, required)
Response:
```json
{
  "baselineId": "guid", "number": 1, "name": "Approved plan", "projectId": "guid",
  "baselineProjectStart": "2026-03-02", "baselineProjectFinish": "2026-06-30",
  "currentProjectStart": "2026-03-02", "currentProjectFinish": "2026-07-08",
  "projectStartVarianceDays": 0, "projectFinishVarianceDays": 8,
  "onTrackCount": 7, "lateCount": 4, "earlyCount": 1, "addedCount": 1, "removedCount": 0,
  "activities": [
    {
      "activityId": "guid", "name": "string", "isSummary": false, "isMilestone": false, "status": 1,
      "baselineStart": "2026-03-02", "baselineFinish": "2026-03-04", "baselineDurationDays": 3,
      "currentStart": "2026-03-02", "currentFinish": "2026-03-06", "currentDurationDays": 5,
      "startVarianceDays": 0, "finishVarianceDays": 2, "durationVarianceDays": 2
    }
  ]
}
```
Variances are current minus baseline: positive means later/longer. Start and finish variances are in **calendar
days**; the duration variance is in working days. `status` (see [VarianceStatus](#variancestatus-waterfall)) is judged
by the finish. For an added or removed activity the missing side's fields are `null`.

Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Baselines
Method: DELETE
Route: /api/project-management/waterfall/baselines/{id}
Description: Delete a baseline and its rows. Needs `WaterfallSchedule.Manage`.
Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall Progress Curve
Method: GET
Route: /api/project-management/waterfall/s-curve
Description: The data behind a project's S-curve.
Query Parameters: projectId (Guid, required), stepDays (int, optional, 1-90, default 7)
Response:
```json
{
  "projectId": "guid", "projectStart": "2026-03-02", "projectFinish": "2026-06-30", "stepDays": 7, "today": "2026-04-01",
  "planned": [ { "date": "2026-03-02", "plannedProgress": 3.5 }, { "date": "2026-03-09", "plannedProgress": 11.0 } ],
  "actual": [ { "date": "2026-03-16", "actualProgress": 9.0, "plannedProgress": 17.0 } ],
  "currentPlannedProgress": 42.0, "currentActualProgress": 35.5, "progressVariance": -6.5, "schedulePerformanceIndex": 0.85
}
```
`planned` is calculated from the schedule alone — each activity completes evenly over its working days (a milestone
all at once), combined with the same weighting as the roll-up — sampled every `stepDays` days from start to finish; the
finish date is always the last point, and a very long project is sampled more coarsely (about 1000 points at most;
`stepDays` in the response is the step actually used). `actual` is the project's snapshots, oldest first: progress
history is stored nowhere else, so take snapshots periodically. `schedulePerformanceIndex` is actual ÷ planned today
(`null` while nothing is planned yet); below 1 is behind schedule.

Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Progress Curve
Method: GET
Route: /api/project-management/waterfall/progress-snapshots
Description: A project's progress snapshots, oldest first.
Query Parameters: projectId (Guid, required)
Response: `[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "snapshotDate": "2026-03-16", "plannedProgress": 17.0, "actualProgress": 9.0, "note": "string | null", "createdByUserId": "guid | null" } ]`
Status Codes: 200, 401, 403

---

### Module: Waterfall Progress Curve
Method: POST
Route: /api/project-management/waterfall/progress-snapshots
Description: Record the project's progress as of a date. Needs `WaterfallSchedule.Manage`.
Request Body: `{ "tenantId": "guid", "projectId": "guid", "snapshotDate": "2026-03-16 | null (today)", "note": "string | null" }`
Response: the snapshot. `plannedProgress` is what the schedule says should be done by the end of that date; `actualProgress` is the roll-up of the entered activity progress right now. A project has one snapshot per date; posting again for the same date refreshes it.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall Progress Curve
Method: DELETE
Route: /api/project-management/waterfall/progress-snapshots/{id}
Description: Delete a snapshot. Needs `WaterfallSchedule.Manage`.
Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404

---

### Module: Waterfall MS Project
Method: POST
Route: /api/project-management/waterfall/msproject/import
Description: Build a project's WBS and dependencies from an MS Project XML (MSPDI) file. Needs `WaterfallSchedule.Manage`.
Content-Type: **multipart/form-data**

Request Body (form fields / query):
```
file: <binary, 1 byte - 20 MB>
projectId: guid (query)
replaceExisting: bool (query, optional, default false)
```
Response:
```json
{ "activitiesImported": 42, "dependenciesImported": 51, "activitiesReplaced": 0, "warnings": ["string"] }
```
Outline levels become the tree; milestones, durations (converted to working days using the file's minutes per day),
percent complete and all four link types with their lags are kept. Resources, assignments, costs, constraints and
calendars are not imported, and dates are then this system's own calculation (apply the schedule to refresh them).
A project that already has activities is refused (409) unless `replaceExisting=true`, which deletes every existing
activity and dependency of the project first; the file is fully validated before anything is deleted, so a bad file
never costs the existing plan. Links that cannot be kept — to or from summary tasks, to tasks not in the file,
duplicates, or ones that would form a cycle — are skipped and listed in `warnings`. At most 5000 tasks. DTDs are not
processed.

Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Waterfall MS Project
Method: GET
Route: /api/project-management/waterfall/msproject/export
Description: The project's calculated schedule as an MS Project XML file.
Query Parameters: projectId (Guid, required)
Response: **an XML file** (`application/xml`, named from the project's code) — not JSON. It contains a project-summary row, the WBS in outline order with start, finish, duration (hours of an 8-hour day), milestone/summary flags, percent complete and predecessor links, and one calendar with the project's working week and holidays.
Status Codes: 200, 401, 403, 404, 409

---

## 9. Agile Tasks

Requires ProjectManagement.Core. Explicitly independent of Waterfall (neither references the
other) — a project can use one, the other, both, or neither. Permission namespace:
`AgileTasks.*` (`View`, `Create`, `Edit`, `Delete`, `Submit`) plus `AgileSprints.Manage` for creating, editing,
starting, completing and deleting sprints and putting tasks into them. Base route: `/api/project-management/agile/tasks`;
the board, backlog, sprint, checklist and chart endpoints below live under `/api/project-management/agile/...` and are
mapped by the same `MapAgileTaskEndpoints()` call.

**Existing databases need `docs/upgrade/2026-10-03-add-agile-sprints.sql` first** (two new columns and three new tables;
it also numbers existing tasks so boards come out in a stable order).

Task changes in this release: every task response carries `storyPoints` (null = not estimated) and `rank` (its position
within its status column, lowest first). `storyPoints` is optional on create and update; **on update, omitted leaves the
estimate unchanged**, so a client that predates the field cannot wipe it — use `PUT .../{id}/estimate` to set it or to
clear it with an explicit `null`. A new status `UnderReview` (value 3) sits between InProgress and Done on the board. A
task joins a sprint through its `sprintNumber`, as before; a task in no sprint is in the **backlog**. A sprint number does
not need a Sprint behind it (older data keeps working), but a *completed* sprint accepts no more tasks (409). Changing a
task's status through `PUT .../{id}/status` puts the card at the bottom of its new column.

### Module: Agile Tasks
Method: GET
Route: /api/project-management/agile/tasks
Description: List a project's agile tasks, optionally filtered to one sprint.
Query Parameters: projectId (Guid, required), sprintNumber (int, optional)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "title": "string", "description": "string | null",
    "status": 0, "responsibleUserId": "guid | null", "approverUserId": "guid | null",
    "dueDate": "2026-01-01 | null", "priority": 1, "sprintNumber": 3, "approvalStatus": 0,
    "storyPoints": 5, "rank": 0
  }
]
```
`status` is `AgileTaskStatus`, `priority` is `AgileTaskPriority` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Agile Tasks
Method: GET
Route: /api/project-management/agile/tasks/{id}
Description: Get one agile task.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Agile Tasks
Method: POST
Route: /api/project-management/agile/tasks
Description: Create an agile task.
Request Body:
```json
{
  "tenantId": "guid", "projectId": "guid", "title": "string", "description": "string | null",
  "responsibleUserId": "guid | null", "approverUserId": "guid | null",
  "dueDate": "2026-01-01 | null", "priority": 1, "sprintNumber": 3
}
```
Response: the created task, `status: 0 (ToDo)`, `approvalStatus: 0`.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Tasks
Method: PUT
Route: /api/project-management/agile/tasks/{id}
Description: Update task details.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated task.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Tasks
Method: PUT
Route: /api/project-management/agile/tasks/{id}/status
Description: Change only the task's status (ToDo/InProgress/Done).
Path Parameters: id (Guid, required)
Request Body:
```json
{ "status": 2 }
```
Response: the updated task.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Tasks
Method: DELETE
Route: /api/project-management/agile/tasks/{id}
Description: Delete an agile task.
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

### Module: Agile Tasks
Method: POST
Route: /api/project-management/agile/tasks/{id}/submit-for-approval
Description: Submit an agile task for approval.
Path Parameters: id (Guid, required)
Response: the updated task.
Status Codes: 200, 401, 403, 404

---

### Module: Agile Tasks
Method: POST
Route: /api/project-management/agile/tasks/generate
Description: **Optional integration point.** Suggest agile tasks for a project goal. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectGoal (string, required)
Request Body: none
Response (when configured):
```json
[ { "title": "string", "description": "string | null", "priority": 1 } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

### Module: Agile Board
Method: GET
Route: /api/project-management/agile/board
Description: The Kanban board: four columns, in order ToDo, InProgress, UnderReview, Done.
Query Parameters: projectId (Guid, required), sprintNumber (int, optional — the sprint's board), responsibleUserId (Guid, optional), priority (AgileTaskPriority, optional)
Response:
```json
{
  "projectId": "guid", "sprintNumber": null,
  "columns": [
    {
      "status": 0, "cardCount": 2, "points": 8,
      "cards": [
        { "id": "guid", "title": "string", "status": 0, "priority": 1, "responsibleUserId": "guid | null",
          "dueDate": "2026-01-01 | null", "sprintNumber": 3, "storyPoints": 5, "rank": 0,
          "checklistDone": 1, "checklistTotal": 3, "approvalStatus": 0 }
      ]
    }
  ]
}
```
Cards are in rank order within each column. `points` is the sum of the column's story points.

Status Codes: 200, 401, 403

---

### Module: Agile Board
Method: GET
Route: /api/project-management/agile/backlog
Description: The tasks in no sprint that are not done, in board order (status column, then rank).
Query Parameters: projectId (Guid, required)
Response: `{ "projectId": "guid", "cards": [ …cards as above… ], "totalPoints": 13, "unestimatedCount": 2 }`
Status Codes: 200, 401, 403

---

### Module: Agile Board
Method: POST
Route: /api/project-management/agile/tasks/{id}/move
Description: Drag and drop: put a task in a column at a position. Needs `AgileTasks.Edit`.
Path Parameters: id (Guid, required)
Request Body: `{ "status": 1, "beforeTaskId": "guid | null" }`
The card is inserted in front of `beforeTaskId`, which must be a card of the destination column, or at the end when null.
The ranks of the column it joins — and of the one it left — are renumbered 0, 1, 2… Dropping a card on itself changes
nothing. Moving into or out of Done inside a sprint is recorded in that sprint's history.
Response: the updated task.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Board
Method: PUT
Route: /api/project-management/agile/tasks/{id}/estimate
Description: Set a task's story points, or clear them with an explicit `null`. Needs `AgileTasks.Edit`. 0-1000.
Request Body: `{ "storyPoints": 5 }` or `{ "storyPoints": null }`
Response: the updated task.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Board
Method: GET / POST / PUT / DELETE
Route: /api/project-management/agile/tasks/{id}/checklist  and  .../checklist/{itemId}
Description: A task's checklist — the small steps shown on its card (the card's `checklistDone` / `checklistTotal`). Reading needs `AgileTasks.View`, changing needs `AgileTasks.Edit`.
Request Body (POST): `{ "text": "string (1-500)" }` — added at the end; at most 100 items per task (409).
Request Body (PUT .../{itemId}): `{ "text": "string", "isDone": true }`
Response: `{ "id": "guid", "taskId": "guid", "text": "string", "isDone": false, "order": 0 }` (a list for GET; empty for DELETE)
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: GET
Route: /api/project-management/agile/sprints
Description: A project's sprints, by number.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "number": 1, "name": "Sprint 1", "goal": "string | null",
    "startDate": "2026-03-02 | null", "endDate": "2026-03-15 | null", "status": 0,
    "taskCount": 6, "totalPoints": 21, "donePoints": 8 } ]
```
`status`: see [SprintStatus](#sprintstatus-agile-tasks). `taskCount`/`totalPoints`/`donePoints` describe the tasks in the sprint right now.

Status Codes: 200, 401, 403

---

### Module: Agile Sprints
Method: GET
Route: /api/project-management/agile/sprints/{id}
Description: One sprint.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Agile Sprints
Method: POST
Route: /api/project-management/agile/sprints
Description: Create a sprint. Needs `AgileSprints.Manage`.
Request Body: `{ "tenantId": "guid", "projectId": "guid", "name": "string | null", "goal": "string | null", "startDate": "2026-03-02 | null", "endDate": "2026-03-15 | null" }`
The number is the project's highest plus one; the name defaults to "Sprint N"; dates may wait until the sprint starts but the end may not precede the start.
Response: the sprint, `status: 0` (Planned).
Status Codes: 200, 400, 401, 403

---

### Module: Agile Sprints
Method: PUT
Route: /api/project-management/agile/sprints/{id}
Description: Edit a sprint's name, goal and dates. A completed sprint can no longer be edited (409). Needs `AgileSprints.Manage`.
Request Body: `{ "name": "string", "goal": "string | null", "startDate": "…|null", "endDate": "…|null" }`
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: POST
Route: /api/project-management/agile/sprints/{id}/start
Description: Planned → Active. Needs `AgileSprints.Manage`.
Request Body: `{ "startDate": "…|null", "endDate": "…|null" }` — dates given here replace the sprint's; both must be known by now (400). Only a planned sprint can start, and a project has at most one active sprint (409).
Response: the sprint, `status: 1`.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: POST
Route: /api/project-management/agile/sprints/{id}/complete
Description: Active → Completed. Needs `AgileSprints.Manage`.
Request Body: `{ "moveIncompleteToSprintId": "guid | null" }`
Tasks that are Done stay in the sprint. Unfinished ones move to the given sprint (another sprint of the same project that is not completed) or back to the backlog when null; their status is kept. A completed sprint's history is frozen: carrying work over is recorded as "carried over", not as a change of scope.
Response: `{ "sprint": { …, "status": 2 }, "completedTasks": 5, "carriedOverTasks": 2 }`
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: DELETE
Route: /api/project-management/agile/sprints/{id}
Description: Delete a sprint. Only a planned sprint with no tasks can be deleted (409): an active or completed one is part of the project's history. Needs `AgileSprints.Manage`.
Status Codes: 200, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: POST / DELETE
Route: /api/project-management/agile/sprints/{id}/tasks  and  .../tasks/{taskId}
Description: Put tasks into a sprint in bulk, or take one back to the backlog. A completed sprint accepts neither (409). Needs `AgileSprints.Manage`.
Request Body (POST): `{ "taskIds": ["guid", "guid"] }` — every task must exist in the sprint's project, otherwise nothing is assigned (404).
Response: the sprint.
Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Agile Sprints
Method: GET
Route: /api/project-management/agile/sprints/{id}/burn
Description: One series that serves both the burn-up and the burn-down chart.
Query Parameters: metric (`Points` or `Count`, optional, default `Points`)
Response:
```json
{
  "sprintId": "guid", "sprintNumber": 1, "name": "Sprint 1", "status": 1, "metric": 0,
  "startDate": "2026-03-02", "endDate": "2026-03-06",
  "committedScope": 8, "currentScope": 10, "currentCompleted": 5, "currentRemaining": 5,
  "points": [
    { "date": "2026-03-02", "scope": 8, "completed": 0, "remaining": 8, "idealRemaining": 8.0 },
    { "date": "2026-03-04", "scope": 10, "completed": 5, "remaining": 5, "idealRemaining": 4.0 },
    { "date": "2026-03-06", "scope": null, "completed": null, "remaining": null, "idealRemaining": 0.0 }
  ],
  "warnings": ["string"]
}
```
Burn-up plots `scope` and `completed`; burn-down plots `remaining` against `idealRemaining` (a straight line from the committed scope to zero on the last day). `scope` is what the sprint holds by the end of each day, so work added mid-sprint raises it and work that leaves lowers it; `completed` counts tasks that were Done, and a task that is reopened stops counting. `committedScope` is the scope at the end of the first day. The first three fields are `null` for days that have not happened yet. Days are UTC dates. The figures are replayed from a recorded history of scope and status changes, so they only cover changes made since the upgrade. A sprint with no story points on any task returns a warning — chart it with `metric=Count`.

Status Codes: 200, 400, 401, 403, 404

---

### Module: Agile Sprints
Method: GET
Route: /api/project-management/agile/velocity
Description: The velocity of a project's most recent completed sprints.
Query Parameters: projectId (Guid, required), sprints (int, optional, 1-50, default 5), metric (`Points` or `Count`, optional)
Response:
```json
{
  "projectId": "guid", "metric": 0,
  "sprints": [ { "sprintId": "guid", "number": 1, "name": "Sprint 1", "startDate": "2026-03-02", "endDate": "2026-03-15",
                 "committed": 8, "finalScope": 13, "completed": 10, "carriedOver": 3, "completionPercent": 76.9 } ],
  "averageVelocity": 9.0, "averageCommitted": 9.0
}
```
Oldest first. `committed` is the scope at the end of the first day, `finalScope` the scope at the end, `completed` is the velocity, `carriedOver` the unfinished work that moved on. Averages are over the sprints listed.

Status Codes: 200, 400, 401, 403

---

## 10. Project Team

Requires ProjectManagement.Core. Two sub-areas: team **members** (a user assigned to a project,
free-text role title) and **governance roles** (a named role like "Sponsor"/"PM", optionally
tied to a user, with contact info). Permission namespace: `ProjectTeam.*` (`View`, `ManageMembers`,
`ManageGovernance`).

### Module: Project Team
Method: GET
Route: /api/project-management/team/members
Description: List a project's team members.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "userId": "guid", "roleTitle": "string | null" } ]
```
Status Codes: 200, 401, 403

---

### Module: Project Team
Method: GET
Route: /api/project-management/team/members/available-users
Description: List users in the tenant not yet assigned as a member of this project (for an "add member" picker). Reuses NexusCore's own Identity user list.
Query Parameters: tenantId (Guid, required), projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "email": "string", "displayName": "string", "isActive": true, "lastLoginAtUtc": "2026-01-01T00:00:00Z | null", "roles": ["string"] } ]
```
(this is NexusCore's own `UserDto`, not a Team-module-specific type)

Status Codes: 200, 401, 403

---

### Module: Project Team
Method: POST
Route: /api/project-management/team/members
Description: Add a user as a project team member.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "userId": "guid", "roleTitle": "string | null" }
```
Response: the created membership.
Status Codes: 200, 400, 401, 403, 404, 409 (user already a member)

---

### Module: Project Team
Method: DELETE
Route: /api/project-management/team/members/{memberId}
Description: Remove a team member.
Path Parameters: memberId (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

### Module: Project Governance
Method: GET
Route: /api/project-management/team/governance-roles
Description: List a project's governance role assignments.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "title": "string", "userId": "guid | null", "personnelNumber": "string | null", "phone": "string | null", "email": "string | null", "serviceLocation": "string | null" } ]
```
Status Codes: 200, 401, 403

---

### Module: Project Governance
Method: POST
Route: /api/project-management/team/governance-roles
Description: Create a governance role assignment (title is free text, e.g. "Sponsor", "Project Manager").
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "title": "string", "userId": "guid | null", "personnelNumber": "string | null", "phone": "string | null", "email": "string | null", "serviceLocation": "string | null" }
```
Response: the created role.
Status Codes: 200, 400, 401, 403

---

### Module: Project Governance
Method: PUT
Route: /api/project-management/team/governance-roles/{id}
Description: Update a governance role assignment.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated role.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Project Team
Method: PUT
Route: /api/project-management/team/members/{memberId}
Description: Change a member's role title. Needs `ProjectTeam.ManageMembers`. A blank title clears it.

Path Parameters: memberId (Guid, required)
Request Body:
```json
{ "roleTitle": "string | null" }
```
Response: the updated member.
Status Codes: 200, 401, 403, 404

---

### Module: Project Team
Method: DELETE
Route: /api/project-management/team/governance-roles/{id}
Description: Delete a governance role. Needs `ProjectTeam.ManageGovernance`.

Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404

---

## 11. Deliverables

Requires ProjectManagement.Core. Permission namespace: `Deliverables.*` (`View`, `Create`, `Edit`).
Base route: `/api/project-management/deliverables`.

### Module: Deliverables
Method: GET
Route: /api/project-management/deliverables
Description: List a project's deliverables.
Query Parameters: projectId (Guid, required)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "title": "string", "description": "string | null", "acceptanceCriteria": "string | null", "responsibleUserId": "guid | null", "targetDate": "2026-01-01 | null", "status": 0 } ]
```
`status` is `DeliverableStatus` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Deliverables
Method: GET
Route: /api/project-management/deliverables/{id}
Description: Get one deliverable.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Deliverables
Method: POST
Route: /api/project-management/deliverables
Description: Create a deliverable.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "title": "string", "description": "string | null", "acceptanceCriteria": "string | null", "responsibleUserId": "guid | null", "targetDate": "2026-01-01 | null" }
```
Response: the created deliverable, `status: 0 (Planned)`.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Deliverables
Method: PUT
Route: /api/project-management/deliverables/{id}
Description: Update deliverable details.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated deliverable.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Deliverables
Method: PUT
Route: /api/project-management/deliverables/{id}/status
Description: Change only the deliverable's status.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "status": 2 }
```
Response: the updated deliverable.
Status Codes: 200, 400, 401, 403, 404

---

## 12. KPI

Requires ProjectManagement.Core **and** Deliverables (each KPI is attached to one Deliverable).
Permission namespace: `Kpi.*` (`View`, `Create`, `Edit`). Base route: `/api/project-management/kpis`.

### Module: KPI
Method: GET
Route: /api/project-management/kpis
Description: List KPI definitions for a project, optionally filtered to one deliverable.
Query Parameters: projectId (Guid, required), deliverableId (Guid, optional)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "deliverableId": "guid", "type": 0, "description": "string", "formula": "string | null", "targetValue": 100.0 } ]
```
`type` is `KpiType` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: KPI
Method: GET
Route: /api/project-management/kpis/{id}
Description: Get one KPI definition.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: KPI
Method: POST
Route: /api/project-management/kpis
Description: Create a KPI definition attached to a deliverable.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "deliverableId": "guid", "type": 0, "description": "string", "formula": "string | null", "targetValue": 100.0 }
```
Response: the created KPI definition.
Status Codes: 200, 400, 401, 403, 404 (deliverableId must exist)

---

### Module: KPI
Method: PUT
Route: /api/project-management/kpis/{id}
Description: Update a KPI definition (description/formula/target only — Type and DeliverableId are immutable).
Path Parameters: id (Guid, required)
Request Body:
```json
{ "description": "string", "formula": "string | null", "targetValue": 100.0 }
```
Response: the updated KPI definition.
Status Codes: 200, 400, 401, 403, 404

---

### Module: KPI
Method: DELETE
Route: /api/project-management/kpis/{id}
Description: Delete a KPI definition. Needs the `Kpi.Delete` permission.

Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404

---

## 13. Risks

Requires ProjectManagement.Core. RPN (Risk Priority Number) is computed server-side
(`Probability × Severity × Impact`) and is never accepted as input. Permission namespace:
`Risks.*` (`View`, `Create`, `Edit`, `Submit`). Base route: `/api/project-management/risks`.

### Module: Risks
Method: GET
Route: /api/project-management/risks
Description: List a project's risks.
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "description": "string",
    "probabilityScore": 3, "severityScore": 4, "impactScore": 2, "rpn": 24,
    "responsePlan": "string | null", "riskOwnerUserId": "guid | null", "approvalStatus": 0,
    "createdByUserId": "guid | null", "createdAtUtc": "2026-01-01T00:00:00Z"
  }
]
```
`rpn` = `probabilityScore * severityScore * impactScore`, always server-computed.

Status Codes: 200, 401, 403

---

### Module: Risks
Method: GET
Route: /api/project-management/risks/{id}
Description: Get one risk.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Risks
Method: POST
Route: /api/project-management/risks
Description: Create a risk (RPN is computed automatically from the three scores).
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "description": "string", "probabilityScore": 3, "severityScore": 4, "impactScore": 2, "responsePlan": "string | null", "riskOwnerUserId": "guid | null" }
```
Response: the created risk (includes computed `rpn`).
Status Codes: 200, 400, 401, 403, 404

---

### Module: Risks
Method: PUT
Route: /api/project-management/risks/{id}
Description: Update a risk's details/scores.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated risk (RPN recomputed).
Status Codes: 200, 400, 401, 403, 404

---

### Module: Risks
Method: POST
Route: /api/project-management/risks/{id}/submit-for-approval
Description: Submit a risk for approval.
Path Parameters: id (Guid, required)
Response: the updated risk.
Status Codes: 200, 401, 403, 404

---

### Module: Risks
Method: POST
Route: /api/project-management/risks/analyze
Description: **Optional integration point.** Ask a configured analyzer to suggest risks for a project context. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectContext (string, required)
Request Body: none
Response (when configured):
```json
[ { "description": "string", "probabilityScore": 3, "severityScore": 4, "impactScore": 2, "suggestedResponsePlan": "string | null" } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

### Module: Risks
Method: GET
Route: /api/project-management/risks/matrix
Description: Probability x impact grid for a project. Only occupied cells are returned; a client renders the full 5x5 grid and treats a missing cell as empty. Colouring is left to the client.

Query Parameters: projectId (Guid, required)
Response:
```json
{
  "projectId": "guid", "totalRisks": 3,
  "cells": [ { "probabilityScore": 2, "impactScore": 3, "count": 2, "riskIds": ["guid", "guid"] } ]
}
```
Status Codes: 200, 401, 403

---

### Module: Risks
Method: DELETE
Route: /api/project-management/risks/{id}
Description: Delete a risk. Needs `Risks.Delete`. Items that are `PendingApproval` cannot be deleted (409), so a workflow instance never points at a missing subject.

Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404, 409

---

## 14. Stakeholders

Requires ProjectManagement.Core. Permission namespace: `Stakeholders.*` (`View`, `Create`, `Edit`,
`Submit`). Base route: `/api/project-management/stakeholders`.

### Module: Stakeholders
Method: GET
Route: /api/project-management/stakeholders
Description: List a project's stakeholders.
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "name": "string", "isInternal": true,
    "expectations": "string | null", "notes": "string | null", "power": 1, "interest": 2,
    "engagementStrategy": "string | null", "requirements": "string | null", "approvalStatus": 0,
    "createdByUserId": "guid | null"
  }
]
```
`power` is `PowerLevel`, `interest` is `InterestLevel` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Stakeholders
Method: GET
Route: /api/project-management/stakeholders/{id}
Description: Get one stakeholder.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Stakeholders
Method: POST
Route: /api/project-management/stakeholders
Description: Create a stakeholder.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "name": "string", "isInternal": true, "expectations": "string | null", "notes": "string | null", "power": 1, "interest": 2, "engagementStrategy": "string | null", "requirements": "string | null" }
```
Response: the created stakeholder.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Stakeholders
Method: PUT
Route: /api/project-management/stakeholders/{id}
Description: Update stakeholder details.
Path Parameters: id (Guid, required)
Request Body: same shape as create, minus `tenantId`/`projectId`.
Response: the updated stakeholder.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Stakeholders
Method: POST
Route: /api/project-management/stakeholders/{id}/submit-for-approval
Description: Submit a stakeholder record for approval.
Path Parameters: id (Guid, required)
Response: the updated stakeholder.
Status Codes: 200, 401, 403, 404

---

### Module: Stakeholders
Method: POST
Route: /api/project-management/stakeholders/analyze
Description: **Optional integration point.** Suggest stakeholders for a project context. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectContext (string, required)
Request Body: none
Response (when configured):
```json
[ { "name": "string", "isInternal": true, "expectations": "string | null", "engagementStrategy": "string | null" } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

### Module: Stakeholders
Method: GET
Route: /api/project-management/stakeholders/matrix
Description: Power x interest grid for a project. Only occupied cells are returned. `quadrant` is the engagement strategy for the cell: `High` is the high side of each axis, `Medium`/`Low` the low side; a client that wants another split can ignore it.

Query Parameters: projectId (Guid, required)
Response:
```json
{
  "projectId": "guid", "totalStakeholders": 3,
  "cells": [
    { "power": 2, "interest": 2, "quadrant": 3,
      "stakeholders": [ { "id": "guid", "name": "string", "isInternal": true } ] }
  ]
}
```
`quadrant` values: see [StakeholderQuadrant](#stakeholderquadrant-stakeholders).

Status Codes: 200, 401, 403

---

### Module: Stakeholders
Method: DELETE
Route: /api/project-management/stakeholders/{id}
Description: Delete a stakeholder. Needs `Stakeholders.Delete`. Items that are `PendingApproval` cannot be deleted (409), so a workflow instance never points at a missing subject.

Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404, 409

---

## 15. Progress

Requires ProjectManagement.Core. Explicitly does **not** reference Waterfall — Deviation and
PerformanceClassification are computed server-side from Planned/Actual progress and are never
accepted as input. `ConfirmedProgress` is set only when the update is approved (via
`SubmitForApproval` → Workflow, or the default auto-approve). Permission namespace:
`Progress.*` (`View`, `Create`, `Edit`, `Delete`, `Submit`). Base route: `/api/project-management/progress-updates`.

### Module: Progress
Method: GET
Route: /api/project-management/progress-updates
Description: List a project's progress updates.
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "statusDescription": "string | null",
    "registerDate": "2026-01-01", "plannedProgress": 30.0, "actualProgress": 25.0,
    "confirmedProgress": 25.0, "delayReasons": "string | null",
    "deviation": -5.0, "performanceClassification": 1, "approvalStatus": 2,
    "createdByUserId": "guid | null"
  }
]
```
`deviation = actualProgress - plannedProgress` (server-computed). `performanceClassification` is
derived from `deviation`: `>= -5` → OnTrack(0), `>= -15` → AtRisk(1), else → Behind(2) — see
[Enums Reference](#enums-reference). `confirmedProgress` is `null` until approved.

Status Codes: 200, 401, 403

---

### Module: Progress
Method: GET
Route: /api/project-management/progress-updates/{id}
Description: Get one progress update.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Progress
Method: POST
Route: /api/project-management/progress-updates
Description: Register a new progress update.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "statusDescription": "string | null", "registerDate": "2026-01-01", "plannedProgress": 30.0, "actualProgress": 25.0, "delayReasons": "string | null" }
```
Response: the created update, `confirmedProgress: null`, `approvalStatus: 0`.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Progress
Method: PUT
Route: /api/project-management/progress-updates/{id}
Description: Update a progress update's figures (before it's approved).
Path Parameters: id (Guid, required)
Request Body:
```json
{ "statusDescription": "string | null", "plannedProgress": 30.0, "actualProgress": 25.0, "delayReasons": "string | null" }
```
Response: the updated record.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Progress
Method: POST
Route: /api/project-management/progress-updates/{id}/submit-for-approval
Description: Submit for approval; on approval, `ConfirmedProgress` is set to `ActualProgress`.
Path Parameters: id (Guid, required)
Response: the updated record.
Status Codes: 200, 401, 403, 404

---

### Module: Progress
Method: GET
Route: /api/project-management/progress-updates/executive-summary
Description: **Optional integration point.** Ask a configured generator for a natural-language executive summary of a project's progress. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required)
Request Body: none
Response (when configured):
```json
"string (plain-text executive summary)"
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

### Module: Progress
Method: DELETE
Route: /api/project-management/progress-updates/{id}
Description: Delete a progress update. Needs `ProjectProgress.Delete`. Items that are `PendingApproval` cannot be deleted (409), so a workflow instance never points at a missing subject.

Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404, 409

---

## 15a. Delay Reasons (Progress module)

A structured register of why a project is behind: root cause, days and rials lost, and the
corrective action. Lives in the Progress module, requires nothing beyond it, and has the same
optional-approval flow as progress updates (`SubjectType` = `"DelayReason"`, also listed by
`/api/integrations/project-workflow/subject-types`). The free-text `delayReasons` on a progress
update is a separate, older field and is unchanged. Permission namespace: `DelayReasons.*`
(`View`, `Create`, `Edit`, `Delete`, `Submit`). Base route: `/api/project-management/delay-reasons`.
**Existing databases need `docs/upgrade/2026-10-03-add-delay-reasons.sql` first** (a new table).

### Module: Delay Reasons
Method: GET
Route: /api/project-management/delay-reasons
Description: List a project's delay reasons, newest first.
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "registerDate": "2026-01-01",
    "rootCause": 1, "description": "string", "timeImpactDays": 14, "costImpact": 250000000,
    "correctiveAction": "string | null", "approvalStatus": 0, "createdByUserId": "guid | null"
  }
]
```
`rootCause`: see [DelayRootCause](#delayrootcause-progress). `costImpact` is in rials.

Status Codes: 200, 401, 403

---

### Module: Delay Reasons
Method: GET
Route: /api/project-management/delay-reasons/{id}
Description: Get one delay reason.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Delay Reasons
Method: POST
Route: /api/project-management/delay-reasons
Description: Register a delay reason.
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "registerDate": "2026-01-01", "rootCause": 1, "description": "string (required, max 2000)", "timeImpactDays": 14, "costImpact": 250000000, "correctiveAction": "string | null" }
```
`timeImpactDays` and `costImpact` are optional and must not be negative.
Response: the created record, `approvalStatus: 0`.
Status Codes: 200, 400, 401, 403

---

### Module: Delay Reasons
Method: PUT
Route: /api/project-management/delay-reasons/{id}
Description: Update a delay reason.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "registerDate": "2026-01-01", "rootCause": 1, "description": "string", "timeImpactDays": 14, "costImpact": 250000000, "correctiveAction": "string | null" }
```
Response: the updated record.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Delay Reasons
Method: DELETE
Route: /api/project-management/delay-reasons/{id}
Description: Delete a delay reason. Items that are `PendingApproval` cannot be deleted (409), so a workflow instance never points at a missing subject.
Path Parameters: id (Guid, required)
Response: empty on success.
Status Codes: 200, 401, 403, 404, 409

---

### Module: Delay Reasons
Method: POST
Route: /api/project-management/delay-reasons/{id}/submit-for-approval
Description: Send the delay reason for approval. With Workflow installed it becomes `PendingApproval(1)`; without it, it is approved directly (`Approved(2)`), like every other approvable record.
Path Parameters: id (Guid, required)
Response: the updated record.
Status Codes: 200, 401, 403, 404

---

## 16. Project Documents

Requires ProjectManagement.Core. Uses NexusCore's shared local-disk `IFileStorage`. Permission
namespace: `ProjectDocuments.*` (`View`, `Upload`, `Edit`, `Delete`, `Submit`). Base route:
`/api/project-management/documents`.

**Version history.** Every file uploaded for a document is kept. The document itself always describes
its *current* (newest) file, so every endpoint below that does not mention versions behaves as before:
`fileName`, `contentType`, `sizeBytes` and `/download` are the current version's. `currentVersion` says
which version that is (1 for a document that has only ever had one file). Uploading a new version never
overwrites or deletes an older file, and deleting the document deletes all of its files.
**Existing databases need `docs/upgrade/2026-10-03-add-document-versions.sql` first** (a new table and a
new column; it also records every existing document as version 1).

### Module: Project Documents
Method: GET
Route: /api/project-management/documents
Description: List a project's documents.
Query Parameters: projectId (Guid, required)
Response:
```json
[
  {
    "id": "guid", "tenantId": "guid", "projectId": "guid", "description": "string",
    "documentType": 0, "registerDate": "2026-01-01", "fileName": "string", "contentType": "string",
    "sizeBytes": 12345, "approvalStatus": 0, "createdByUserId": "guid | null", "currentVersion": 1
  }
]
```
`documentType` is `ProjectDocumentType` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}
Description: Get one document's metadata.
Path Parameters: id (Guid, required)
Response: one item shaped as above.
Status Codes: 200, 401, 403, 404

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}/download
Description: Download the file content.
Path Parameters: id (Guid, required)
Response: **binary file stream** — not JSON.
Status Codes: 200, 401, 403, 404

---

### Module: Project Documents
Method: POST
Route: /api/project-management/documents
Description: Upload a project document.
Content-Type: **multipart/form-data**

Request Body (form fields):
```
file: <binary>
tenantId: guid
projectId: guid
description: string
documentType: int (ProjectDocumentType)
```
Response: the created document's metadata.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}/versions
Description: A document's full file history, newest first. Exactly one entry has `isCurrent: true`.
Path Parameters: id (Guid, required)
Response:
```json
[
  {
    "id": "guid | null", "documentId": "guid", "versionNumber": 2, "fileName": "plan-v2.pdf",
    "contentType": "application/pdf", "sizeBytes": 20480, "comment": "string | null",
    "isCurrent": true, "createdByUserId": "guid | null", "createdAtUtc": "2026-10-03T10:00:00+00:00"
  }
]
```
`id` is `null` only for a document uploaded before version history existed and not yet given a second file:
its one file is reported as version 1 without a stored row. Needs `ProjectDocuments.View`.

Status Codes: 200, 401, 403, 404

---

### Module: Project Documents
Method: POST
Route: /api/project-management/documents/{id}/versions
Description: Upload a new file as the document's next version. It becomes the current file; the previous files stay downloadable. Needs `ProjectDocuments.Upload`.
Content-Type: **multipart/form-data**

Path Parameters: id (Guid, required)
Request Body (form fields / query):
```
file: <binary>
comment: string (optional, max 1000 — what changed)
```
Response: the created version, shaped as in the list above, with `isCurrent: true`.

Rules: a document that is `PendingApproval(1)` refuses a new version (409), because approvers are reviewing the
current file. Any other document accepts one and goes back to `approvalStatus: 0` (NotSubmitted) — the new file has
not been approved, so an earlier approval does not carry over.

Status Codes: 200, 400, 401, 403, 404, 409

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}/versions/{versionNumber}/download
Description: Download one specific version's file.
Path Parameters: id (Guid, required), versionNumber (int, required)
Response: **binary file stream** — not JSON.
Status Codes: 200, 401, 403, 404

---

### Module: Project Documents
Method: PUT
Route: /api/project-management/documents/{id}
Description: Update a document's description/type (not the file content).
Path Parameters: id (Guid, required)
Request Body:
```json
{ "description": "string", "documentType": 0 }
```
Response: the updated metadata.
Status Codes: 200, 400, 401, 403, 404

---

### Module: Project Documents
Method: DELETE
Route: /api/project-management/documents/{id}
Description: Delete a document: its metadata and the stored file of every version.
Path Parameters: id (Guid, required)
Response: empty body.
Status Codes: 204, 401, 403, 404

---

### Module: Project Documents
Method: POST
Route: /api/project-management/documents/{id}/submit-for-approval
Description: Submit a document for approval.
Path Parameters: id (Guid, required)
Response: the updated document.
Status Codes: 200, 401, 403, 404

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}/summary
Description: **Optional integration point.** Ask a configured generator to summarize a document's content. Returns `501` by default.
Path Parameters: id (Guid, required)
Request Body: none
Response (when configured):
```json
"string (plain-text summary)"
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

### Module: Project Documents
Method: GET
Route: /api/project-management/documents/{id}/relevance
Description: **Optional integration point.** Ask a configured analyzer how relevant a document is to a given project. Returns `501` by default.
Path Parameters: id (Guid, required)
Query Parameters: projectId (Guid, required)
Request Body: none
Response (when configured):
```json
"string (plain-text relevance assessment)"
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

## 17. Project Workflow (integration)

Family × Workflow integration: lets an admin create a Project-scoped override of a Workflow
definition. Neither ProjectManagement.Core nor Workflow reference this project — it depends on
both of them, not the other way around. Permission namespace: `ProjectWorkflowIntegration.Configure`.
Base route: `/api/integrations/project-workflow`.

### Module: Project Workflow
Method: GET
Route: /api/integrations/project-workflow/subject-types
Description: List the fixed catalog of SubjectType strings the ProjectManagement family submits for approval (for an admin UI's dropdown — not a database query).
Request Body: none
Response:
```json
["Project", "WaterfallActivity", "AgileTask", "Risk", "Stakeholder", "ProgressUpdate", "DelayReason", "ProjectDocument", "Action"]
```
Status Codes: 200, 401, 403

---

### Module: Project Workflow
Method: GET
Route: /api/integrations/project-workflow/projects/{projectId}/overrides
Description: List the Project-scoped Workflow definitions configured for this project.
Path Parameters: projectId (Guid, required)
Query Parameters: tenantId (Guid, required)
Response: array of `WorkflowDefinitionDto` — same shape as [Workflow's definition list](#3-workflow).
Status Codes: 200, 401, 403

---

### Module: Project Workflow
Method: POST
Route: /api/integrations/project-workflow/projects/{projectId}/overrides
Description: Create a Project-scoped Workflow definition override for one SubjectType (validates the project exists, then delegates to Workflow's own definition-creation logic with `scopeType: "Project"`, `scopeId: projectId`).
Path Parameters: projectId (Guid, required)
Request Body:
```json
{ "tenantId": "guid", "subjectType": "string", "name": "string" }
```
(`projectId` in the body is overwritten with the path's `{projectId}` — only `tenantId`/`subjectType`/`name` are meaningful to send)

Response: the created `WorkflowDefinitionDto` (`scopeType: "Project"`, `scopeId: projectId`, `steps: []`).
Status Codes: 200, 400, 401, 403, 404 (project must exist)

---

## 17a. Project Calendar (integration)

Waterfall × Calendar integration (`Nexus.Integrations.ProjectCalendar`): makes Waterfall's schedule honour each project's
work calendar — the weekly working days and the holidays/extra working days of the `WorkCalendar` the project points
at. Neither Waterfall nor Calendar references this project; it depends on both. It owns no data and exposes no
endpoints. Register it with `AddProjectCalendarIntegration()` (before or after `AddWaterfallPlanning()`).

Without it, Waterfall still works but every day counts as a working day (`usesWorkCalendar: false` on the schedule). A
project whose calendar cannot be loaded — unknown id, or a calendar of another tenant — falls back the same way and
the schedule carries a warning.

---

## 18. Project-Strategy Alignment (integration)

Project family × Strategy integration. Neither Project Core nor Strategy references this project.
Permission namespace: `ProjectStrategyAlignment.*` (`View`, `Manage`). Base route:
`/api/integrations/project-strategy-alignment`.

### Module: Project-Strategy Alignment
Method: GET
Route: /api/integrations/project-strategy-alignment
Description: List alignment records, optionally filtered by project and/or strategy.
Query Parameters: tenantId (Guid, required), projectId (Guid, optional), strategyId (Guid, optional)
Response:
```json
[ { "id": "guid", "tenantId": "guid", "projectId": "guid", "strategyId": "guid", "alignmentLevel": 2, "alignmentPercentage": 75.0 } ]
```
`alignmentLevel` is `AlignmentLevel` — see [Enums Reference](#enums-reference).

Status Codes: 200, 401, 403

---

### Module: Project-Strategy Alignment
Method: POST
Route: /api/integrations/project-strategy-alignment
Description: Create an alignment link between a project and a strategy (validates both exist).
Request Body:
```json
{ "tenantId": "guid", "projectId": "guid", "strategyId": "guid", "alignmentLevel": 2, "alignmentPercentage": 75.0 }
```
Response: the created alignment record.
Status Codes: 200, 400, 401, 403, 404 (project or strategy must exist)

---

### Module: Project-Strategy Alignment
Method: PUT
Route: /api/integrations/project-strategy-alignment/{id}
Description: Update an alignment record's level/percentage.
Path Parameters: id (Guid, required)
Request Body:
```json
{ "alignmentLevel": 3, "alignmentPercentage": 90.0 }
```
Response: the updated record.
Status Codes: 200, 400, 401, 403, 404

---

## 19. Portfolio

Read/orchestration only — owns no data of its own; reads Project and Action from their owning
modules and applies **real backend visibility filtering** (not just UI hiding). Permission
namespace: `Portfolio.View` (base — results filtered to items the caller owns/manages) and
`Portfolio.ViewAll` (elevated — sees everything regardless of ownership). Base route: `/api/portfolio`.

### Module: Portfolio
Method: GET
Route: /api/portfolio
Description: Combined project + action list for a tenant. If the caller holds `Portfolio.ViewAll`, sees every item; otherwise the result is filtered server-side to items where the caller is Owner, Manager, or Responsible.

Query Parameters:
- tenantId (Guid, required)
- organizationUnitId (Guid, optional)
- status (string, optional — matches either a ProjectStatus or ActionStatus name, e.g. "Active")
- search (string, optional — a project's name or code, or an action's title; case-insensitive)
- involvedUserId (Guid, optional — a project's owner or manager, or an action's owner or responsible)
- type (string, optional — "Waterfall" or "Agile" returns projects only; "Action" returns actions only)
- approvalStatus (string, optional — an ApprovalStatus name, e.g. "PendingApproval")
- priority (string, optional — "Low", "Normal", "High" or "Urgent"; actions only, so projects are left out)

All filters are optional and only ever narrow the result: they are applied on top of the visibility rule, never instead of it.

Request Body: none
Response:
```json
{
  "projects": [
    {
      "id": "guid", "name": "string", "code": "string", "type": "Waterfall", "status": "Active",
      "organizationUnitId": "guid | null", "managerUserId": "guid | null", "ownerUserId": "guid | null",
      "approvalStatus": "Approved", "startDate": "2026-01-01 | null", "endDate": "2026-12-31 | null"
    }
  ],
  "actions": [
    { "id": "guid", "title": "string", "status": "Open", "organizationUnitId": "guid", "responsibleUserId": "guid | null", "ownerUserId": "guid | null", "approvalStatus": "NotSubmitted", "startDate": "2026-01-01 | null", "endDate": "2026-01-31 | null", "priority": "Normal" }
  ]
}
```
Note: unlike everywhere else in this document, Portfolio's `type`/`status`/`approvalStatus` fields
are already **stringified enum names** (`project.Type.ToString()` etc. at the service layer), not
raw integers — this is the one endpoint where you do not need the enum-value table.

Status Codes: 200, 401, 403

---

## 20. Reporting

Read/orchestration only — owns no data. Progress Management is an **optional runtime dependency**:
if it isn't installed, progress-derived fields are simply `null` rather than the endpoint failing.
Permission namespace: `Reporting.View` (base — own dashboard only) and `Reporting.ViewAll`
(elevated — tenant-wide summary and any project's dashboard).

### Module: Reporting
Method: GET
Route: /api/reporting/summary
Description: Tenant/organization-unit-wide aggregate counts. Requires `Reporting.ViewAll`.
Query Parameters: tenantId (Guid, required), organizationUnitId (Guid, optional)
Response:
```json
{
  "projectCount": 42,
  "runningProjectCount": 10,
  "actionCount": 87,
  "runningActionCount": 20,
  "projectsByStatus": [ { "key": "Active", "count": 10 } ],
  "projectsByOrganizationUnit": [ { "key": "guid-or-Unassigned", "count": 5 } ],
  "projectsByManager": [ { "key": "guid-or-Unassigned", "count": 3 } ]
}
```
"Running" = Active or OnHold projects / Open or InProgress actions.

Status Codes: 200, 401, 403 (403 also returned when authenticated but lacking `Reporting.ViewAll`)

---

### Module: Reporting
Method: GET
Route: /api/reporting/me
Description: The caller's own dashboard (projects/actions they own, manage, or are responsible for). Requires only the base `Reporting.View`.
Query Parameters: tenantId (Guid, required)
Response:
```json
{
  "myRunningProjectCount": 3,
  "myRunningActionCount": 5,
  "myProjectIds": ["guid"],
  "myActionIds": ["guid"]
}
```
Status Codes: 200, 401, 403

---

### Module: Reporting
Method: GET
Route: /api/reporting/projects/{projectId}
Description: One project's dashboard (status + latest progress figures). Requires `Reporting.ViewAll`.
Path Parameters: projectId (Guid, required)
Response:
```json
{
  "projectId": "guid", "name": "string", "status": "Active",
  "latestPlannedProgress": 30.0, "latestActualProgress": 25.0,
  "deviation": -5.0, "performanceClassification": "AtRisk"
}
```
The last four fields are `null` if Progress Management isn't installed, or the project has no
progress updates yet. Note `status`/`performanceClassification` here are also stringified, like Portfolio.

Status Codes: 200, 401, 403, 404

---

## Enums Reference

Every enum below is serialized as its **raw integer** value in both requests and responses
(no `JsonStringEnumConverter` is configured anywhere in this codebase).

### ActionStatus (Actions)
| Value | Name |
|---|---|
| 0 | Open |
| 1 | InProgress |
| 2 | Completed |
| 3 | Cancelled |

### ActionPriority (Actions)
| Value | Name |
|---|---|
| 0 | Low |
| 1 | Normal |
| 2 | High |
| 3 | Urgent |

### DelayRootCause (Progress)
| Value | Name |
|---|---|
| 0 | Funding |
| 1 | Procurement |
| 2 | Permits |
| 3 | HumanResources |
| 4 | Other |

### StakeholderQuadrant (Stakeholders)
| Value | Name | Meaning |
|---|---|---|
| 0 | Monitor | low-to-medium power, low-to-medium interest |
| 1 | KeepInformed | low-to-medium power, high interest |
| 2 | KeepSatisfied | high power, low-to-medium interest |
| 3 | ManageClosely | high power, high interest |

### DependencyType (Waterfall)
| Value | Name | Meaning |
|---|---|---|
| 0 | FinishToStart | the successor starts after the predecessor finishes |
| 1 | StartToStart | the successor starts after the predecessor starts |
| 2 | FinishToFinish | the successor finishes after the predecessor finishes |
| 3 | StartToFinish | the successor finishes after the predecessor starts |

### VarianceStatus (Waterfall)
| Value | Name |
|---|---|
| 0 | OnTrack |
| 1 | Late |
| 2 | Early |
| 3 | Added |
| 4 | Removed |

### ApprovalStatus (shared across every capability that supports optional approval)
| Value | Name |
|---|---|
| 0 | NotSubmitted |
| 1 | PendingApproval |
| 2 | Approved |
| 3 | Rejected |

### ProjectType (Projects)
| Value | Name |
|---|---|
| 0 | Waterfall |
| 1 | Agile |

### ProjectStatus (Projects)
| Value | Name |
|---|---|
| 0 | Draft |
| 1 | Active |
| 2 | OnHold |
| 3 | Completed |
| 4 | Archived |

### ProjectSortBy (Projects — query parameter only, not a response field)
| Value | Name |
|---|---|
| 0 | Name |
| 1 | Code |
| 2 | StartDate |
| 3 | EndDate |
| 4 | Status |
| 5 | CreatedAtUtc |

### DayOfWeekMask (Calendar) — `[Flags]` bitmask, values combine with bitwise OR
| Value | Name |
|---|---|
| 0 | None |
| 1 | Sunday |
| 2 | Monday |
| 4 | Tuesday |
| 8 | Wednesday |
| 16 | Thursday |
| 32 | Friday |
| 64 | Saturday |
| 79 | IranWorkWeek (= Sat+Sun+Mon+Tue+Wed = 64+1+2+4+8) |
| 127 | AllDays (all seven bits) |

### WorkflowInstanceStatus (Workflow)
| Value | Name |
|---|---|
| 0 | InProgress |
| 1 | Approved |
| 2 | Rejected |

### AgileTaskStatus (Agile Tasks)
| Value | Name |
|---|---|
| 0 | ToDo |
| 1 | InProgress |
| 2 | Done |
| 3 | UnderReview (appended: on the board it sits between InProgress and Done) |

### SprintStatus (Agile Tasks)
| Value | Name |
|---|---|
| 0 | Planned |
| 1 | Active |
| 2 | Completed |

### AgileTaskPriority (Agile Tasks)
| Value | Name |
|---|---|
| 0 | Low |
| 1 | Medium |
| 2 | High |
| 3 | Critical |

### DeliverableStatus (Deliverables)
| Value | Name |
|---|---|
| 0 | Planned |
| 1 | InProgress |
| 2 | Delivered |
| 3 | Accepted |
| 4 | Rejected |

### KpiType (KPI)
| Value | Name |
|---|---|
| 0 | Lag (measures an outcome after the fact) |
| 1 | Lead (measures a leading indicator, predictive) |

### PowerLevel / InterestLevel (Stakeholders) — same three values, two separate axes of the standard power/interest grid
| Value | Name |
|---|---|
| 0 | Low |
| 1 | Medium |
| 2 | High |

### PerformanceClassification (Progress) — derived server-side from `deviation = actualProgress - plannedProgress`
| Value | Name | Rule |
|---|---|---|
| 0 | OnTrack | deviation >= -5 |
| 1 | AtRisk | deviation >= -15 |
| 2 | Behind | deviation < -15 |

### ProjectDocumentType (Project Documents)
| Value | Name |
|---|---|
| 0 | Report |
| 1 | Letter |
| 2 | MeetingMinutes |
| 3 | Other |

### KnowledgeDocumentType (Knowledge)
| Value | Name |
|---|---|
| 0 | Book |
| 1 | Software |
| 2 | Notes |
| 3 | Other |

### AlignmentLevel (Project-Strategy Alignment)
| Value | Name |
|---|---|
| 0 | None |
| 1 | Low |
| 2 | Medium |
| 3 | High |

---

*Generated from the live `/swagger/v1/swagger.json` (routes, parameters, request bodies) cross-referenced
against each module's DTO and service-interface source (response bodies, precise nullability, status
codes) — see the final report in this session for the exact verification steps (real `dotnet build`,
`dotnet test`, and a live run of NexusCore.Api).*
