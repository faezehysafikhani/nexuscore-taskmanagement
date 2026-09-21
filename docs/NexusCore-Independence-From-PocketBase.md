# Running the task manager on NexusCore alone

This release lets the task-manager frontend run on NexusCore with no PocketBase at all:
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
| `POST /api/identity/auth/login` | now accepts email **or username or mobile number** in `email` |
| `POST /api/identity/auth/register` | self-registration (only when `Identity:SelfRegistration:Enabled`) |
| `POST /api/identity/auth/forgot-password` | emails a reset link; same answer whether or not the account exists |
| `POST /api/identity/auth/reset-password` | sets the new password, signs the user out everywhere |
| `PUT /api/identity/auth/me/profile` | own name, username, avatar, mobile, Telegram id, notification choices |
| `PUT /api/identity/auth/me/preferences` | own theme, colour palette, light/dark |
| `POST/PUT /api/identity/users` | now also take username, mobile, Telegram id, notification choices; `PUT` can change email and password |
| `GET /api/identity/users` | defaults to the caller's tenant; contact details only for `users.update` holders |

`identity.Users` gained: `Username` (unique per tenant when set), `PhoneNumber`,
`TelegramChatId`, `NotifySms`, `NotifyTelegram`, `AvatarUrl`, `Theme`, `ColorPalette`,
`ThemeMode`. Password reset uses the existing `identity.PasswordResetTokens` table and
`IPasswordResetLinkSender` contract, now implemented by `EmailPasswordResetLinkSender`.

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
  refused, never faked), `ITelegramSender`, `IEmailSender` (SMTP or pickup directory).
* Gateway settings per tenant in `platform.Settings` (`Notifications.Channels`); the API key and
  bot token are encrypted with ASP.NET Core Data Protection.
* `GET/PUT /api/platform/notification-channels` (`settings.view` / `settings.update`),
  `POST .../test-sms`, `POST .../test-telegram` (`settings.update`).
* The HTTP clients used for the gateways do not log request URLs (they contain the secrets).

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
* Task creation sends SMS/Telegram from the server (`Nexus.Integrations.TaskNotifications`),
  honouring each user's choices; the module's SMS and contact seams are now real.

## Configuration (Rozet.Api)

```jsonc
"Identity": {
  "SelfRegistration": { "Enabled": true, "TenantSlug": "", "DefaultRoleName": "Member" },
  "SeedRoles": [ { "Name": "Member", "Permissions": [ "Tasks.View", "...", "groups.manage_own" ] } ]
},
"PasswordReset": { "TokenLifetimeMinutes": 30, "ResetUrlTemplate": "http://localhost:3030/reset-password" },
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

Both are idempotent. Verified: a database created by the previous version, upgraded with the
script (run twice), has exactly the schema a fresh database gets from this version.

EF Core migrations are included for deployments that use them: `SyncModelWithRuntimeSchema`
(brings the stale Core snapshot in line with what the runtime already creates) and
`AddUserProfileAndPersonalTeams` (NexusCore), `AddDirectMessagingAndAttachments` (Chat),
`AddCommentFilesAndGeneratedOccurrences` (TaskManagement).
