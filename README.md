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
rule forbids. SQL auth keeps provisioning to a single Bicep deployment. Key Vault access
uses managed identity + RBAC today; Blob Storage access still authenticates with the
KV-stored connection string — the app's managed identity is granted the Storage Blob
Data Contributor role (see `infra/modules/storageAccount.rbac.bicep`), but that grant is
forward-looking for a future migration to `DefaultAzureCredential` and isn't consumed
by the app yet.

**Task-7 runbook note:** on first provision, App Service application settings can
briefly serve unresolved `@Microsoft.KeyVault(SecretUri=...)` references if the app
starts before the Key Vault RBAC grant (`Key Vault Secrets User` on the app's managed
identity) has propagated. If the app fails to start or throws configuration errors on
first deploy, restart the Web App once after a minute or two — RBAC propagation is
typically the cause, and a restart re-resolves the references once it's in effect.

**Cost:** roughly $13–15/month per environment — App Service B1 (~$13/mo), SQL
serverless with auto-pause after 60 minutes idle (near-$0 when idle, ~$0.50–1/mo for a
low-traffic storefront), Storage/Key Vault/Application Insights/Log Analytics (all
consumption-based, cents/month at this scale).
