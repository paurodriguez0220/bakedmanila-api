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

## Known gaps / not yet built

- [ ] **Admin password rotation** — no rotation flow exists. The seeder only creates
      the admin account; it never resets it. Rotating the `AdminPassword` Key Vault
      secret alone will **not** change the actual login password.
- [ ] **ACS Email domain verification** — deferred, interactive, not yet completed.
      Order emails are currently log-only (`LoggingNotificationSender` fallback).

## Related

- `README.md` → "Infrastructure" section — full deploy runbook, cost breakdown, and
  every documented Bicep-vs-standard deviation and why.
- `infra/main.bicep`, `infra/modules/*` — source of truth for what gets deployed.
- `scripts/azure-provision-prerequisites.ps1`, `scripts/azure-grant-cost-auto-stop-rbac.ps1`
  — the runnable scripts this checklist points at.

---
*Maintained by paurodriguez0220 · Last updated: 2026-07-06*
*Standards: https://github.com/paurodriguez0220/standards-docs*
