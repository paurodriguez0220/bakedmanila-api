# BakedManila Deploy Implementation Plan (Plan 5 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BakedManila runs on Azure — one B1 App Service serving the API and the built storefront same-origin, SQL serverless, Blob images, Key Vault secrets, ACS email — provisioned by Bicep and deployed by GitHub Actions.

**Architecture:** Per spec §6 and `standards-docs/azure-infra.md`: app code `bkdmnl`, region Southeast Asia (hardcoded), module-per-resource Bicep with RBAC split out, one `.bicepparam` per environment. The API serves the React build from `wwwroot` with an SPA fallback (deep links like `/admin/orders` work). CI: `cicd-bkdmnl.yml` in bakedmanila-api checks out both repos, tests both, builds web with `VITE_API_BASE_URL=""` (same-origin), publishes API+SPA to the Web App via OIDC. Infra workflow is manual-only.

**Tech Stack:** Bicep (az CLI's bundled compiler), GitHub Actions (OIDC azure/login), Azure App Service Linux B1, Azure SQL serverless, Blob Storage, Key Vault, Application Insights, ACS Email.

## Global Constraints

Plan 1–2 repo law binds for API changes (0 warnings, ProblemDetails, tests, Conventional Commits + Fable footer, multiple `-m` flags, PowerShell 5.1 no `&&`). Plus:

- `azure-infra.md` binds verbatim: thin `main.bicep` orchestrator (~150 lines), one module per resource, RBAC in separate modules, `.bicepparam` per env with `readEnvironmentVariable('X','')` for secrets (empty default = skip KV write), region hardcoded, managed identity + RBAC, Key Vault references for runtime secrets, no manual post-deploy steps, no staging slots, `az` CLI read-only (Bicep is the write tool).
- `github-actions.md` binds verbatim: `{type}-{app}.yml` naming, named steps with capital verbs, least-privilege permissions, OIDC ids as **variables** not secrets, secrets via env not interpolation, pre-flight guards fail loudly, third-party actions pinned to SHA, infra workflows `workflow_dispatch` only, GitHub Environments.
- Resource names: `rg-bkdmnl-{env}-sea`, `asp-bkdmnl-{env}-sea`, `app-bkdmnl-{env}-sea`, `sql-bkdmnl-{env}-sea`, `stbkdmnl{env}sea`, `kv-bkdmnl-{env}-sea`, `appi-bkdmnl-{env}-sea`, `log-bkdmnl-{env}-sea`. Envs: dev, prod.
- **Documented deviation:** SQL uses SQL auth with the password stored in Key Vault (referenced via `@Microsoft.KeyVault`), not managed identity — Entra-only SQL auth requires a post-deploy contained-user script, which `azure-infra.md` forbids. Note it in the infra README section.
- **Gated tasks:** Task 6 (GitHub push) requires `gh` to be authenticated as **paurodriguez0220** (currently the work account is active — HARD STOP, never push with it). Task 7 (provision/deploy) requires `az login`. When a gate fails, STOP and report — the controller asks the user to authenticate.
- Secrets never committed, never echoed to logs. JWT signing key and admin password are generated values (64+ chars / strong), fed via environment variables to the bicepparam and GitHub secrets.

---

### Task 1: API production hardening

**Files (bakedmanila-api, branch `feat/deploy`):**
- Modify: `src/BakedManila.Api/Program.cs`
- Create: `src/BakedManila.Api/Middleware/SecurityHeadersMiddleware.cs`
- Test: `tests/BakedManila.Core.Tests/Api/SecurityHeadersTests.cs`, extend `OrdersEndpointTests`

**Interfaces:**
- Produces:
  1. **ForwardedHeaders** (closes the Plan-1 TODO): when NOT Development, before `UseRateLimiter`:
     ```csharp
     app.UseForwardedHeaders(new ForwardedHeadersOptions
     {
         ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
         // App Service fronts the app with a known proxy; trust it.
         KnownNetworks = { }, KnownProxies = { },  // cleared: App Service sets the header itself
     });
     ```
     Delete the `TODO(Plan 5 deploy)` comment.
  2. **Security headers** middleware (always on, all envs): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; plus `app.UseHsts()` when not Development. Registered first in the pipeline.
  3. **Lookup throttle**: new rate-limit policy `"lookup"` (fixed window, 30 per 5 minutes per IP) applied to `OrdersController.Lookup` via `[EnableRateLimiting("lookup")]`.
  4. **Startup migrations by flag**: config `Migrations:ApplyAtStartup` (bool). When true and NOT Development, run `db.Database.MigrateAsync()` at startup (no seeding). Development keeps the existing DevSeeder path. Bicep sets the flag true in Azure.

- [ ] **Step 1: Failing tests.** SecurityHeadersTests (ApiFactory): GET /api/products response carries the three headers with exact values. OrdersEndpointTests addition: 31 rapid lookups → 31st is 429 (or assert policy exists via 30 OK then 429 — keep the loop cheap: permit 30, do 30 + 1).
- [ ] **Step 2: Implement; full suite green (64 + 2 ≈ 66); commit** `feat(api): production hardening — forwarded headers, security headers, lookup throttle, startup migrations`

---

### Task 2: API serves the SPA same-origin

**Files:**
- Modify: `src/BakedManila.Api/Program.cs`, `src/BakedManila.Api/BakedManila.Api.csproj` (ensure `wwwroot/` contents publish when present), `.gitignore` (ignore `src/BakedManila.Api/wwwroot/` — CI drops the build there; never committed)
- Test: `tests/BakedManila.Core.Tests/Api/SpaFallbackTests.cs`

**Interfaces:**
- Produces: when `wwwroot/index.html` exists: `UseDefaultFiles()` + `UseStaticFiles()` (root) + a fallback that serves `index.html` for any unmatched GET whose path does NOT start with `/api`, `/openapi`, `/scalar`, or `/images`; unmatched `/api/*` keeps returning the ProblemDetails 404 (via `UseStatusCodePages` as today). When `wwwroot` is absent (local dev), behavior is unchanged. Implementation shape:
  ```csharp
  var spaIndex = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
  if (File.Exists(spaIndex))
  {
      app.UseDefaultFiles();
      app.UseStaticFiles();
      app.MapFallback("{*path}", async context =>
      {
          if (context.Request.Path.StartsWithSegments("/api")
              || context.Request.Path.StartsWithSegments("/openapi")
              || context.Request.Path.StartsWithSegments("/scalar")
              || context.Request.Path.StartsWithSegments("/images"))
          {
              context.Response.StatusCode = StatusCodes.Status404NotFound;  // StatusCodePages upgrades to problem+json
              return;
          }
          context.Response.ContentType = "text/html";
          await context.Response.SendFileAsync(spaIndex);
      });
  }
  ```
- [ ] **Step 1: Failing test.** SpaFallbackTests: ApiFactory variant whose ContentRoot gets a temp `wwwroot/index.html` written in the fixture (`builder.UseWebRoot(tempDir)`); assert GET `/admin/orders` → 200 text/html containing the sentinel from the temp index; GET `/api/nope` → 404 problem+json (NOT html). Second factory without wwwroot: `/admin/orders` → 404 (unchanged).
- [ ] **Step 2: Implement; suite green (~68); commit** `feat(api): serve SPA from wwwroot with API-safe fallback`

---

### Task 3: Web production polish

**Files (bakedmanila-web, branch `feat/deploy-prep`, merge to master when green):**
- Modify: `vite.config.ts` (`workbox: { globIgnores: ['**/admin-*'] }` inside VitePWA — admin chunks not precached for storefront visitors), `index.html` (`<meta name="description" content="Pre-order cookies and chocolate-chunk banana bread, baked to order in Quezon City.">`)

- [ ] **Step 1: Implement both; `npm test` green (169); `npm run build` clean — verify `dist/sw.js` precache manifest no longer lists `admin-*` files, and a build with `VITE_API_BASE_URL=""` succeeds (same-origin mode: `$env:VITE_API_BASE_URL=''; npm run build` — then reset). Commit** `feat(pwa): exclude admin chunks from precache; add meta description`

---

### Task 4: Bicep infrastructure

**Files (bakedmanila-api):**
- Create: `infra/main.bicep`, `infra/modules/appServicePlan.bicep`, `infra/modules/webApp.bicep`, `infra/modules/sqlServer.bicep`, `infra/modules/storageAccount.bicep`, `infra/modules/storageAccount.rbac.bicep`, `infra/modules/keyVault.bicep`, `infra/modules/keyVault.rbac.bicep`, `infra/modules/appInsights.bicep`, `infra/modules/logAnalytics.bicep`, `infra/parameters/dev.bicepparam`, `infra/parameters/prod.bicepparam`
- Modify: `README.md` (infra section: how to deploy, env vars needed, the SQL-auth deviation note)

**Interfaces (binding specs per module — follow azure-infra.md structure exactly):**
- `main.bicep`: params `appName` (default `bkdmnl`), `env`, `sku` (default `B1`), `@secure() sqlAdminPassword`, `@secure() jwtSigningKey`, `@secure() adminPassword`, `@secure() acsEmailConnectionString` (all secrets default `''`); `var location = 'southeastasia'` hardcoded; calls every module; outputs webAppName + defaultHostName. Thin — no resource bodies.
- `logAnalytics` → `log-{app}-{env}-sea`, PerGB2018, 30-day retention. `appInsights` → workspace-based, linked to it.
- `appServicePlan` → Linux (`reserved: true`), `sku` param.
- `webApp` → `app-{app}-{env}-sea` on the plan; `httpsOnly: true`; `linuxFxVersion: 'DOTNETCORE|10.0'`; system-assigned identity; app settings: `ConnectionStrings__BakedManila` = KV reference to secret `SqlConnectionString`, `Jwt__SigningKey` = KV ref `JwtSigningKey`, `Jwt__Issuer`/`Jwt__Audience` = `BakedManila`, `Admin__Email` = `admin@bakedmanila.com` (param), `Admin__Password` = KV ref `AdminPassword`, `Email__ConnectionString` = KV ref `AcsEmailConnectionString` (conditional), `Email__From`/`Email__To` params (default `''`), `Images__Provider` = `AzureBlob`, `ConnectionStrings__BlobStorage` = KV ref `BlobConnectionString`, `Storage__PublicBaseUrl` = `https://stbkdmnl{env}sea.blob.core.windows.net/product-images`, `Migrations__ApplyAtStartup` = `true`, `APPLICATIONINSIGHTS_CONNECTION_STRING` from appInsights output.
- `sqlServer` → server + database `BakedManila`, serverless `GP_S_Gen5_1`, `autoPauseDelay: 60`, `minCapacity` json('0.5'), maxSizeBytes 2 GB; firewall rule `AllowAzureServices` (0.0.0.0). Admin login `bkdmnladmin`, password param. Outputs the ADO connection string expression (server FQDN, db, user; password composed INTO THE KV SECRET in main or keyVault module — password never in outputs; build the full connection string inside the keyVault module's secret resources by passing parts).
- `storageAccount` → `stbkdmnl{env}sea`, Standard_LRS, blob container `product-images` with `publicAccess: 'Blob'`, `allowBlobPublicAccess: true`, TLS 1.2 min. `.rbac` module: `Storage Blob Data Contributor` to the webApp principal.
- `keyVault` → RBAC-mode vault (`enableRbacAuthorization: true`); conditional secrets (each `if (!empty(param))` per azure-infra.md): `SqlConnectionString` (composed full ADO string), `JwtSigningKey`, `AdminPassword`, `AcsEmailConnectionString`, `BlobConnectionString` (storage key-based conn string via `listKeys` — pragmatic: AzureBlobImageStore takes a connection string today). `.rbac`: `Key Vault Secrets User` to the webApp principal.
- `parameters/{env}.bicepparam`: `using '../main.bicep'`; env-specific values; secrets via `readEnvironmentVariable('SQL_ADMIN_PASSWORD','')` etc. with EMPTY defaults (load-bearing per standard).

- [ ] **Step 1: Author all files.**
- [ ] **Step 2: Validate.** `az bicep build --file infra/main.bicep` (compiles all modules) — 0 errors; warnings resolved or justified in the report. `az bicep build-params --file infra/parameters/prod.bicepparam` also clean. (az CLI is installed; `az bicep install` first if the compiler is missing. az stays read-only otherwise.)
- [ ] **Step 3: README infra section; commit** `feat(infra): add bicep modules for bkdmnl azure environment`

---

### Task 5: GitHub Actions workflows

**Files:**
- Create in bakedmanila-api: `.github/workflows/cicd-bkdmnl.yml`, `.github/workflows/infra-bkdmnl.yml`
- Create in bakedmanila-web: `.github/workflows/ci-web.yml` (branch `feat/ci`, merge to master)

**Interfaces:**
- `ci-web.yml` (web repo): on `push` to main + `pull_request`: checkout, Node 24, `npm ci`, `npm test`, `npm run build` (with `VITE_API_BASE_URL: ''`). No deploy (the api workflow owns deploys).
- `cicd-bkdmnl.yml` (api repo): `on: push: branches: [main]` + `workflow_dispatch`; `permissions: contents: read, id-token: write`; env `DOTNET_VERSION: '10.0.x'`; jobs:
  - `build-test`: checkout api; setup dotnet; `dotnet test` (LocalDB unavailable on ubuntu → run SQL Server 2022 as a service container `mcr.microsoft.com/mssql/server:2022-latest` with SA password from a generated step env, and pass the connection string via env var — REQUIRES a small test change: `TestDb.NewConnectionString()` reads `TEST_SQL_CONNECTION_TEMPLATE` env var when set (template with `{db}` placeholder), else LocalDB. Include that change in this task with the test-file diff.)
  - `deploy` (needs build-test, `environment: prod`, only on main): checkout api + checkout web (`repository: paurodriguez0220/bakedmanila-web`, public); setup Node; build web with `VITE_API_BASE_URL: ''`; copy `dist/*` → `src/BakedManila.Api/wwwroot/`; `dotnet publish -c Release`; `azure/login` with OIDC vars (`AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` as `vars.`); `azure/webapps-deploy` to `app-bkdmnl-prod-sea`. Pre-flight guard fails loudly if OIDC vars missing.
  - `smoke` (needs deploy): curl the site root (200, html) and `/api/products` (200, json).
- `infra-bkdmnl.yml`: `workflow_dispatch` only, input `environment` (choice dev/prod); OIDC login; `az deployment sub create` (or `az group create` + `az deployment group create`) with `infra/main.bicep` + the env bicepparam; secrets passed as env vars (`SQL_ADMIN_PASSWORD`, `JWT_SIGNING_KEY`, `ADMIN_PASSWORD`, `ACS_EMAIL_CONNECTION_STRING` from environment-scoped GitHub secrets) with pre-flight guards.
- Pin actions/checkout, actions/setup-dotnet, actions/setup-node, azure/login, azure/webapps-deploy — first-party may use version tags per standard; any third-party pinned to SHA (avoid third-party entirely if possible).

- [ ] **Step 1: Author workflows + the TestDb template change (+ run `dotnet test` locally to prove the LocalDB default path still works).**
- [ ] **Step 2: Commit both repos** (`ci: add cicd and infra workflows` / `ci: add web ci workflow`).

---

### Task 6 (GATED — GitHub): create repos and push

**Gate check (controller runs BEFORE dispatching):** `gh auth status` must show **paurodriguez0220** as an available account. If only the work account is present — STOP, ask the user to `! gh auth login` with the personal account. Never push with `paulo-rodriguez_fefi`.

- [ ] Switch: `gh auth switch --user paurodriguez0220`; verify `gh auth status`.
- [ ] Per repo (api, web): set local git identity already done; `gh repo create bakedmanila-api --public --source . --remote origin --push` (and `bakedmanila-web`). Public = portfolio.
- [ ] Verify remotes point at `github.com/paurodriguez0220/...` before every push (CLAUDE.md policy).
- [ ] Confirm `ci-web.yml` runs green on GitHub for the web repo push; `cicd-bkdmnl.yml`'s deploy job will fail until Task 7 wires Azure — expected; note it.

### Task 7 (GATED — Azure): provision, wire OIDC, deploy, smoke

**Gate check:** `az account show` succeeds. If not — STOP, ask the user to `! az login`.

- [ ] Generate secrets locally (never echoed): SQL admin password, JWT signing key (64 hex), admin password. Store them ONLY in GitHub environment secrets (`gh secret set --env prod`) and pass to the initial deployment via env vars.
- [ ] Create the Entra app registration + federated credential for GitHub OIDC (`repo:paurodriguez0220/bakedmanila-api:environment:prod`), grant it Contributor on the subscription (or the rg after creation) — via `az` (this is identity setup, not resource provisioning; allowed as the one-time bootstrap, documented in README).
- [ ] Set GitHub `vars`: AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID; `secrets` (env prod): SQL_ADMIN_PASSWORD, JWT_SIGNING_KEY, ADMIN_PASSWORD (+ ACS later).
- [ ] Run `infra-bkdmnl.yml` (env prod) via `gh workflow run`; watch to success.
- [ ] Run `cicd-bkdmnl.yml`; watch deploy + smoke to green.
- [ ] Full user-journey smoke on the live URL: menu renders, order placed, admin login works, image upload works (ACS email may stay on logging fallback until an ACS resource is provisioned — note as the one deliberate omission; provisioning ACS Email requires domain setup best done manually later).

---

## Plan Self-Review Notes

- Spec §6 coverage: B1 App Service serving API+SPA ✔ (T2/T4), SQL serverless auto-pause ✔, Blob + managed-identity RBAC ✔ (T4 — note: image store currently uses a connection string; the KV-referenced conn string keeps it working, moving to `BlobServiceClient` with MI is a future refactor), Key Vault + references ✔, Bicep per standards ✔, CI/CD per github-actions.md ✔, deferred-list closure: ForwardedHeaders ✔ (T1), security headers ✔ (T1), lookup throttle ✔ (T1), SW globIgnores ✔ (T3), meta description ✔ (T3), SPA rewrite ✔ (T2), same-origin env ✔ (T5 build). ACS Email resource provisioning deliberately deferred (domain verification is interactive) — sender stays config-switched.
- Gates are explicit (T6 account, T7 login); no push/provision happens on the work account or without login.
- Cross-repo: T3 must merge before T6 pushes web; T5's deploy job references the public web repo created in T6 — first CI run after T6 is expected to fail at deploy until T7; noted.
