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
`Edit`, `Delete`, `Submit`). Base route: `/api/project-management/waterfall/activities`.

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
    "plannedProgress": 0.0, "actualProgress": 0.0, "approvalStatus": 0
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
Description: **Optional AI integration point.** Ask a configured WBS generator to suggest a breakdown for a project goal. Returns `501` if no `IWbsGenerator` implementation is registered (none is, by default — this is a pure extension point).

Query Parameters: projectId (Guid, required), projectGoal (string, required)
Request Body: none (both inputs are query parameters, not a JSON body)
Response (when a generator is configured):
```json
[ { "name": "string", "description": "string | null", "durationDays": 5, "weight": 1.0 } ]
```
Response (when no generator is configured, i.e. always in this codebase's default installation):
RFC 7807 Problem Details, `status: 501`, `detail: "AI WBS generation is not configured for this deployment."`

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default — no provider registered)

---

## 9. Agile Tasks

Requires ProjectManagement.Core. Explicitly independent of Waterfall (neither references the
other) — a project can use one, the other, both, or neither. Permission namespace:
`AgileTasks.*` (`View`, `Create`, `Edit`, `Delete`, `Submit`). Base route: `/api/project-management/agile/tasks`.

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
    "dueDate": "2026-01-01 | null", "priority": 1, "sprintNumber": 3, "approvalStatus": 0
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
Description: **Optional AI integration point.** Suggest agile tasks for a project goal. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectGoal (string, required)
Request Body: none
Response (when configured):
```json
[ { "title": "string", "description": "string | null", "priority": 1 } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

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
Description: **Optional AI integration point.** Ask a configured analyzer to suggest risks for a project context. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectContext (string, required)
Request Body: none
Response (when configured):
```json
[ { "description": "string", "probabilityScore": 3, "severityScore": 4, "impactScore": 2, "suggestedResponsePlan": "string | null" } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

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
Description: **Optional AI integration point.** Suggest stakeholders for a project context. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required), projectContext (string, required)
Request Body: none
Response (when configured):
```json
[ { "name": "string", "isInternal": true, "expectations": "string | null", "engagementStrategy": "string | null" } ]
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

## 15. Progress

Requires ProjectManagement.Core. Explicitly does **not** reference Waterfall — Deviation and
PerformanceClassification are computed server-side from Planned/Actual progress and are never
accepted as input. `ConfirmedProgress` is set only when the update is approved (via
`SubmitForApproval` → Workflow, or the default auto-approve). Permission namespace:
`Progress.*` (`View`, `Create`, `Edit`, `Submit`). Base route: `/api/project-management/progress-updates`.

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
Description: **Optional AI integration point.** Ask a configured generator for a natural-language executive summary of a project's progress. Returns `501` by default (no provider registered).

Query Parameters: projectId (Guid, required)
Request Body: none
Response (when configured):
```json
"string (plain-text executive summary)"
```
Response (default): RFC 7807 Problem Details, `status: 501`.

Status Codes: 200 (if a provider is plugged in), 401, 403, 501 (default)

---

## 16. Project Documents

Requires ProjectManagement.Core. Uses NexusCore's shared local-disk `IFileStorage`. Permission
namespace: `ProjectDocuments.*` (`View`, `Upload`, `Edit`, `Delete`, `Submit`). Base route:
`/api/project-management/documents`.

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
    "sizeBytes": 12345, "approvalStatus": 0, "createdByUserId": "guid | null"
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
Description: Delete a document (metadata and stored file).
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
Description: **Optional AI integration point.** Ask a configured generator to summarize a document's content. Returns `501` by default.
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
Description: **Optional AI integration point.** Ask a configured analyzer how relevant a document is to a given project. Returns `501` by default.
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
["Project", "WaterfallActivity", "AgileTask", "Risk", "Stakeholder", "ProgressUpdate", "ProjectDocument", "Action"]
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

Request Body: none
Response:
```json
{
  "projects": [
    {
      "id": "guid", "name": "string", "code": "string", "type": "Waterfall", "status": "Active",
      "organizationUnitId": "guid | null", "managerUserId": "guid | null", "ownerUserId": "guid | null",
      "approvalStatus": "Approved"
    }
  ],
  "actions": [
    { "id": "guid", "title": "string", "status": "Open", "organizationUnitId": "guid", "responsibleUserId": "guid | null", "ownerUserId": "guid | null", "approvalStatus": "NotSubmitted" }
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
