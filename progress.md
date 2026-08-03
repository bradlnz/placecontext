# Progress — Generic Communications + 2FA + Access Overhaul

Session date: 2026-08-01. Plan file (approved): `~/.kimi-code/sessions/wd_devcontext_33786abf65a9/session_1c186bdd-74ae-4b05-ace5-e1d6c7901fc1/agents/main/plans/spectrum-siryn-jubilee.md`

## Status: Phases 1–3 DONE. Remaining: final verification, commit/push, deploy.

### Phase 1 — Generic communication providers ✅
- `communication_providers` table replaces `postmark_connections` (migration `20260801015834_AddCommunicationProviders`, incl. data migration of the old Postmark row). Fields: Channel (email/sms), Kind (postmark/sendgrid/twilio), Name, Enabled, IsDefault, UseForTwoFactor, AuthType (none/bearer/header/basic), AuthHeaderName, VaultProjectId + ApiKeySecretName (vault reference only), SettingsJson.
- `CommunicationProviderService` (CRUD, per-channel default + 2FA-flag exclusivity, vault-secret resolution), `DatabaseCommunicationSender` replaces `SendGridTwilioCommunicationSender` (appsettings email/SMS fallback removed).
- `CommunicationProvidersController` `/api/settings/communication-providers` (+ `POST …/{id}/test` test-send).
- `CommunicationsSettings.razor` fully rewritten: provider list per channel, set-default, 2FA toggle, auth-type-conditional form with vault pickers, per-kind fields, send-test.
- `IClientCommunicationSender` gained `SendAuthenticationSmsAsync`.

### Phase 2 — Mandatory multi-channel 2FA ✅
- `users` += `PhoneNumber`, `TwoFactorChannel` (migration `20260801022734_AddTwoFactorChannelAndPhone`); legacy `TwoFactorEnabled` no longer consulted.
- 2FA is now org-wide mandatory whenever any enabled provider is flagged `UseForTwoFactor`; codes route email→`SendAuthenticationEmailAsync`, sms→`SendAuthenticationSmsAsync`.
- Login flow: phone enrollment at first verify when SMS channel needs it; channel-switch link when both channels flagged; verify template generalized.
- `/api/2fa/*`: status/phone/channel endpoints (opt-in setup/disable endpoints removed); `SecuritySettings.razor` rewritten (status + phone editor + channel preference).

### Phase 3 — Access tab, default admin, editable roles, settings gating ✅
- `users.IsDefaultAdmin` + `role_definitions` table (migration `20260801025651_AddDefaultAdminAndRoleDefinitions`); earliest Owner flagged per tenant; system roles lazy-seeded from `RolePermissionDefaults`.
- `MembershipService.DeleteMemberAsync` (refuses default admin / Owner / self); role names are now plain strings end-to-end (custom roles assignable; coarse Member/Admin/Owner policies intentionally don't match custom roles).
- Access tab rewritten: default-admin badge, Remove w/ confirm, "Manage member" picker (scrolls + expands), Roles & permissions CRUD section.
- Settings gating: `Policies.DefaultAdmin` requirement+handler; all `/settings/*` pages except Security/ApiTokens now default-admin-only; SettingsLayout nav filtered; MenuConfigService settings entry requires default admin; `settings.manage` never effective for non-default-admins (resolver hardening).

### Test state
- Full suite green at last run: 861 passed / 0 failed across all projects (+ Host.Tests 98/98, not in .slnx — run separately).
- Known pre-existing flake: `S3ObjectStoreTests` presign timing (unrelated, passes standalone).

## Remaining (Phase 4–5)
1. Final verification pass: `dotnet build PlaceContext.slnx` + `dotnet test PlaceContext.slnx` + `dotnet test tests/PlaceContext.Host.Tests`.
2. Manual smoke test recommended — Blazor pages (Communications, Access, Security, login 2FA flow) were build-verified but never run in a browser.
3. Commit + push: **careful** — the working tree had PRE-EXISTING unrelated dirty files before this work (Chat.razor, Crm.razor, MainLayout.razor, several controllers, Program.cs, SectionAuthorizationTests.cs, terraform/tools go.mod…). Review `git status`/`git diff` and decide what goes in; feature files and pre-existing edits overlap in `AuthController.cs`, `Program.cs`, `SectionAuthorizationTests.cs`.
4. Run `./deploy.sh` (docker build/push + kubectl rollout restart on 100.81.205.22).

## Decisions locked in (user-approved)
- "Any provider" = configurable auth + endpoint over built-in payload shapes (postmark/sendgrid/twilio); new shapes are code additions.
- Default admin = first-run Owner, flagged; only it can access settings (Security + API tokens pages stay self-service for everyone).
- 2FA mandatory for all users once a provider is flagged; unflagging all providers disables 2FA globally.
- Roles are editable/DB-backed; per-user overrides still win (revoke > allow).
