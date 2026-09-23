# Running the task manager on NexusCore alone

This release lets the task-manager frontend run entirely on NexusCore:
accounts, profiles, teams, chat, notification gateways, tasks, files and live updates are all
served by NexusCore.

## What changed in the backend

### Permissions: `groups.*` no longer fails with 500

Every `/api/identity/groups` request failed with
`AuthorizationPolicy named 'groups.view' was not found`. The group permissions were catalogued
(and seeded, so they appeared in tokens) but never registered as authorization policies.

* `AddUserGroupFeature` now registers a policy for every `UserGroupPermissions` entry, next to
  the catalog - the same pattern every module uses. This covers all hosts (NexusCore.Api,
  Rozet.Api, PostBank.Api, PostbankPM), because they all call `AddInfrastructure`.
* `PermissionPolicyProvider` (Core) backs every host: a policy name nobody registered resolves
  to the standard permission policy instead of `null`. A user without the permission gets 403;
  the missing registration is logged. Access is never widened.
* `Error.Forbidden` (403) was added to the shared kernel and mapped in `EndpointResults`.
  "Not allowed" answers that used to be 401 (which makes a client drop its session) are now 403.
* New permissions: `groups.delete`, `groups.manage_own`.

### Identity

| Endpoint | Purpose |
|---|---|
| `POST /api/identity/auth/login` | `identifier` is a **username or mobile number** (email is not a sign-in name); CAPTCHA fields once required - see below |
| `POST /api/identity/auth/captcha` | a single-use CAPTCHA image for the next sign-in attempt of this client |
| `POST /api/identity/auth/register` | self-registration (only when `Identity:SelfRegistration:Enabled`); username and mobile required, email optional |
| `POST /api/identity/auth/forgot-password` | `identifier` = username or mobile; emails a reset link to the account's email, if it has one; same answer whether or not the account exists |
| `POST /api/identity/auth/reset-password` | sets the new password, signs the user out everywhere |
| `PUT /api/identity/auth/me/profile` | own name, username (required), avatar, mobile, SMS choice |
| `PUT /api/identity/auth/me/preferences` | own theme, colour palette, light/dark |
| `POST/PUT /api/identity/users` | username required on create; mobile and email optional; `PUT` can change email and password |
| `GET /api/identity/users` | defaults to the caller's tenant; mobile numbers only for `users.update` holders |

`identity.Users` has: `Username` and `PhoneNumber` (the sign-in names, each unique per tenant),
`Email` (optional contact address, unique when set), `NotifySms`, `AvatarUrl`, `Theme`,
`ColorPalette`, `ThemeMode`. Password reset uses the existing `identity.PasswordResetTokens`
table and `IPasswordResetLinkSender` contract, implemented by `EmailPasswordResetLinkSender`.

### Sign-in: username or mobile number, CAPTCHA after a failed attempt

* The identifier is resolved by `FindUserByLoginAsync`: a valid mobile number (in any common
  spelling - `0912 123 4567`, `+98 912...`, `00989...`, Persian digits) is compared in its canonical
  form (`NexusCore.Domain.Identity.PhoneNumber`: `09xxxxxxxxx`, or `+<digits>` for other
  countries); otherwise a valid username (starts with a letter); anything else - an email
  address included - matches nothing. Every failure answers the same
  `Invalid username/mobile number or password.`
* `ILoginProtection` (Core, on `IDistributedCache`): after a failed attempt - counted per
  identifier and per client address - the next attempt needs a CAPTCHA (`captcha.required`,
  400). While one is due the password is not checked at all. A CAPTCHA is a server-rendered PNG
  (digits drawn as distorted strokes over noise; the answer is never in any response), bound to
  the client that requested it and consumed by the first attempt to answer it (`captcha.invalid`
  when wrong, expired, replayed or someone else's). A wrong password that makes the next attempt
  need one answers `unauthorized.captcha_required` (401). Success clears the state. After
  `MaxFailedAttemptsPerIdentifier` failures an identifier is refused (`too_many_requests`, 429)
  until the window ends; login, CAPTCHA, registration and password-reset requests are also
  limited per client. Settings: `Identity:LoginProtection`.
* The seeded built-in administrator signs in as `Identity:AdminUsername` (default `admin`).

### Personal work teams

`identity.UserGroups.OwnerUserId`: null for organisational groups (admin-managed, may carry
permissions), set for a user's own work team (managed by its owner, **never** a source of
permissions - enforced in the domain, the service and the permission provider).

| Endpoint | Permission |
|---|---|
| `GET/POST /api/identity/groups/mine`, `PUT/DELETE /api/identity/groups/mine/{id}`, `PUT .../mine/{id}/members` | `groups.manage_own` |
| `DELETE /api/identity/groups/{id}` | `groups.delete` |

