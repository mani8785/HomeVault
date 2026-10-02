# ADR-0021: Protected operator invitations and account recovery

Status: Proposed
Created: 2026-10-02
Issue: [HV-22.2 / #70](https://github.com/mani8785/HomeVault/issues/70)

## Context

[ADR-0020](0020-local-accounts-vault-authorization.md) accepts invitation-only
accounts and operator-assisted recovery, but explicitly defers the exact protected
operator input/output workflow and recovery expiry to this slice. PR #73 delivered
the required Identity storage and Windows session-key foundation. This proposal
fills that remaining decision; it does not reopen the accepted account design.

## Proposed operator workflow

1. Use an explicit operator mode of the future HomeVault.Api executable, separate
   from its HTTP server mode. It runs locally as the Windows account that owns the
   database and session keys. Access to that OS account and its private data is the
   operator authority; Vault ownership grants no account administration powers.
2. Require the server to be stopped during operator mutations. Server and operator
   modes hold the same exclusive local lock for the configured database. Fail if
   the lock cannot be acquired; do not kill processes. Validate existing schema,
   private database-directory permissions and session keys before proceeding.
   Do not migrate or create keys implicitly.
3. Accept only non-secret configuration paths and an action on the command line.
   Prompt for the target login using non-echoing console input. Reject redirected
   input; do not accept identifiers, passwords or tokens from arguments or
   environment variables. The operator never chooses or retrieves user passwords.
4. Export the invitation or recovery credential into a newly created file in an
   explicitly provisioned private directory outside Git checkouts. Reject existing
   destinations, reparse points and unsafe permissions. Assign current-user-only
   ACLs and ownership before writing secret bytes. Print only a generic outcome,
   never the credential, login, file contents or exception details.
5. The exported file contains a version, credential purpose, login identifier,
   expiry and the credential. It is readable plaintext protected by Windows ACLs,
   not a portable encrypted envelope. The operator verifies the recipient and
   sends this file through an independently trusted encrypted channel. HomeVault
   does not send mail, choose a transfer service or write the clipboard. The
   operator removes the local export after delivery; deletion is not secure erasure.
   This confidentiality tradeoff needs explicit approval.
6. The recipient submits the credential and their own new password in an HTTPS
   POST body with antiforgery protection. No token-bearing links, query strings or
   automatic login after redemption/reset. The future Angular UI can import the
   file locally; this task supplies the API and automated HTTP tests, not that UI.

Use the same invitation process for the first account. There is no initial admin
password or public bootstrap endpoint. Enrollment grants no existing memberships.

## Credential lifetime and atomicity

Retain ADR-0020's 256-bit random invitation secret, hash-only database storage,
24-hour expiry, normalized-login binding and atomic one-use consumption with
account creation. Reissuing for a login revokes its older unconsumed invitations.

Propose a dedicated Identity password-reset token provider with a **30-minute**
lifetime. Also store a hash of the issued credential and its pending/consumed state
to enforce explicit one-use and replacement semantics. Validate both the Identity
token and that record; consume and reset the password in one SQLite transaction.
Concurrent redemption must produce at most one successful reset.

Issuing replacement recovery revokes earlier recovery credentials and rotates the
account security stamp immediately; it intentionally signs out existing sessions
after the operator has verified the recipient. Successful reset rotates the stamp
again and clears lockout. Recovery does not enable a disabled account. Operator
disable rotates the stamp and revokes outstanding recovery credentials. Export or
database failure must never report success; failed delivery can be replaced by
explicit reissue. Do not attempt to claim a filesystem/database atomic commit.

Following database restore, an offline operator command must rotate every account
stamp and revoke all pending invitations and recovery credentials before the host
is reopened. This is an explicit mandatory restore step; arbitrary externally
replaced database files cannot be reliably detected by the application. Preserve
existing Vault memberships and password hashes during this invalidation.

## HTTP boundary

Keep ADR-0020's eight-hour non-sliding secure cookie, per-request stamp/enabled
checks, password and lockout policy, antiforgery and generic login failures.
Add anonymous **recovery redemption** to the narrow allowlist, with the same
antiforgery, request-size and rate-limit protection as invitation redemption.
This supplements ADR-0020's allowlist: recovering users cannot be required to
already possess a working session. Recovery issuance stays offline only.

The initial server binds to loopback HTTPS with an explicit allowed host and no
credentialed cross-origin access. Public hosting remains a separate decision.
Do not log HTTP bodies, authentication headers, cookies, tokens or login identifiers.
No stock public registration or public forgot-password issuance endpoints.

## Alternatives and consequences

- Console/clipboard token output is simpler but risks transcripts and clipboard
  history; it is rejected by the accepted secret-handling requirements.
- DPAPI-encrypted export files cannot be redeemed by a recipient on another Windows
  account. Portable encryption would require another key-exchange workflow, which
  is deferred rather than borrowing the parked Asset encryption design.
- Email delivery adds an unselected provider and infrastructure; defer it.
- A web account-administration UI adds an authorization surface beyond this slice.

The local Windows account is trusted. ACLs do not protect against its malware or
machine administrators. Export files need careful handling and must not enter
source control, cloud-sync folders or ordinary backups. No new scripts are needed.

## Implementation and validation after acceptance

Implement within #70 in this order: protected operator file handling; invitation
and recovery schema/services; cookie API and antiforgery; integrated verification.
Do not close #70 for this proposal alone.

Tests must cover permission failures without secret output, no overwrite, failed
export/reissue, invitation/reset expiry and replay, concurrent redemption, disabled
accounts, restart, wrong credentials/lockout, cookie attributes, antiforgery on
every unsafe route, logout versus sign-out-all, restore invalidation and captured
diagnostics. Use real SQLite and HTTP cookies, plus Windows ACL/DPAPI integration.
Run restore, format, Release build, NUnit and both GitHub validation jobs.

## Confirmation

Awaiting explicit owner acceptance of the protected plaintext export workflow,
30-minute recovery lifetime, immediate sign-out on recovery issuance and offline
operator/restore workflow. No implementation is claimed by this document.

## References

- [Accepted account design](0020-local-accounts-vault-authorization.md)
- [Existing Identity/session-key implementation](../hv-22-identity-storage.md)
- [Identity token lifetime configuration](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.dataprotectiontokenprovideroptions?view=aspnetcore-10.0)
