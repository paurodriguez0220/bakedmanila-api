# Azure Manual Steps Runbook

**Status:** Living document — update whenever a new manual, non-CI step is discovered.

## Why this exists

The GitHub Actions deploy identity is intentionally scoped to Contributor on just the
`bkdmnl` resource groups (least privilege), and this tenant blocks it from creating
role assignments or performing subscription-scoped actions. A handful of Azure setup
steps can only be performed by a human with broader rights (subscription Owner). This
runbook is the checklist so none of them get forgotten — every step below is a
**runnable script**, not a manual copy-paste command list.

## One-time, per environment (dev / prod / dr)

- [x] **prod** — resource group, deploy identity Contributor grant, and resource
      provider registration:

      ./scripts/azure-provision-prerequisites.ps1 -Environment prod -DeployAppClientId <deploy-app-client-id>

- [ ] **dev** / **dr** — run the same script with `-Environment dev` / `-Environment dr`
      before the first deploy to that environment.

- [x] **prod** — after the first deploy with `budgetAmount > 0` created the cost
      auto-stop Logic App, grant it permission to actually stop the app:

      ./scripts/azure-grant-cost-auto-stop-rbac.ps1 -Environment prod

  (done 2026-07-06 — required because `Microsoft.Logic` was not yet registered on
  first attempt; re-run `azure-provision-prerequisites.ps1` first if this fails with
  a resource-not-found error, then retry this script.)

## One-time, for the whole project

- [x] Create the Entra app + federated credential for GitHub Actions OIDC
      (`repo:paurodriguez0220/bakedmanila-api:environment:prod`) — this identity's
      client ID is the `-DeployAppClientId` passed to the prerequisites script above.

## Ad hoc, as needed

- **Rotate the admin login password** (e.g. the current one contains `!`, which
  breaks bash history expansion when copy-pasted) — generates a new alphanumeric-only
  password, writes it to Key Vault, and restarts the app so `DevSeeder.SeedAdminAsync`
  picks it up on the next startup (it compares the configured password against the
  stored one on every boot and resets on mismatch — this rotation path already
  existed in code, it just needed a script to drive all three steps in one command):

      ./scripts/azure-rotate-admin-password.ps1 -Environment prod

  Corrected 2026-07-07: this runbook previously listed admin password rotation as a
  "known gap" (seeder only creates, never resets) — that was stale. The reset-on-
  mismatch logic is already in `DevSeeder.SeedAdminAsync`; the actual gap was the
  lack of a script tying secret-update + restart together, which this closes.

## Known gaps / not yet built

- [ ] **ACS Email domain verification** — deferred, interactive, not yet completed.
      Order emails are currently log-only (`LoggingNotificationSender` fallback).

## Related

- `README.md` → "Infrastructure" section — full deploy runbook, cost breakdown, and
  every documented Bicep-vs-standard deviation and why.
- `infra/main.bicep`, `infra/modules/*` — source of truth for what gets deployed.
- `scripts/azure-provision-prerequisites.ps1`, `scripts/azure-grant-cost-auto-stop-rbac.ps1`,
  `scripts/azure-get-secret.ps1`, `scripts/azure-rotate-admin-password.ps1`
  — the runnable scripts this checklist points at.

---
*Maintained by paurodriguez0220 · Last updated: 2026-07-07*
*Standards: https://github.com/paurodriguez0220/standards-docs*
