# SSO and OAuth

*Connect trusted web platforms to PlaceContext identity, or use a compatible external authority to sign users into PlaceContext.*

## Choose the direction

There are two separate integrations:

| Goal | PlaceContext role | Configuration |
|---|---|---|
| Sign into another platform with a PlaceContext account | OAuth authorization server | `PlaceContext:OAuth:TrustedClients` |
| Sign into PlaceContext through another authority | OAuth/PKCE client | `PlaceContext:Sso` |

These flows can be enabled independently. Neither replaces project permissions: a signed-in user
still receives the PlaceContext role and project access assigned in the workspace.

## Prerequisites

Use a stable public HTTPS origin and configure it explicitly:

```text
PlaceContext__PublicBaseUrl=https://placecontext.io
```

Persist `PlaceContext:OAuth:SigningKeyPem` and `PlaceContext:DataProtection:Key`, and share the same
values across replicas. The reverse proxy must preserve the original host/protocol and allow normal
authorization redirects. Verify the advertised endpoints before registering another application:

```text
https://placecontext.io/.well-known/oauth-authorization-server
https://placecontext.io/.well-known/jwks.json
```

## Use PlaceContext sign-in in another platform

Register each external web application as a trusted client. The client ID and redirect URI are exact
matches; one trusted-client entry represents one callback URI.

```text
PlaceContext__OAuth__TrustedClients__Reports__ClientId=reports-portal
PlaceContext__OAuth__TrustedClients__Reports__RedirectUri=https://reports.example.com/auth/callback
PlaceContext__OAuth__TrustedClients__Reports__Name=Reports portal

PlaceContext__OAuth__TrustedClients__Crm__ClientId=crm-portal
PlaceContext__OAuth__TrustedClients__Crm__RedirectUri=https://crm.example.com/oauth/callback
PlaceContext__OAuth__TrustedClients__Crm__Name=CRM portal
```

The segment after `TrustedClients` is only a unique configuration key. Restart or roll PlaceContext,
then configure the external application with:

| Item | Value |
|---|---|
| Authorization server metadata | `https://placecontext.io/.well-known/oauth-authorization-server` |
| Authorization endpoint | `https://placecontext.io/connect/authorize` |
| Token endpoint | `https://placecontext.io/connect/token` |
| User information endpoint | `https://placecontext.io/connect/userinfo` |
| JWKS | `https://placecontext.io/.well-known/jwks.json` |
| Client authentication | Public client; no client secret |
| Flow | Authorization code with PKCE S256 |
| Scope | `identity` |

The external platform must generate a verifier/challenge, preserve `state`, exchange the code with
the same verifier, and call `/connect/userinfo` with the returned bearer token. User information
contains `sub`, `email`, `name`, and `role`. Access tokens last one hour; use the returned rotating
refresh token rather than sending the user through sign-in for every session.

This is an OAuth identity profile, not a full OpenID Connect implementation: no ID token is issued.
Use the user-information endpoint as the identity source. The `identity` scope is accepted only for
an exact trusted-client match; dynamically registered MCP clients cannot request it.

## Sign users into PlaceContext through another authority

Register PlaceContext as a public PKCE client at the external authority. Use this callback exactly:

```text
https://placecontext.io/auth/sso/callback
```

Then configure:

```text
PlaceContext__Sso__Authority=https://identity.example.com
PlaceContext__Sso__ClientId=placecontext
PlaceContext__Sso__CallbackUrl=https://placecontext.io/auth/sso/callback
```

Start the flow at `/auth/sso` (optionally with a local `returnUrl`). The authority must support a
public authorization-code client with PKCE S256 and the `identity` scope, using these authority-
relative endpoints:

```text
/oauth/authorize
/oauth/token
/oauth/userinfo
```

The user-information response must be JSON with non-empty `sub` and `email` values and may include
`name`. Standard providers that use different endpoint paths, require a client secret, or only
return OpenID Connect ID tokens need a compatible gateway/adapter; the current integration does not
perform generic OIDC discovery.

On first successful sign-in, PlaceContext provisions the external identity as a **Viewer**. An
administrator can then assign the appropriate workspace role and project access. Later sign-ins do
not downgrade an existing member's role.

## MCP OAuth is already automatic

MCP clients use the same authorization server with the `mcp` scope. Compatible clients discover the
metadata from the workspace `/mcp` resource, dynamically register a safe loopback callback, open the
browser for user authorization, and rotate refresh tokens. Do not add MCP clients to
`TrustedClients`; that list is for reviewed remote web callbacks and the restricted `identity`
scope.

## Security checklist

- Require HTTPS and exact callback matching; never use wildcard redirect URIs.
- Give every platform a distinct client ID and configuration entry.
- Keep signing and Data Protection keys outside source control and stable across replicas.
- Restrict proxy access to administrative endpoints and keep host/protocol forwarding correct.
- Remove a trusted-client entry when retiring an integration, then revoke its active sessions in the
  external platform.
- Test with a low-privilege member before enabling the integration for administrators.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `Unknown client or redirect_uri` | Client ID or callback does not exactly match a trusted entry |
| `Only response_type=code with S256 PKCE is supported` | The client omitted PKCE or requested another flow |
| `The identity scope is restricted` | A remote web client was dynamically registered instead of trusted |
| Redirects use HTTP or the wrong host | `PublicBaseUrl` or reverse-proxy forwarding is incorrect |
| External sign-in returns 503 | One of `Sso:Authority`, `ClientId`, or HTTPS `CallbackUrl` is missing/invalid |
| External sign-in returns invalid identity | The authority's user-information JSON lacks `sub` or `email` |
