# bakedmanila-api

Pre-order API for BakedManila — cookies & banana bread, Quezon City.

## Architecture

Pragmatic layered monolith: Controllers → OrderService → Repositories → EF Core.
Clean Architecture/CQRS were considered and rejected per
[standards-docs/design-patterns.md](https://github.com/paurodriguez0220/standards-docs) —
one database, one consumer; the layers would be indirection without benefit.
Growth seams: `IPaymentMethod` (PayMongo later), `INotificationSender` (ACS Email —
see [Admin API](#admin-api) — logging fallback until configured), `Order.CustomerId`
(customer accounts later).

Spec: `docs/superpowers/specs/2026-07-04-bakedmanila-design.md`.

## Run locally

Requires .NET 10 SDK and SQL Server LocalDB (`sqllocaldb info MSSQLLocalDB`).

    dotnet run --project src/BakedManila.Api

Dev startup migrates and seeds the `BakedManila.Dev` LocalDB database.

API docs (Development only):
- Interactive reference (Scalar): `/scalar/v1`
- Raw OpenAPI document: `GET /openapi/v1.json`

## Admin API

Admin endpoints require a JWT bearer token issued by the login endpoint.

**Login**

    POST /api/auth/login
    { "email": "...", "password": "..." }

Response: `{ "token": "..." }`. Dev credentials are seeded from `Admin:Email` /
`Admin:Password` in `appsettings.Development.json` (`admin@bakedmanila.local` /
`DevAdmin!2026`) — do not reuse these outside local development.

**Endpoints** (all under `/api/admin`, `Authorize` role `Admin` unless noted):

- `GET /api/admin/orders` — filtered order list (status, date range, rush)
- `PATCH /api/admin/orders/{id}/status` — transition order status
- `PATCH /api/admin/orders/{id}/payment` — record payment
- `GET|POST|PUT|DELETE /api/admin/products` — product CRUD with soft delete
- `POST /api/admin/products/{id}/images` / `DELETE /api/admin/products/{id}/images/{imageId}` —
  upload/remove a product image (JPEG/PNG/WebP, server-generated file name, size-limited)

**Image storage**

`Images:Provider` selects the backing store:

- `FileSystem` (default in Development) — writes under `Images:FileSystemRoot`
  (defaults to `App_Data/images`) and serves them at `/images/*` via static files.
  This directory is git-ignored; it's dev-only scratch data.
- `AzureBlob` — writes to the `product-images` container using
  `ConnectionStrings:BlobStorage`.

Public URLs are built from `Storage:PublicBaseUrl`
(`http://localhost:5127/images` in dev).

**Email notifications**

`Email:ConnectionString` selects the notification sender:

- Empty (default) — `LoggingNotificationSender` logs the order-placed event;
  no email is sent. This is the out-of-the-box behavior for local dev.
- Set to an Azure Communication Services connection string — `AcsEmailNotificationSender`
  sends a plain-text email via ACS Email using `Email:From` and `Email:To`
  (fire-and-forget; failures are swallowed by `OrderService` so order placement
  never fails because of email).

**Authorizing in Scalar**

1. Run the app and open `/scalar/v1`.
2. Call `POST /api/auth/login` with the dev admin credentials to get a token.
3. Click the **Auth** button (top of the Scalar UI), choose **Bearer**, and
   paste the token.
4. Admin requests made from Scalar will now include the `Authorization: Bearer ...` header.

## Test

    dotnet test

Integration tests create throwaway LocalDB databases and delete them on dispose.

## Infrastructure

Provisioned by Bicep under `infra/` — one Linux B1 App Service serving the API and the
built storefront same-origin, Azure SQL serverless, Blob Storage for product images,
Key Vault for secrets, Application Insights, and Log Analytics. Region is hardcoded to
Southeast Asia; naming follows `{type}-bkdmnl-{env}-sea` (see
[standards-docs/azure-infra.md](https://github.com/paurodriguez0220/standards-docs)).

`main.bicep` deploys at resource-group scope — every module deploys implicitly into the
target resource group, so the OIDC service principal only needs Contributor on the
bkdmnl resource groups, not the subscription. The resource group itself must exist
first (a resource-group-scoped deployment cannot create its own resource group):

    az group create `
      --name rg-bkdmnl-prod-sea `
      --location southeastasia

    az deployment group create `
      --resource-group rg-bkdmnl-prod-sea `
      --template-file infra/main.bicep `
      --parameters infra/parameters/prod.bicepparam

In CI this runs via the manual-only `infra-bkdmnl.yml` workflow (Task 5) — it creates
the resource group with an idempotent `az group create` step, then deploys with
`az deployment group create`. Locally, set these environment variables before running
the commands above:

- `SQL_ADMIN_PASSWORD` — SQL Server admin login password. **Required on every deploy** —
  it is passed straight to the SQL server's `administratorLoginPassword`, so an unset
  value fails the deployment. Must not contain `;` or quotes (it is composed into the
  ADO connection string secret).
- `JWT_SIGNING_KEY` — JWT signing key (64+ random hex chars recommended)
- `ADMIN_PASSWORD` — seed admin account password
- `ACS_EMAIL_CONNECTION_STRING` — Azure Communication Services Email connection string
  (optional — omitted, the app keeps using `LoggingNotificationSender`)

The last three are optional with skip semantics: each has an empty default, and leaving
one unset skips its Key Vault secret write rather than writing a blank value.

**Deviation from `azure-infra.md`:** SQL auth (login/password in Key Vault, referenced
via `@Microsoft.KeyVault(SecretUri=...)`) is used instead of managed identity for the
database connection. Entra-only SQL auth requires a post-deployment contained-user
script against the database, which the standard's "no manual post-deployment steps"
rule forbids. SQL auth keeps provisioning to a single Bicep deployment. Blob Storage
access authenticates with the KV-stored connection string, not the app's managed
identity — there is no Storage RBAC grant.

**Second deviation — Key Vault access policy instead of RBAC:** the deploy service
principal only has Contributor on the resource group, and this tenant's admin cannot
grant it `roleAssignments/write`, so the template must not contain any
`Microsoft.Authorization/roleAssignments` resources. Key Vault access is granted via an
access policy instead of RBAC (`enableRbacAuthorization: false` in `keyVault.bicep`,
grant applied by `infra/modules/keyVaultAccessPolicy.bicep`), since an access policy
write is a property of the vault resource and Contributor-compatible. The former
`keyVault.rbac.bicep` and `storageAccount.rbac.bicep` modules (the latter granted
Storage Blob Data Contributor, a forward-looking grant the app never consumed) have
been removed for the same reason. Revisit RBAC for both if tenant permissions ever
allow granting `roleAssignments/write`.

**Third deviation — cost auto-stop needs one manual role grant:**
`infra/modules/costAutoStopLogicApp.bicep` provisions a Logic App (its own managed
identity) that the cost Action Group invokes to stop the web app once the monthly
budget (`infra/modules/budget.bicep`) crosses 100% of `budgetAmount`. Stopping a web
app is an ARM control-plane operation requiring a role grant (`Website Contributor`)
on whoever performs it — the same `roleAssignments/write` restriction above means the
deploy service principal cannot grant this to the Logic App's identity either. Until
the grant below is applied, the budget and its 80%/100% email alerts still work; only
the auto-stop HTTP action fails (visible in the Logic App's run history), so skipping
this step doesn't break the deploy pipeline — it's an enhancement, not a dependency.

One-time, per environment, after the first deploy that creates the Logic App:

    $logicAppName = az deployment group show --resource-group rg-bkdmnl-prod-sea `
      --name main --query properties.outputs.costAutoStopLogicAppName.value -o tsv
    $principalId = az resource show --resource-group rg-bkdmnl-prod-sea `
      --resource-type Microsoft.Logic/workflows --name $logicAppName `
      --query identity.principalId -o tsv

    az role assignment create --assignee $principalId --role "Website Contributor" `
      --scope /subscriptions/<sub-id>/resourceGroups/rg-bkdmnl-prod-sea/providers/Microsoft.Web/sites/app-bkdmnl-prod-sea

**Budget:** production is capped at $20/month (`budgetAmount` in `prod.bicepparam`) —
80% actual spend emails `alertEmail`, 100% additionally stops the web app via the
mechanism above. Disabled for other environments (`budgetAmount` defaults to `0`,
which skips the budget/action-group/Logic App deployment entirely).

**Task-7 runbook note:** on first provision, App Service application settings can
briefly serve unresolved `@Microsoft.KeyVault(SecretUri=...)` references if the app
starts before the Key Vault access policy grant (secrets `get`/`list` for the app's
managed identity) has propagated. If the app fails to start or throws configuration
errors on first deploy, restart the Web App once after a minute or two — grant
propagation is typically the cause, and a restart re-resolves the references once it's
in effect.

**Cost:** roughly $13–15/month per environment — App Service B1 (~$13/mo), SQL
serverless with auto-pause after 60 minutes idle (near-$0 when idle, ~$0.50–1/mo for a
low-traffic storefront), Storage/Key Vault/Application Insights/Log Analytics (all
consumption-based, cents/month at this scale). The cost auto-stop resources (Logic App,
Action Group, Budget) add negligible cost — Consumption-plan Logic Apps and Action
Groups are billed per execution and this one fires at most a few times a month.

### One-time per-environment bootstrap (human, not CI)

The GitHub OIDC identity is granted Contributor **per resource group** (least
privilege), so a human must create each environment's RG and grant the role once:

    az group create --name rg-bkdmnl-<env>-sea --location southeastasia
    az role assignment create --assignee <deploy-app-client-id> --role Contributor `
      --scope /subscriptions/<sub-id>/resourceGroups/rg-bkdmnl-<env>-sea

The identity itself (Entra app + federated credential for
`repo:paurodriguez0220/bakedmanila-api:environment:prod`) is also a one-time
manual creation. After that, all infrastructure changes flow through the
`infra-bkdmnl` workflow.
