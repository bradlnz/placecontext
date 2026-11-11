# Access, identity, and sharing

PlaceContext combines workspace and project boundaries with server-side permission checks. Configure identity, secrets, and deployment controls for the environment in which your data and workloads run.

## Workspace and project access

Roles and permission overrides determine available pages and actions. The portal hides unavailable actions, and the server checks permission when a request is made. Project data and tool requests remain scoped to the selected project and tenant.

Start users with the access they need, then grant additional permissions deliberately.

## Sign-in and external identity

Local account sign-in and configured email/SMS two-factor flows are implemented. PlaceContext can also use a compatible external authority through OAuth/PKCE, including an explicitly configured OpenID Connect mode with discovery and user-information endpoints.

External identity setup requires the correct callback, public origin, and provider configuration. Read the [SSO configuration reference](https://github.com/bradlnz/placecontext/blob/main/src/PlaceContext.Host/Wiki/sso-and-oauth.md) and verify the selected protocol against the deployment.

PlaceContext can separately act as an OAuth authorization server for trusted clients. Its identity profile does not issue an OpenID Connect ID token. These inbound and outbound identity roles are distinct.

## Secrets and model context

Store credentials in the project vault or deployment secret store. Model connection keys are encrypted and are not returned to the browser or model. Preserve the Data Protection key ring with database backups so encrypted records remain readable after recovery.

The model provider receives the conversation, relevant memory, and tool results used for the request. Consider that data flow when selecting a provider and assigning tools.

## Share only the intended result

Normal artifact access requires sign-in and the appropriate permission. A public artifact link is an explicit exception: anyone holding the link can view that artifact until the link expires or is revoked.

Completed-run demo links are also read-only snapshots. They can include an edited outcome and a recorded browser replay. Check the content and frames before sharing; revocation cannot remove copies a recipient already made.

## Prepare production operations

Use HTTPS, stable signing and encryption keys, managed secrets, backups, and tested restore procedures. Validate tenant, storage, network, and execution boundaries in the target environment before accepting untrusted workloads.

The self-hosted installer is an evaluation path. Managed signup is disabled by default and needs separate email, entitlement, and deployment configuration before launch.

[Deployment](/docs/deployment/) · [Repository security policy](https://github.com/bradlnz/placecontext/blob/main/SECURITY.md)
