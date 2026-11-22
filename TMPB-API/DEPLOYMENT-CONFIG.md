# Deployment Configuration — TMPB API

What to set when installing this API at a new site (e.g. a bank environment). None of these
steps touch source code, and none require a rebuild.

## 1. Set the frontend's origin (CORS)

Edit `appsettings.Production.json` — the only file this install step touches:

```json
{
  "Cors": {
    "AllowedOrigins": ["https://<bank-host-or-ip>:<frontend-port>"]
  }
}
```

Use the exact scheme + host + port the frontend is served from. If the frontend and this API
are served from the same public address (a reverse proxy in front of both), you can leave this
empty — same-origin requests don't need CORS at all.

This file is loaded automatically: `ASPNETCORE_ENVIRONMENT` defaults to `Production` when unset,
which is the normal case for a real install (outside an IDE launch profile).

## 2. Set the JWT signing key

Never put a real signing key in any `appsettings*.json` file — it must not be committed. Set it
as an environment variable instead:

```
Jwt__SigningKey=<a real secret, at least 32 characters>
```

Set it however this environment sets process environment variables for the app — an IIS
Application Pool's environment variables, a Windows service's environment, a systemd unit file,
etc. If this is missing, a known development placeholder, or shorter than 32 characters, **the
app refuses to start** — this is intentional (the check in `Program.cs`) and is not a bug; set
the key and start it again.

## 3. Set the database connection string (if not using the local default)

