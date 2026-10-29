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
- **Posts** («پست سازمانی») — one place for one person: a position in a unit (several posts of
  one position are fine, e.g. three «کارشناس») or directly in the company (the head's, board
  members'). Each has a code, unique in its company (by default the company's next number).
  The head's post is the one directly in the company of its position with no superior.
  A post may sit under another post of the same place (`parentPostId`, e.g. «کارشناس» under
  «معاون»; any depth): the holder of the post above manages its holder (see below). Set it when
  adding the post or with `PUT /unit-positions/{id}/parent`; a post with posts under it can't be
  removed until they move.
- **Tasks given to posts** (optional, `OrgStructure:AssignTasksToPosts`) — a task's creator,
  responsible people and access list are posts; their holders are the task's people. When a
  post's holder changes, its tasks move to the new holder (`org_structure.TaskPosts` keeps which
  posts each task has). A vacant post can't be given a new task. On start-up, tasks that have no
  posts yet are given to their people's main posts.
- **Substitution inbox** («کارتابل جانشینی», `GET/PUT/DELETE /substitution[/{postId}]`) — the open
  tasks of vacant posts, shown (title, due date, status) to the managers above each post and to
  holders of `org_structure.manage_substitution`. They may put another post of the company in
  charge of a vacant one (سرپرستی): its holder sees the tasks and comments on them as a stand-in
  («جانشین ...» next to their name), nothing more, until someone takes the vacant post.
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
2. the holder of the post above the user's main post (the nearest one someone else holds; a
   vacant post above is passed over) — so the post above also gets the user's tasks in its
   reports, may give them tasks with the assignment limit on, and sees their post in the
   substitution inbox when it falls vacant; it gets no rights over the chart itself;
3. the manager of the user's unit, or of the nearest unit above it — up to the unit linked to the
   company (units above that are not the company's);
4. the company's head.

So a unit's head reports to the head of the unit above, and the head of a company's top unit
reports to the company's head. Writes that would create a loop are rejected; reads are
loop-safe even if units are later rearranged in `Nexus.Organization`.

## Three boundaries that are not the same thing

The dynamic structure (units under units, kinds of node, secondary relations) uses three words
that must not be mixed up:

1. **The owning organization** — the **company** (`Company` and `CompanyUnit`). It is the boundary of
   access and reach: with `OrgStructure:LimitToOwnCompany` people see, message and give tasks only
   inside it. A unit belongs to a company through the **top unit of its branch** — the only unit
   that has a `CompanyUnit` link. A unit nested below it belongs to the same company; it is never
   linked itself. (`OrgGraph.CompanyOfUnit` walks up to the linked unit.)
2. **The root of the whole structure** — each company's **implicit root**. It has no row and no
   manager: a unit with no parent (or whose parent is not a unit, e.g. the company) is at the top
   of its company. So a structure where no unit has a parent is exactly the flat structure that
   existed before nesting, and gives the same managers, subordinates, task-assignment limits and
   stand-ins. A root with a manager would change those results, so there is none.
3. **«Same deputy»** (هم‌معاونت) — `OrgGraph.DomainOf(unit)`: the nearest unit at or above it whose
   node type has `IsDomainBoundary` (true for a plain unit and by default for every type). Two
   people are in the same deputy when their units have the same domain. It decides who a team
   builder may add and who counts as at one's level in one's own deputy. With no nesting every
   unit is its own domain, so nothing changes.

Moving a node from one company to another is **not allowed** until the rules for it exist (the
move endpoint refuses it with a validation error); the top unit's company link is changed with
`PUT /company-units/{unitId}` as before, and only while nobody sits in its branch.

All of these read one graph, `OrgGraph`, built once per operation by `IOrgGraphProvider` from the
units, their company links and their node types; `HierarchyResolver` walks the same graph.

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

## Nodes: units under units (dynamic structure, phase 2)

Units are the structure's **nodes** and may sit under each other. The flat structure is unchanged
until two things are both true:

- `OrgStructure:AllowNesting` is `true` (temporary switch, **off by default**; with it off a node is
  only made and moved at the top of its company, as before), and
- the organization has «what may sit under what» rules (`/node-type-rules`). No rules: flat only.

Only the top node of each branch is linked to a company; the nodes under it belong to the same
company. Every change is made in the organization's turn (one change at a time, on SQL Server with an
application lock across servers), checked against the data as it is then (loops, the rules, the
company), saved with the node's version and written to `org_structure.OrgChangeLogs`. Send back the
`rowVersion` you read: a save over someone else's newer change is refused with 409.