Deleting a team that tasks are still assigned to answers 409.

### Messaging (Core)

* `ISmsSender` (Kavenegar - the only gateway the frontend really implemented; other providers are
  refused, never faked), `IEmailSender` (SMTP or pickup directory). Telegram was removed
  completely (sender, settings, endpoint, user fields).
* SMS gateway settings per tenant in `platform.Settings` (`Notifications.Channels`); the API key
  is encrypted with ASP.NET Core Data Protection.
* `GET/PUT /api/platform/notification-channels` (`settings.view` / `settings.update`),
  `POST .../test-sms` (`settings.update`).
* The HTTP client used for the gateway does not log request URLs (they contain the API key).

### Chat

* Sender always taken from the token (the SignalR broadcast used the client-supplied id).
* Every endpoint answers with the platform's normal results (failures used to be 200).
* One conversation per pair of users (`Conversations.DirectKey`).
* New: `GET /api/chat/direct/unread-counts`, `GET/POST /api/chat/direct/{userId}/messages`
  (multipart: `text`, optional `file` up to `Chat:MaxAttachmentBytes`), `POST .../{userId}/read`,
  `PUT/DELETE /api/chat/messages/{id}` (own messages only),
  `GET /api/chat/messages/{id}/attachment` (participants only).
* Times are returned as UTC.
* Rozet.Api, NexusCore.Api and PostBank.Api now create the chat and notification schemas at
  start-up (only PostbankPM did).

### TaskManagement

* File uploads now store their content through `IFileStorage` (previously only the metadata was
  saved and every download answered 404); deleting a file, subtask, comment or task removes the
  content too.
* Comment attachments: `POST /api/task-management/files/comments/{commentId}` (author only);
  comments return their files.
* `POST /api/task-management/tasks/{id}/activity` - history entries written by the UI, recorded
  under the caller.
* `SubTasks.IsGeneratedOccurrence` - lets the UI replace its generated recurrence occurrences on
  save instead of duplicating them.
* The module's validators now actually run (`RequestValidationFilter`); invalid input is 400.
* Live updates: SignalR hub `/hubs/task-management`, event `TasksChanged`, sent to the caller's
  tenant after every successful write.
* Task creation sends SMS from the server (`Nexus.Integrations.TaskNotifications`), honouring
  each user's choice; the module's SMS and contact seams are now real.

## Configuration (Rozet.Api)

```jsonc
"Identity": {
  "SelfRegistration": { "Enabled": true, "TenantSlug": "", "DefaultRoleName": "Member" },
  "SeedRoles": [ { "Name": "Member", "Permissions": [ "Tasks.View", "...", "groups.manage_own" ] } ]
},
"PasswordReset": { "CodeLength": 6, "CodeLifetimeMinutes": 5, "ResetTokenLifetimeMinutes": 10 },
"Email": { "Smtp": { "Host": "", "Port": 587, "EnableSsl": true, "UserName": "", "Password": "", "FromAddress": "" } },
"Chat": { "MaxAttachmentBytes": 5242880 }
```

Supply the SMTP password through user-secrets or environment variables. Without `Email:Smtp`,
password reset answers that email is not configured.

## Upgrading an existing database

Hosts create schemas with `EnsureCreated`, which never adds a column to an existing table. Apply
these once, after a backup:

* `docs/upgrade/2026-09-21-upgrade-existing-database.sql` - the DefaultConnection database.
* `docs/upgrade/2026-09-21-upgrade-existing-chat-database.sql` - only if the chat database
  already exists.
* `docs/upgrade/2026-09-22-add-task-and-subtask-times.sql` - due times of tasks and subtasks.
* `docs/upgrade/2026-09-22-signin-by-username-or-phone.sql` - canonical, unique mobile numbers,
  optional email, Telegram columns and settings removed. Stops without changing anything if two
  users of a tenant share a mobile number.

Both are idempotent. Verified: a database created by the previous version, upgraded with the
script (run twice), has exactly the schema a fresh database gets from this version.

EF Core migrations are included for deployments that use them: `SyncModelWithRuntimeSchema`
(brings the stale Core snapshot in line with what the runtime already creates) and
`AddUserProfileAndPersonalTeams`, `SignInByUsernameOrPhoneAndRemoveTelegram` (NexusCore),
`AddDirectMessagingAndAttachments` (Chat), `AddCommentFilesAndGeneratedOccurrences`,
`AddTaskAndSubTaskTimes`, `RemoveTelegramFromUserModel` (TaskManagement; snapshot only).