`appsettings.json`'s default (`Server=.`, i.e. this same machine) works if SQL Server runs on
the same host. Otherwise, add to `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<db-host>;Database=TMPB;Trusted_Connection=True;TrustServerCertificate=True;",
    "Chat": "Server=<db-host>;Database=TMPB;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

## 4. AllowedHosts

`"AllowedHosts": "*"` in `appsettings.json` (the base file) accepts any Host header, which is
the standard ASP.NET Core default and fine behind a reverse proxy/firewall. Tighten it in
`appsettings.Production.json` only if this site's policy requires restricting it to a specific
host name.

## 5. Start/restart

Restart the API (IIS site recycle, service restart, etc.). No `dotnet build`, no source change.

## 6. Organization structure schema (deputies, companies, positions)

Nothing to do by default: with `Database:InitializeOrgStructureSchema` = `true` (in
`appsettings.json`) the API creates the `organization` and `org_structure` tables on a new
database and applies pending migrations on an existing one, at start-up. It never creates a
table or column that already exists.

Where schema changes must be run by a DBA instead, set it to `false` in
`appsettings.Production.json` and run the scripts in `db/` (each needs `-I`, QUOTED_IDENTIFIER):

| Database | Script |
|---|---|
| New (no `organization` / `org_structure` tables yet) — after the API has started once | `db/new-database-org-structure.sql` |
| Already has them (an earlier version) | `db/upgrade-1-organization.sql`, then `db/upgrade-2-org-structure.sql`, then `db/upgrade-3-unit-positions.sql`, then `db/upgrade-4-position-companies.sql`, then `db/upgrade-5-posts.sql`, then `db/upgrade-6-task-posts.sql`, then `db/upgrade-7-substitution.sql`, then `db/upgrade-8-post-parent.sql`, then `db/upgrade-9-task-history.sql`, then `db/upgrade-10-org-nodes.sql`, then `db/upgrade-11-node-api.sql`, then `db/upgrade-12-templates.sql`, then `db/upgrade-13-node-details.sql`, then `db/upgrade-14-appointments.sql`, then `db/upgrade-15-company-profile.sql` |
| Sign-in sessions (NexusCore `0.1.0.1405081201-mahdi`) | `db/upgrade-sessions.sql` (this repository; also in the NexusCore repository): the session tables and the columns they add. The API also creates them at start-up when `Database:SeedOnStartup` is `true`. |

```
sqlcmd -S <db-host> -d TMPB -I -i db/upgrade-1-organization.sql
```

To see which of these steps a database already has (read-only, changes nothing; it recognises each
step by the tables and columns it adds and says which script to start from):

```
sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/check-upgrades.sql
```

Positions are per company, and a company's head is not chosen on the company: whoever is placed
directly in the company with its position that has no superior («رئیس کل») is its head. The
upgrade gives each position the companies it is used in, and gives today's heads that position.
Positions it found with no superior keep none: give each one its superior (only the head's
position stays without one).

Posts (upgrade 5): each post has a code (unique in its company) and one person. A position
several people held in a deputy becomes one post each; people placed directly in a company with
a position get a post there (the head's included); every post gets a number of its company.

Structure nodes (upgrade 10): units get an optional parent, node type and validity period, companies a
group/template/screen level, and posts the head's mark; new tables hold node types, rules, links and
templates. Today's data keeps its meaning: every unit stays at the top of its company (no parent), every
unit becomes a plain «واحد» node and each company's head post gets the head's mark. Run
`db/check-upgrade-10.sql` before and after (section A must be identical); `db/upgrade-10-rollback.sql` undoes it.

Node API (upgrade 11): units get their order among the units beside them, node types a colour and
an icon. Existing data keeps its meaning (order 0: still by name; the default colour and icon).
`db/upgrade-11-rollback.sql` undoes it. Putting a node under another node through
`/api/org-structure/nodes` needs «what may sit under what» rules (`/node-type-rules`); with none, nodes
stay at the top of their company, as before. (The temporary `OrgStructure:AllowNesting` switch of the
trial phases is gone; an old `appsettings.json` that still has it is harmless.)

Templates (upgrade 12): structure templates get a column for a saved template's content
(`StructureTemplates.Content`, empty for existing rows). Nothing else changes; secondary links and
the built-in templates use tables upgrade 10 already made. `db/upgrade-12-rollback.sql` undoes it
(templates saved from a company are lost). A built-in template with nested nodes («بانکی») brings
its own rules.

Node details (upgrade 13): units get five optional columns (`NodeCode`, `LatinName`,
`CorrespondenceTitle`, `Phone`, `Address`), empty for existing rows. The code column is named
`NodeCode` on purpose: `check-upgrades.sql` recognises step 1 by the old `Code` column being gone.
`db/upgrade-13-rollback.sql` undoes it.

Appointments over time (upgrade 14): `UserPositions` gets `StartDate` and `EndDate`; today's rows
start on the day they were saved and have no end, so everyone holds what they hold now. The unique
index becomes «one open appointment per post». From then on, taking a post off someone ends their
appointment and keeps it as history. `db/upgrade-14-rollback.sql` undoes it: the history (ended
appointments and ones from a later day) is lost; the appointment on now stays. (Importing nodes from an Excel/CSV file was removed from the product; the structure is built in the UI.)

Company profile (upgrade 15): `org_structure.Companies` gets eight optional columns (`Phone`, `Email`,
`Website`, `NationalId`, `EconomicCode`, `Address`, `HeadTitle` - what the chart calls the head - and `Logo`,
where its logo file is kept in the API's file storage). Empty for every existing company. A new company starts
at the «advanced» level and gets an automatic code (`C-001`...) when none is given.
`db/upgrade-15-rollback.sql` undoes it (the values are lost; logo files stay unused).

Stand-in for a set time (upgrade 17, `db/upgrade-17-stand-in-period.sql`): `org_structure.PostActings` gets
`StartDate` and `EndDate` (both optional, empty for every existing stand-in). With an end date a stand-in
(«تعیین جانشین» in a post's details) is on from its start until its end, also while the post has a holder
(leave, a mission); without one it lasts while the post is vacant, as before. Only people of the post's own unit
(or a unit below it) may stand in. `db/upgrade-17-rollback.sql` undoes it (stand-ins for a set time are removed).

Calendar meetings (upgrade 18, `db/upgrade-18-calendar-meetings.sql`): the calendar's meetings («ایجاد جلسه»; the people a user may invite follow the
organization structure; the minutes make tasks) keep their data in two new tables, `calendar.Meetings` and `calendar.MeetingParticipants`.
With `Database:InitializeMeetingSchema` true (the default) the API creates them at start-up; set it to `false` in
`appsettings.Production.json` and run the script when a DBA owns the schema. Nothing existing changes. Who may create, see, edit or delete
meetings is decided by the permissions of the roles (`calendar` group of the role screen): a user without «ایجاد جلسه» sees the button disabled.
Same as TejaratBank-API (where this step is numbered 17).

### 6.1 Moving a production database to the dynamic structure

Do this once, in this order (steps 10 to 15). Nothing here changes what today's flat data means: every unit stays at
the top of its company, every company keeps its head and the people keep their managers.

1. **Back up** the `TMPB` database.
2. **Rehearse on a copy** (staging, restored from that backup) with the same steps; do production only
   when the copy passes step 6.
3. **Before:** `sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/check-upgrades.sql` says which
   step to start from. For step 10 also save the output of `db/check-upgrade-10.sql` (section A).
4. **Run the missing steps in order** (each is safe to run again; each needs `-I`, and the ones with
   Persian text `-f 65001`):
   ```
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-10-org-nodes.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-11-node-api.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-12-templates.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-13-node-details.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-14-appointments.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-15-company-profile.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-17-stand-in-period.sql
   sqlcmd -S <db-host> -d TMPB -I -f 65001 -i db/upgrade-18-calendar-meetings.sql
   ```
   With `Database:InitializeOrgStructureSchema` true (the default) the API makes the same changes at
   start-up; run the scripts when a DBA owns the schema.
5. **After:** `db/check-upgrades.sql` must say all steps are applied, and `db/check-upgrade-10.sql`
   section A must be identical to the one saved before (row counts and checksums), section B all zero.
6. **Deploy the new NexusCore packages** (section 9) and start the API. Open the chart: it must look as
   it did (flat companies, same heads, same managers). Then, in the UI, set a company's display level
   to «پیشرفته» only where the new screens are wanted; nothing changes for the others.
7. **Rules and templates** come after, as the organization decides: the «قواعد ساختار» tab (types and
   what may sit under what), templates. Nothing nests until rules exist.

**Undo**, newest first, only if needed (data made with the new features in the dropped columns and
tables is lost): `db/upgrade-17-rollback.sql`, `db/upgrade-15-rollback.sql`, `db/upgrade-14-rollback.sql`, `db/upgrade-13-rollback.sql`, `db/upgrade-12-rollback.sql`,
`db/upgrade-11-rollback.sql`, `db/upgrade-10-rollback.sql`. The table `organization.OrganizationUnits`
keeps its name (the host checks it at start-up).

## 7. Tasks are given to posts

`OrgStructure:AssignTasksToPosts` is `true` in `appsettings.json`: a task's creator, responsible
people and access list are posts, and their holders are the task's people. When someone leaves a
post or a new person takes it, the post's tasks go with it. A vacant post can't be given a new
task, and someone with no post can't be given one. When the API starts it gives tasks that have
no posts yet to their people's main posts (people with no post stay on them as people). Set it
to `false` in `appsettings.Production.json` to give tasks to people again.

When a post falls vacant, its open tasks show in «کارتابل جانشینی» to the managers above the post
and to holders of the permission «مدیریت کارتابل جانشینی». They may put another post of the
company in charge of it (سرپرستی): its holder sees those tasks and comments on them as a stand-in
(«جانشین ...»), nothing more. When someone takes the vacant post the stand-in ends by itself.

Disabling a user's account takes them out of their posts: the posts fall vacant and their tasks
go to «کارتابل جانشینی». Their deputy and set manager stay; enabled again, they need to be put
back in a post.

## 7.1 Giving tasks only to one's own level and below

`OrgStructure:LimitTaskAssignment` is `true` in `appsettings.json`: in the org chart, a user makes
responsible for a task only themselves, the people below them, the people of their own deputy at
their level or lower, and the people of the company at their own level (an expert to an expert of
another deputy; not someone in a higher position of their own deputy) - never anyone above. A
person's level is that of their position: how many superior positions («سمت مافوق», set in «سمت‌ها»)
are above it up to «رئیس کل» - the highest position they hold; someone with no position is measured
by how many managers are above them in the chart. People with no
place in the chart yet are not limited (and anyone may give them tasks); the permission «ارجاع
فعالیت به همه‌ی سطوح سازمانی» lifts the limit for its holders (the Administrator role has it).
Set it to `false` in `appsettings.Production.json` to let anyone give tasks to anyone.

## 7.2 Posts under posts

A post can sit under another post of the same deputy (or, for posts directly in the company,
another of those), at any depth: e.g. «کارشناس» under «معاون». The holder of the post above is
then the manager of the post's holder: they see their tasks in reports, may give them tasks with
the limit above on, and see the post in «کارتابل جانشینی» when it falls vacant - but get no
rights over the chart. A vacant post above is passed over to the next one up (or the deputy's
manager). Set in the chart's post panel («پست مافوق»); a post with posts under it can't be removed.

## 7.3 Each company on its own

`OrgStructure:LimitToOwnCompany` is `true` in `appsettings.json`: everyone is kept to their own
company - the companies they are placed in or head. Others' people are not in their user lists,
chat, work teams or reports; they cannot be given a task, access to one or a message; the org chart
and its panels show and change only their own company; «مدیریت همهٔ وظایف» shows only the tasks
of their own people. Within the company, anyone may message anyone (a manager above too), and
tasks follow 7.1 (an expert may give one to an expert of another deputy, never to someone above).
Someone with no place in any company reaches nobody but themselves; people with no place yet stay
in the user list, so they can be placed. The permission «دسترسی به همه‌ی شرکت‌ها» lifts the limit
for its holders (the Administrator role has it). Set it to `false` in `appsettings.Production.json`
to open the companies to one another.

## 7.4 The switches at a glance

Each one is on its own: turn off what is not wanted, the rest keeps working.

| Setting (`OrgStructure:`) | On in this API | What it does | Lifted for holders of |
| --- | --- | --- | --- |
| `LimitTaskAssignment` | yes | tasks only to one's own level and below (7.1) | «ارجاع فعالیت به همه‌ی سطوح سازمانی» |
| `AssignTasksToPosts` | yes | tasks given to posts, «کارتابل جانشینی» (7) | «مدیریت کارتابل جانشینی» (every vacant post) |
| `LimitToOwnCompany` | yes | everyone kept to their own company (7.3) | «دسترسی به همه‌ی شرکت‌ها» |
| `LimitChatToHierarchy` | yes | direct chat only with one's peers (same manager, same unit), those below, one's manager and the manager's manager; people with no place are not limited | - |

Work teams are always kept to one's own deputy with the org structure; «افزودن اعضای سایر
معاونت‌ها به تیم کاری» lifts that (still within one's own company when 7.3 is on).

## 8. Reminders and recurring tasks

`TaskManagement:Scheduler:Enabled` is `true` in `appsettings.json`: recurring tasks, due-soon /
overdue reminders and SMS held back during the quiet hours are sent by this API. Set it to
`false` in `appsettings.Production.json` only on a second server of the same database that should
not send them (one is enough; two would not send anything twice).

## 9. Taking a newer NexusCore

The NexusCore modules come as packages from `./packages` (see `nuget.config`), built in the Core
repository with `eng/pack-core-modules.ps1`. To update:

1. Copy the new `.nupkg` files of the packages this API uses into `./packages`.
2. Set `<NexusCoreVersion>` in `TMPB-API.csproj` to the new version.
3. Build and deploy. The schema changes of Task Management, Notifications and Chat are applied by
   the modules at start-up; those of the organization modules as described in section 6.

---

## 10. Security settings

Since NexusCore `0.1.0.1405072818-mahdi`. All have safe defaults; at a real install set these:

| Setting | Where | What |
|---|---|---|
| `Jwt__SigningKey` | environment variable (IIS: `web.config` `<environmentVariables>`) | the access-token key (section 2). Outside Development the API refuses to start without a real one (32+ characters). |
| `Identity__AdminPassword` | environment variable | the first administrator's password on a new database (12+ characters, not `Admin@12345`). Not needed afterwards. |
| `NexusCore:ForwardedHeaders:KnownProxies` / `KnownNetworks` | `appsettings.Production.json` | the reverse proxy / load balancer in front of the API (IP addresses, or CIDR networks such as `10.0.0.0/24`). Only from these is the real client address taken from `X-Forwarded-For` - it is what rate limits, sign-in protection and the audit log count by. Without a proxy (IIS serves the API itself) leave both empty. **With a proxy and nothing set here, every user shares the proxy's address, and the sign-in limit (10 per minute per address) is shared by everyone.** |
| `DataProtection:KeysPath` | `appsettings.Production.json` | a folder (writable by the app pool, backed up) for the keys that encrypt the SMS panel's API key and the LDAP password. Empty: the keys stay in the app pool's profile and are lost with a new server or pool. After it is first set (or changed), enter the SMS API key and the LDAP password again once. |
| `Identity:Session:ReturnRefreshTokenInBody` / `AcceptRefreshTokenInBody` | `appsettings.Production.json` | `true` (as shipped): the refresh token also travels in the response body - works over plain http. Once the site is served over **HTTPS** (front end and API on the same site), set both to `false`: the refresh token then lives only in an HttpOnly, Secure, SameSite=Strict cookie no script can read. Over plain http that cookie is not kept, and users would be signed out every 15 minutes. |
| `NexusCore:RateLimiting` | `appsettings.Production.json` | per-user / per-address request limits (defaults: 600 requests a minute overall, 10 sign-ins a minute per address, 120 writes a minute). `Enabled: false` turns them off. |
| `Security:OutboundAddresses:AllowedHosts` | `appsettings.Production.json` | extra SMS gateway host names besides the provider's own (the API calls no other address). |

The API also sends HSTS over HTTPS, `Cache-Control: no-store` on `/api/identity`, limits request
sizes, and receives the browser's CSP violation reports at `/api/platform/csp-report`.

---

### What NOT to do
- Don't edit `appsettings.json` (the base file) for a specific deployment — it's meant to stay
  environment-independent so it's identical across every install. Environment-specific values go
  in `appsettings.Production.json` (this environment) or `appsettings.Development.json`
  (developers' machines only).
- Don't hardcode an IP/domain in `Program.cs` — CORS and hosts are already fully
  configuration-driven; there's nothing to touch there.
