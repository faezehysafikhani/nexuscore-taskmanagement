# Organization structure (optional module)

`Nexus.OrgStructure` adds, per tenant:

- **Companies** — several companies can share one tenant (a holding and its subsidiaries). Each
  can have a head («رئیس کل»), not chosen on the company: see positions.
- **Which company each unit belongs to** — a top unit from `Nexus.Organization` (e.g. a deputy,
  «معاونت») is linked to a company; every unit below it belongs to the same company.
- **Positions** («سِمَت») — job titles such as «کارشناس» or «معاون». Not permission roles. Each
  is for one or more companies (`companyIds`) and is given only there. Its superior
  (`parentPositionId`) must be for the same companies. In each company exactly one position has
  no superior: the head's. Whoever is placed directly in the company (no unit) with it is the
  company's head — one person at a time.
- **Each user's place** — company, optional unit, optional position, optional direct manager.
- **Manager / subordinates** — derived, see below.

It is **optional**. A product that does not reference the two packages has none of it: no tables,
no endpoints, no permissions. NexusCore and `Nexus.Organization` do not reference it and their
tables are unchanged (a test enforces this), so products such as PostBank or TMPB are unaffected.

## Turning it on in a product

Requires `Nexus.Organization` too (its units are the deputies / departments).

```csharp
builder.Services.AddOrganizationApplication();
builder.Services.AddOrgStructure();

builder.Services.AddOrganizationInfrastructure(builder.Configuration);
builder.Services.AddOrgStructureInfrastructure(builder.Configuration);

app.MapOrganizationEndpoints();
app.MapOrgStructureEndpoints();

// at startup, after DefaultDataSeeder.SeedAsync:
await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<OrganizationDbContext>(), ct);
await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<OrgStructureDbContext>(), ct);
```

The tables are created in the `DefaultConnection` database under the `org_structure` schema
(`Companies`, `CompanyUnits`, `Positions`, `UserAssignments`). No SQL script is needed.

## Who is whose manager

In order, the first that applies (and is not the user themselves):

1. the manager set on the user's own assignment;
2. the manager of the user's unit, or of the nearest unit above it — up to the unit linked to the
   company (units above that are not the company's);
3. the company's head.

So a unit's head reports to the head of the unit above, and the head of a company's top unit
reports to the company's head. Writes that would create a loop are rejected; reads are
loop-safe even if units are later rearranged in `Nexus.Organization`.

## Work teams in the chart

The chart shows the work teams people build for their tasks and chat (the user-group feature's
personal teams, read through the Core's `IWorkTeamDirectory`): each team under the unit of the
person who built it, with all its members - from any unit, so a person in several teams appears
more than once. A unit's people who are in no work team are listed apart. Teams whose builder is
in no unit of a company come from `/teams-outside-units`. The module stores no teams of its own
(migration `RemoveOrgTeams` drops the short-lived organizational teams).

## Whom a work team's builder may add

With this module, the builder of a work team adds the people of their own unit (deputy) only;
`org_structure.team_members_any_unit` lets them add people of other units too. A builder in no
unit (directly under a company, or with no place yet) is not limited. Only people being added are
checked: members a team already has stay. The module provides this through the Core's
`IWorkTeamMemberScope`; a product without the module keeps the Core's default (anyone may be
added). `GET /api/identity/groups/mine/member-scope` tells the UI whom the caller may add.

## Titles before names

A list of titles per organization («آقای»، «خانم»، «جناب مهندس»...) and one per user. A user's
display name starts with their title, so every module shows it; renaming a title renames its
holders. The name is set through the Core's `IUserTitleWriter` (no table of the Core changes).
They go with user administration: `users.create` manages the list, `users.create` or
`users.update` gives a user a title. Saving a user's first and last name rebuilds their display
name, so the UI sets the title again after saving.

## Permissions (module "Organization")

| Permission | Allows |
|---|---|
| `org_structure.view` | read companies, positions, assignments, anyone's manager/subordinates, the chart |
| `org_structure.manage_companies` | companies, and linking units to them |
| `org_structure.manage_positions` | positions |
| `org_structure.manage_assignments` | each user's company / unit / position / manager |
| `org_structure.team_members_any_unit` | add people of other units to one's work teams (gives no access to the structure) |

Any of the first four also allows reading. Every signed-in user may read their own place (`/me`).
The built-in administrator role gets all of them automatically.

## Endpoints (`/api/org-structure`)

| | |
|---|---|
| `GET/POST /companies`, `GET/PUT/DELETE /companies/{id}` | companies |
| `GET /company-units`, `PUT/DELETE /company-units/{unitId}` | link a unit (and its branch) to a company: `{ "companyId": ... }` |
| `GET/POST /positions`, `PUT/DELETE /positions/{id}` | positions |
| `GET /assignments?companyId=&unitId=`, `GET/PUT/DELETE /assignments/{userId}` | a user's place: `{ "companyId", "unitId", "positionId", "managerUserId" }` |
| `GET /users/{userId}/manager`, `GET /users/{userId}/subordinates?all=true` | one user's manager / subordinates |
| `GET /me`, `GET /me/subordinates?all=true` | the signed-in user's place, management chain and subordinates |
| `GET /chart?companyId=` | companies → units → people |

Deleting a company or position that is in use is refused (deactivate it instead); so is
unlinking or moving a unit while people sit in its branch.

## Trying it: the Tejarat Bank host

The product that composes NexusCore + Organization + this module is built outside this
repository, on the NexusCore packages: **TejaratBank_API** (a separate repository, like TMPB_API
for Post Bank). Set `DefaultConnection` in its `appsettings`, create the Organization and
OrgStructure schemas (see its `DEPLOYMENT-CONFIG.md`), run it, open `/swagger`, sign in with
`POST /api/identity/auth/login` (`{ "identifier": "admin", "password": "Admin@12345" }` — the
development default) and authorize with the access token. A typical first run:

1. `POST /api/organization/units` — create a deputy (and units under it).
2. `POST /api/org-structure/companies` — create the company.
3. `PUT /api/org-structure/company-units/{deputyId}` — put the deputy in the company.
4. `POST /api/org-structure/positions` — create positions: first the head's (no superior), then
   the rest under it, each with its `companyIds`.
5. `PUT /api/org-structure/assignments/{userId}` — place users.
6. `GET /api/org-structure/chart`, `/users/{id}/manager`, `/users/{id}/subordinates?all=true`.