| | |
|---|---|
| `GET /nodes?companyId=`, `GET /nodes/{id}` | nodes (`parentNodeId`, `nodeTypeId`, `companyId`, `sortOrder`, `childCount`, `rowVersion`) |
| `POST /nodes` | `{ name, parentNodeId?, companyId?, nodeTypeId? }` (no type: the plain «واحد») |
| `PUT /nodes/{id}` | `{ name, nodeTypeId?, rowVersion? }` |
| `PUT /nodes/{id}/manager` | `{ managerUserId, rowVersion? }` |
| `POST /nodes/{id}/move` | `{ parentNodeId, companyId?, rowVersion? }` (null parent: to the top of `companyId` or its own company) |
| `POST /nodes/{id}/reorder` | `{ position, rowVersion? }` among the nodes beside it |
| `POST /nodes/{id}/deactivate`, `/activate` | refused (409, with the list) while active nodes, posts or people depend on it; nothing is deleted |
| `GET /nodes/search?q=&companyId=` | by name, post code or the person in it; each hit with its path from the top |
| `GET /nodes/{id}/ancestors` | the nodes above it, from the top |
| `GET/POST /node-types`, `PUT/DELETE /node-types/{id}` | types: name, `color` (#rrggbb), `icon` (lucide name), `isDomainBoundary` |
| `GET/PUT /node-type-rules` | `{ rules: [{ parentNodeTypeId?, childNodeTypeId }] }`; returns the nodes that do not match (nothing is changed) |
| `POST /impact-preview` | `{ changes: [{ kind: "move"/"add"/"manager"/"unlink", nodeId, parentNodeId, companyId, managerUserId, name }] }`: per person, before and after: manager, people under them, whom they cannot give tasks to, substitution inbox, work team. Nothing is saved |

`GET /chart` keeps its fields and adds: on each unit `children` (the nodes under it, nested),
`parentNodeId`, `nodeType` (`name`, `color`, `icon`, `isDomainBoundary`) and `links`; on each company
`uiLevel` (`simple` / `normal` / `advanced`). `units` of a company are its top nodes.

Permissions, each operation on its own; every one is also passed by the older permission that
covered it, so nobody loses anything:

| Permission | Also passed by |
|---|---|
| `org_structure.node.view` | `org_structure.view` and the other structure permissions, `organization_units.view` |
| `org_structure.node.create` | `organization_units.create` |
| `org_structure.node.update` | `organization_units.update` |
| `org_structure.node.move` (moving and ordering) | `org_structure.manage_companies` |
| `org_structure.node.deactivate` | `organization_units.delete` |
| `org_structure.node.assign` (a node's manager) | `organization_units.update` |
| `org_structure.manage_rules` (types and rules) | - |
| `org_structure.manage_templates` (saving and removing templates) | - |

## Secondary links and templates (phase 4)

A **secondary link** joins two nodes of the same company outside the tree (a committee to the board,
internal audit to the audit committee). Its type has a line style (`solid`, `dotted`, `dashed`) and
`countsAsManager`. A dotted line never counts as manager. A link that counts gives the people of the
source node an **extra manager**: the target node's manager. Their main manager, the people under
anyone, the task limit and the substitution inbox do not change. No counting link (the default):
nothing changes at all.

An **automatic rule** says that a new node of one type is linked to the one node of another type in
its company. One target: linked when the node is made. Several: not linked; the new node comes back
with `autoLinkChoices` for the user to pick (`POST /links`). None: nothing happens (the rule waits).
Existing nodes are linked only through the preview: `POST /auto-link-rules/preview` lists what the
rules would add, and `POST /auto-link-rules/apply` adds those (any link not in the current plan: 409).

A **structure template** is a copy of a structure: node types, rules, link types, automatic rules,
positions, the posts each node type gets, nodes and links, and the screen level. Three are built in
(`simple`, `deputy`, `bank`, from `Nexus.OrgStructure/Templates`, with fixed ids). An organization
can save any of its companies as its own template. `POST /companies` with `templateId` makes the
company and adds the template's copy: a type, link type or position that already exists (same code,
or same name/title) is reused. Rules are for the whole organization, so the template's rules are
added to them. If the organization had none (flat), the places its nodes are in now become rules
first, so no existing node becomes a mismatch. A template with nested nodes needs `AllowNesting`.
Posts are not made from a template yet (the posts each type should get are saved). Later changes or
removing the template do not touch companies made from it.

| | |
|---|---|
| `GET/POST /link-types`, `PUT/DELETE /link-types/{id}` | `{ name, code?, lineStyle, countsAsManager }`; a type in use is not removed |
| `GET/PUT /auto-link-rules` | `{ rules: [{ sourceNodeTypeId, targetNodeTypeId, linkTypeId }] }` |
| `POST /auto-link-rules/preview`, `POST /auto-link-rules/apply` | `{ links: [] }` (one target each) and `ambiguous`; apply `{ links: [{ fromNodeId, toNodeId, linkTypeId }] }` |
| `GET /links?nodeId=`, `POST /links`, `DELETE /links/{id}` | `{ fromNodeId, toNodeId, linkTypeId }`: same company, not to itself, not twice |
| `PUT /companies/{id}/ui-level` | `{ uiLevel: "simple" / "normal" / "advanced" }` (only hides things in the screens) |
| `GET /users/{userId}/extra-managers` | the managers someone has through links that count (their own, or with a read permission) |
| `GET /templates`, `GET /templates/{id}` | the templates; one with its whole content |
| `POST /templates`, `DELETE /templates/{id}` | `{ companyId, name, code?, description? }`; built-in ones are not removed |

## A node's posts (phase 6.1)

A node has at most one **manager post** (`PUT /unit-positions/{id}/manager-post { isManagerPost }`;
a second one is refused, 409). Marking or unmarking it changes no other post. A post added to a node
with no post above given goes under the node's manager post; existing posts are never moved under it.

Each node type can say which posts its nodes get (`GET/PUT /node-types/{id}/posts`:
`{ posts: [{ positionId, isManagerPost }] }`, at most one manager post). `POST /post-templates/apply`
`{ nodeId }` or `{ nodeTypeId }` makes the posts a node lacks: a position the node already has is not
made again, the manager post only when the node has none, and the new posts go under its manager
post. A position that is not for the node's company is listed in `skipped`, not made. Running it
again makes nothing. A company made from a template gets its posts this way too: the template's
posts directly in the company (the head's) and each node's posts from its type.

`/impact-preview` also takes `{ kind: "link", nodeId, toNodeId, linkTypeId }` and
`{ kind: "unlink-link", linkId }`, and lists each person's `extraManagersAdded` / `extraManagersRemoved`.

| Permission | for |
|---|---|
| `org_structure.node.view` | reading link types, rules, links, templates; the previews |
| `org_structure.manage_rules` | link types, automatic rules, applying them |
| `org_structure.node.update` | adding and removing links |
| `org_structure.manage_companies` | the screen level; making a company (with or without a template) |
| `org_structure.manage_templates` | saving and removing templates |

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
