# ADR-0020: Invitation-only local accounts and Vault authorization

Status: Proposed
Created: 2026-10-01
Issue: [HV-22 / #26](https://github.com/mani8785/HomeVault/issues/26)

## Confirmed direction and current coverage

The owner selected local HomeVault accounts, invitation-only initially, and
prioritized HV-22 before the accepted Angular/API UI in
[ADR-0019](0019-browser-ui-first-journey.md). Encryption implementation remains
parked under [ADR-0018](0018-sensitive-value-encryption.md).

Today ICurrentActor is a contract, not authentication. Playground supplies fictional
actors. Application exposes CreateVault, RegisterAsset and InspectAsset only.
SQLite checks current membership for Asset reads and serializes registration with
role/archive checks. Attributes, Evidence, Relationships, Reminders and membership
mutations have Domain behavior but no complete durable application/API operations.
Securing the three existing use cases alone cannot close the full HV-22 story.

## Recommended identity and session design

Use ASP.NET Core Identity and its maintained password hashing, validation,
lockout, security-stamp and token facilities. Do not invent a password hash or
issue a custom JWT. Keep Identity entities/stores in Infrastructure and cookie/
HTTP integration in HomeVault.Api. Domain and Application remain independent of
Identity, ASP.NET and EF. Map a server-generated Identity Guid to the same actor
Guid used by memberships; email is a mutable login label, never an ownership key.

Propose extending the existing Infrastructure context with Identity tables and
reviewed migrations in the same SQLite database. Preserve existing table mappings
and test upgrades and backup history checks. Do not retrofit account ownership
onto fictional actor IDs. Existing demonstration databases stay fictional; a
fresh account-enabled database is the first supported user setup. Any import of
existing real ownership needs a separate explicit mapping workflow.

Use same-origin HTTPS with an HttpOnly, Secure, SameSite=Lax authentication cookie,
host-only scope and Path=/, plus server-validated antiforgery tokens on all unsafe
requests, including login, invitation redemption and logout. Coordinate the
request-token cookie/header with Angular; its readable antiforgery token is not
the authentication cookie. Refresh antiforgery state after identity changes.
Use API 401/403 responses, not HTML redirects. Do not put session tokens in local
storage. Configure explicit allowed hosts/origins; no wildcard credentialed CORS.

Propose an eight-hour absolute session lifetime, no persistent remember-me and no
sliding extension initially. Validate account enabled state and security stamp on
every authenticated request; unavailable identity storage fails closed. Password
reset and operator disable update the stamp, invalidating subsequent requests.
Logout clears the browser cookie; it does not revoke a stolen copy by itself.
Provide a separate sign-out-all action through stamp invalidation. Already running
requests may finish; do not claim retroactive revocation of completed/in-flight work.

Persist ASP.NET Data Protection keys for cookie/token protection outside checkout,
with restricted access and explicit at-rest protection for the initial Windows
host. Missing configured keys must fail startup rather than silently reset them.
This session-key dependency is required by HV-22 and is separate from the parked
Sensitive-value encryption/key-recovery implementation. Restoring old identity
state can restore credentials/stamps, so recovery must invalidate all sessions and
outstanding invitations before reopening the host. Document key loss as session/
token invalidation requiring login, not loss of Asset data.

## Invitation-only enrollment and account recovery

Expose only reviewed Identity-backed endpoints. Do not map the stock public
registration endpoints wholesale: anonymous account creation must be impossible.
Initially only the trusted local operator can provision invitations and initiate
account recovery, using an explicit application command with protected input/output.
There is no public first-user-wins endpoint, default password, or account-admin role
implicitly granted by Vault ownership. No email service is selected in this slice.

Propose invitations tied to a normalized login identifier, using a random 256-bit
secret, stored only as a hash, expiring after 24 hours and consumed once. Redeem
through a POST body, never a URL query; atomically consume the invitation and create
the account. Concurrent redemption yields at most one account. Restrict access to
invitation export files and operator commands; never use secret CLI arguments,
environment variables, logs or ordinary console output. Specify the exact secure
operator workflow in the provisioning slice before implementation.

The operator verifies the recipient and transfers the invitation through a trusted
channel. This does not claim automated email-address verification. Enrollment
creates an account only, granting no existing Vault membership. An authenticated
account may create a Vault and become its Owner; granting existing Vault access is
a separate authorized membership operation. Invitees choose their own password.

Use Identity password validators with a proposed minimum of 15 characters, allow
spaces and password-manager paste, avoid mandatory character-class composition,
and bound input size explicitly. Review supported hashing settings at implementation.
Propose lockout after five failed attempts for 15 minutes, with endpoint rate limits
and generic login failures for unknown, disabled, locked and incorrect accounts.
Test enumeration behavior without claiming perfect timing indistinguishability.

No public forgot-password flow or email sender is selected. Operator-assisted
recovery requires recipient verification and a short-lived, single-use Identity
reset token delivered through the protected channel, invalidating active sessions
on reset. Never disclose or retrieve old passwords. Finalize expiry and replay
tests before exposing recovery. MFA and external providers remain separate scope;
initial deployment remains local until a hosting/security review is completed.

## Vault operation matrix

Preserve [ADR-0004](0004-domain-language-and-boundaries.md):

| Operation on an Active Vault | Owner | Administrator | Editor | Viewer |
| --- | --- | --- | --- | --- |
| Read ordinary records | Yes | Yes | Yes | Yes |
| Write Assets, ordinary attributes, Evidence, Relationships, Reminders | Yes | Yes | Yes | No |
| Add/remove Editor or Viewer, change between those roles | Yes | Yes | No | No |
| Grant/revoke Owner or Administrator | Yes, preserve last Owner | No | No | No |
| Change Vault metadata or archive | Yes | No | No | No |

Archived Vaults permit current-member reads and prohibit record/membership writes.
Nonmembers and missing resources share one unavailable outcome. Vault roles are
queried from current storage, never trusted from cookies or global Identity roles.
Account administration does not grant access to Vault contents. Cross-Vault
Relationships are rejected even when the actor can access both Vaults.

Sensitive reads were not authorized by ordinary read permission in ADR-0004.
Keep Sensitive read/write APIs unexposed while their explicit policy and encryption
integration are pending. Ordinary attribute support must reject Sensitive input
rather than silently downgrade it. This remains an explicit outstanding HV-22
acceptance area, not a claim that encryption or authentication alone resolves it.

Require authenticated routes by default with a small reviewed anonymous allowlist
for antiforgery bootstrap, login and invitation redemption. Map ICurrentActor only
from validated server identity; ignore caller-supplied actor fields. Every use case
must still check trusted identity, ownership and role. For writes, read current
membership/lifecycle and commit within the same SQLite write transaction as under
ADR-0017. Recheck authorization on every read; no cached view is a permission grant.

## Implementation slices after acceptance

1. Identity storage, explicit migrations and protected session-key configuration.
   Test fresh schema, upgrades preserving Vault/Asset data, backup compatibility,
   missing keys, forbidden dependencies and safe failures.
2. Secure local operator bootstrap, invitations, recovery and cookie API. Test
   replay/expiry/concurrency, no public signup, login/logout, lockout/rate limits,
   antiforgery, cookie attributes, stamp invalidation and no credential logging.
3. Bind authenticated actors to existing Vault/Asset endpoints. Test two separate
   accounts, all four roles, missing/inaccessible equivalence, archived writes,
   forged actor fields and membership changes between requests/around commits.
   This is the first checkpoint that can unblock the accepted three-operation UI.
4. Refine and implement missing purpose-specific application operations for Vault
   membership/archive, ordinary attributes, Evidence, Relationships and Reminders,
   including necessary reviewed storage schemas. Cover the matrix at each entry
   point, same-Vault references, last-Owner concurrency and archive serialization.
   Sensitive operations remain blocked until their policy/encryption dependency
   is resolved. Keep the parent open while any required operation is outstanding.

Create linked task issues after architectural acceptance. Each implementation PR
needs NUnit integration tests with real SQLite and HTTP cookies, safe fictional
fixtures, restore/format/Release build, and passing CI. Add Windows checks for
actual OS key protection. Keep host DI in composition, no new helper scripts
without separate approval, and no authentication bypass in the normal API host.

## Acceptance needed

Local invitation-only accounts are confirmed. Identity with secure cookie sessions,
operator-only invitation/recovery, proposed session/password/lockout limits,
Identity schema placement and phased operation coverage remain Proposed. Confirm
these details before adding dependencies or authentication code. This ADR neither
implements authentication nor authorizes public hosting or secret storage.

## References

- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [Identity for SPA backends and cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization)
- [Antiforgery protection](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery)
- [Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest)
