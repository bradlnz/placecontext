# Security, privacy, and sharing

*Understand who can see information and when a link makes a file public.*

## Workspaces, projects, and permissions

Workspace data is separated by tenant, and project information stays with its project. A user's
role and permission overrides decide which pages and actions are available. The portal hides menu
items and buttons the user cannot use, while the server checks permission again when an action is
requested.

Read-only Viewers can browse permitted projects, jobs, data, and artifacts. Members can usually run
work and manage artifacts. Administrators and Owners have broader workspace controls. Custom roles
and individual overrides may differ, so ask an administrator if your access looks wrong.

## Information protected while stored

The project **Vault** encrypts secrets such as API credentials. Jobs receive a secret only while
they run; it should not be copied into job code, prompts, notes, or ordinary settings.

## Normal artifact access

Run artifacts normally require sign-in and the **View artifacts** permission. An artifact lookup is
limited to the current workspace. HTML and SVG previews are opened in a restricted browser sandbox
to prevent embedded scripts from taking over the portal.

## Public artifact links

A public share link is an intentional exception to normal sign-in. The random code inside the link
acts like a temporary password: anyone who has it can open that one artifact.

- Links expire after the selected 1, 7, or 30 days.
- Only a one-way fingerprint of the code is saved; the full code is shown once.
- Rotating a link makes the previous code stop working immediately.
- Revoking or deleting the artifact removes public access.
- Invalid, expired, or revoked links do not reveal whether the artifact exists.

Shared responses are marked private, are not intended for search indexing, and do not send the link
as a referrer to another site. Even with these protections, recipients can download or copy what
they can view. Share only information you are comfortable giving to every person who receives the
link.

## Access tokens

Personal API tokens, cluster join codes, and artifact share links serve
different purposes and cannot be used interchangeably. Keep every full token private. When one is
lost or exposed, rotate or revoke it from the page where it was created.

Backup exports deliberately exclude vault secrets and run history. Always protect downloaded
backups and job-code ZIP files according to your organisation's policies.

For trusted web-client registration, external sign-in, callback restrictions, and key-rotation
guidance, see [SSO and OAuth](/wiki/sso-and-oauth).
