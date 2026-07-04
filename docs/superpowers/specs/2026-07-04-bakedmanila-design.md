# BakedManila — Design Spec

**Date:** 2026-07-04
**Status:** Approved pending final review
**Repos:** `bakedmanila-api` (ASP.NET Core API + infra), `bakedmanila-web` (React storefront + admin)

---

## 1. Overview

BakedManila is an e-commerce webapp for a home bakery in Quezon City (Instagram: `_baked.manila`) selling cookies and banana bread with a rotating, frequently-changing menu. It serves two purposes:

1. A real ordering channel replacing DM-based order taking.
2. A portfolio piece for the developer — built properly against `standards-docs/`.

### Users and roles

| Role | Who | Capabilities |
| --- | --- | --- |
| Customer (anonymous) | Cookie buyers, mostly mobile, arriving from Instagram/Facebook | Browse menu, build cart, place pre-order as guest, look up order status |
| Admin | The baker (and the developer) | Log in, manage products + photos, view orders, update order/payment status |

### Business model (v1)

- **Order-first, pay outside**: customers place pre-orders in the app; payment (GCash / bank transfer / COD) and delivery arrangements happen in conversation. The baker confirms each order manually.
- **Pre-order with preferred date**: everything is baked to order. Customers pick a preferred date; rush requests are a flag + notes, resolved by discussion.
- **Fulfillment**: pickup, or delivery booked manually by the baker (Lalamove/Grab). No delivery integration.

### Explicit growth paths (designed for, not built)

| Future need | Seam in v1 |
| --- | --- |
| Online payments (PayMongo: GCash/Maya/cards) | `PaymentMethod` strategy abstraction; `PaymentStatus` on Order; checkout flow isolates payment step |
| Customer accounts | ASP.NET Core Identity already in place for admin; `Order.CustomerId` nullable FK exists from day one; orders linkable by phone/email |
| More notification channels | `INotificationSender` interface; `OrderPlaced` domain event |

### Out of scope for v1

- Online payment processing, delivery booking/quoting, customer registration, inventory/stock counts, discount codes, order audit trail (`OrderStatusHistory`), delivery address structure (captured in notes), multi-admin roles.

---

## 2. Architecture

**Approach: pragmatic layered monolith** (Clean Architecture and CQRS were considered and rejected per `design-patterns.md` — cargo-cult for this scale; the README will state this reasoning).

```
bakedmanila-web (React SPA: storefront + /admin)
        │ REST/JSON
bakedmanila-api (ASP.NET Core, .NET 9)
  Controllers → Domain services → Repositories → EF Core
        │              │               │
   Azure SQL      Blob Storage      Email provider
```

### Solution structure — `bakedmanila-api`

```
src/
├── BakedManila.Api/    controllers, auth setup, middleware, DI wiring
└── BakedManila.Core/   domain entities (with behavior), domain services,
                        repository interfaces + EF implementations, DbContext,
                        IPaymentMethod, INotificationSender, OrderPlaced event
tests/
└── BakedManila.Core.Tests/   xUnit unit + integration tests
infra/                  Bicep (see §6)
NuGet.config            nuget.org only (corporate-feed isolation)
```

Two projects, not four: one database, one consumer — splitting Infrastructure/Application out now would be premature abstraction. Domain entities carry behavior (e.g. `Order.TransitionTo(status)`) to avoid an anemic model.

### Patterns in use (each justified by a present need or an approved growth path)

- **Repository** — `IProductRepository`, `IOrderRepository`; hides EF from domain logic (standard).
- **Strategy** — `IPaymentMethod` with single v1 implementation `ManualPayment`; PayMongo slots in later.
- **Observer / domain event** — `OrderPlaced` raised after order commit; v1 sole handler sends admin email.

### Auth

- ASP.NET Core Identity, dedicated database → tables renamed without the `AspNet` prefix (`Users`, `Roles`, …) per `code-style.md`.
- Single role: `Admin`. JWT bearer for API; React admin stores the session.
- Customers are anonymous (guest checkout). No customer auth in v1.

---

## 3. Data Model

EF Core code-first, Fluent API, explicit `decimal(18,2)` on all money columns, explicit max lengths on all strings, explicit cascade behavior. Prices are PHP.

### Product
| Column | Type | Notes |
| --- | --- | --- |
| Id | int PK | |
| Name | nvarchar(100) | |
| Slug | nvarchar(120), unique | URL-friendly |
| Description | nvarchar(2000) | |
| Price | decimal(18,2) | |
| IsAvailable | bit | "sold out / off menu" toggle |
| IsDeleted | bit | soft delete; global query filter |
| SortOrder | int | admin-controlled display order |
| CreatedAt, UpdatedAt | datetime2 | |

### ProductImage
| Column | Type | Notes |
| --- | --- | --- |
| Id | int PK | |
| ProductId | int FK → Product | cascade delete |
| BlobName | nvarchar(260) | path in Blob Storage, server-generated |
| SortOrder | int | first = card thumbnail |

### Order
| Column | Type | Notes |
| --- | --- | --- |
| Id | int PK | |
| OrderNumber | nvarchar(20), unique | human-friendly, e.g. `BM-2026-0042` |
| Status | int enum | `Pending → Confirmed → Ready → Completed`, `Cancelled` from any non-terminal state; transitions validated in domain |
| CustomerName | nvarchar(100) | |
| Phone | nvarchar(20) | **required** |
| Email | nvarchar(256), nullable | for confirmation copy |
| MessengerHandle | nvarchar(100), nullable | |
| PreferredDate | date | requested pickup/delivery date |
| IsRush | bit | |
| Notes | nvarchar(1000), nullable | rush details, delivery discussion, address |
| FulfillmentType | int enum | `Pickup`, `Delivery` |
| Subtotal | decimal(18,2) | snapshot at order time |
| PaymentMethod | int enum | `ManualGcash`, `ManualBankTransfer`, `Cod` |
| PaymentStatus | int enum | `Unpaid`, `Paid` — toggled manually by admin |
| CustomerId | nvarchar(450), nullable FK → Users | unused in v1; accounts growth path |
| CreatedAt | datetime2 | |

### OrderItem
| Column | Type | Notes |
| --- | --- | --- |
| Id | int PK | |
| OrderId | int FK → Order | cascade delete |
| ProductId | int FK → Product | **restrict** (products are soft-deleted, history preserved) |
| ProductName | nvarchar(100) | snapshot |
| UnitPrice | decimal(18,2) | snapshot |
| Quantity | int | |

Plus ASP.NET Core Identity tables (renamed `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`).

Key rules: order items snapshot name + price; deleting a product is a soft delete; `IsAvailable=false` means "off this week's menu".

---

## 4. API Surface

REST, JSON, `/api` prefix. DTOs are `record` types; EF entities never cross the API boundary. All errors return RFC 7807 ProblemDetails via ASP.NET Core's `IProblemDetailsService` / `Results.ValidationProblem()` with field-level `errors` — no custom error shapes (per `api-design.md`). Public endpoints never expose internal int IDs: products are addressed by slug, orders by OrderNumber + phone.

### Public
| Endpoint | Behavior |
| --- | --- |
| `GET /api/products` | Available, non-deleted products, sorted by SortOrder, with image URLs |
| `GET /api/products/{slug}` | Product detail |
| `POST /api/orders` | Place order. Validates product availability, **re-prices server-side** (client prices never trusted), generates OrderNumber, saves transactionally, raises `OrderPlaced` after commit. Returns OrderNumber. Rate-limited: fixed-window, 5 orders per 10 minutes per IP, 429 + `Retry-After` on breach. |
| `GET /api/orders/{orderNumber}?phone=…` | Status lookup; requires matching phone to prevent enumeration |

### Admin (JWT, `Admin` role)
| Endpoint | Behavior |
| --- | --- |
| `POST /api/auth/login` | Email + password → JWT |
| `GET /api/admin/orders?status=&from=&to=` | Filterable list, newest first |
| `PATCH /api/admin/orders/{id}/status` | Validated status transition |
| `PATCH /api/admin/orders/{id}/payment` | Mark Paid/Unpaid |
| `GET/POST/PUT/DELETE /api/admin/products` | CRUD; DELETE = soft delete |
| `POST /api/admin/products/{id}/images` | Upload photo → Blob Storage. Validates content type + ≤5 MB; server-generated blob name `products/{productId}/{guid}.{ext}`. Blob written before DB row. |
| `DELETE /api/admin/products/{id}/images/{imageId}` | Remove photo (DB row + blob) |

Email notification (`OrderPlaced` → admin email) failure is logged, never fails the order.

---

## 5. Frontend — `bakedmanila-web`

React 19 + Vite + TypeScript strict + Tailwind CSS v4. Standards: `web-components.md` (component design, stories, tests), `code-style.md` TypeScript rules.

```
src/
├── components/    shared presentational components (one per folder/file, kebab-case,
│                  co-located .stories.ts + .test.tsx)
├── pages/
│   ├── storefront/   menu, product detail, cart, checkout, order-lookup
│   └── admin/        login, orders, order-detail, products, product-edit
├── api/           typed client per resource + DTO types mirroring API
├── stores/        cart (Zustand, persisted to localStorage), admin auth
└── lib/           PHP currency formatting, date helpers
```

- One app, two zones: `/` storefront, `/admin/*` behind route guard. Code-split by route.
- Server state via TanStack Query; forms via react-hook-form + zod.
- **Money handled as integer centavos** in API payloads and state; formatted only for display.
- Mobile-first (390×844 baseline), 44px touch targets, safe-area insets, `viewport-fit=cover`.
- **PWA: yes** — installable storefront via Vite PWA plugin, configured per `pwa.md` (manifest, safe-area, standalone gate). Offline scope is minimal: cached shell + menu; ordering requires connectivity.

### Visual design — "Warm Signature"

Chosen from mockups built with the bakery's real Instagram photography (bright, natural light, warm neutral backdrops). Design must let photos dominate; existing brand identity (black circular logo with gold lettering) is kept.

**Design tokens (Tailwind theme):**

| Token | Value | Use |
| --- | --- | --- |
| `cream` | `#FAF5EC` | page background |
| `surface` | `#FFFDF8` / `#FFFFFF` | cards |
| `ink` | `#141210` | primary text, buttons, logo circle |
| `gold` | `#B08D42` | accents, prices, eyebrows |
| `gold-bright` | `#D9B65C` | logo lettering, highlights on dark |
| `taupe` | `#7A6A55` | secondary text |

**Typography:** Fraunces (display serif — headlines, product names, italic gold emphasis) + Inter (UI, body). **Shape:** rounded cards (~14–18px radius), soft shadows (`rgba(20,18,16,.07–.12)`), pill buttons (ink bg, cream text). **Eyebrow labels:** small caps, letter-spaced, gold. **Rule:** photography always on cream or white — never on busy backgrounds; no decoration competing with product photos.

A distilled `DESIGN.md` + Tailwind theme config in `bakedmanila-web` carries these tokens; components never hardcode hex values.

---

## 6. Infrastructure

Azure, app code **`bkdmnl`**, region Southeast Asia (hardcoded in bicep per standard). Target ≈ $13–15/month.

```
infra/
├── main.bicep                   thin orchestrator
├── modules/                     one per resource; RBAC separate
│   appServicePlan (B1 Linux) · webApp (API + built React static files,
│   managed identity) · sqlServer (serverless, auto-pause) · storageAccount
│   (+ .rbac) · keyVault (+ .rbac) · appInsights · logAnalytics
└── parameters/  dev.bicepparam · prod.bicepparam
```

Names: `rg-bkdmnl-prod-sea`, `app-bkdmnl-prod-sea`, `sql-bkdmnl-prod-sea`, `stbkdmnlprodsea`, `kv-bkdmnl-prod-sea`, `appi-bkdmnl-prod-sea`, `log-bkdmnl-prod-sea`.

- Two environments: dev (on-demand, tear down freely), prod.
- SQL serverless auto-pause is the main cost lever. **Known trade-off:** first request after idle pause takes ~30–60 s. Options at implementation: business-hours keep-alive ping, or accept it initially.
- Managed identity + RBAC throughout; secrets (SQL connection, JWT signing key, mail credentials) in Key Vault via `@Microsoft.KeyVault` references. No passwords in config.
- Product images container: public read (images are public content), write via managed identity only.
- Email: **Azure Communication Services Email** behind `INotificationSender` — stays in the Azure/bicep story, managed-identity auth, pay-per-message (pennies at this volume). Provider swappable via the interface if it disappoints.
- CI/CD: GitHub Actions — build + test both repos, `az deployment` of bicep, deploy app. Push to `main` → prod. Repos pushed only to the `paurodriguez0220` GitHub account (CLAUDE.md policy).

---

## 7. Error Handling

**API**
- Global exception middleware → RFC 7807; unhandled exceptions logged to App Insights, generic 500 outward.
- Domain exceptions (`ProductNotFoundException`, `ProductUnavailableException`, `InvalidStatusTransitionException`) → 404 / 409 / 422.
- `POST /api/orders`: validate → re-price → save in one transaction; `OrderPlaced` email after commit, failure logged and swallowed (order must not fail because email did).
- Status transitions validated in `Order.TransitionTo`; illegal moves throw.
- Image upload: content-type/size rejection before storage; blob written before DB row (orphan blobs harmless; dangling DB rows are not).

**Frontend**
- Every async handler: `catch` + local error state surfaced in UI (per `web-components.md`); errors cleared on retry.
- TanStack Query for uniform loading/error/retry; checkout failure preserves cart.
- Route-level error boundary with friendly fallback (link to Instagram DM as escape hatch).

---

## 8. Testing

| Layer | Tools | Coverage |
| --- | --- | --- |
| API unit | xUnit, AAA | Order pricing snapshots, status transition rules, order-number generation, payment strategy selection |
| API integration | xUnit + SQLite in-memory or Testcontainers | `POST /api/orders` happy path + every validation failure; admin auth 401/403; product CRUD |
| Frontend unit | Vitest + RTL + user-event | Cart math, checkout validation, dual-mode product modal, role-based queries only |
| Component states | Storybook 10 (Vite builder) | Default + distinct-state stories per shared component; iPhone viewport |
| E2E | Playwright | Two smoke journeys: customer places order; admin logs in and confirms it |

Commit gate (CLAUDE.md): clean build, green tests, standards reflection, no debug artifacts.

---

## 9. Decisions Log

| # | Decision | Chosen | Rejected alternatives |
| --- | --- | --- | --- |
| 1 | Order/payment flow | Order-first, pay outside (upgrade path to gateway) | Full online payments now; inquire-only page |
| 2 | Catalog management | In-app admin panel | Developer-managed data; headless CMS |
| 3 | Fulfillment model | Pre-order + preferred date + rush notes, manual delivery | Slot booking; weekly drops; ready stock |
| 4 | Order notification | Admin email + admin panel list (source of truth) | In-app only |
| 5 | Customer contact | Phone required; email, Messenger optional | Accounts-only contact |
| 6 | Customer accounts | Guest checkout only; schema ready for accounts | Optional or required accounts |
| 7 | Stack | .NET 9 API + React/Vite/TS | Next.js full-stack; Blazor |
| 8 | Backend architecture | Pragmatic layered monolith, 2 projects | Clean Architecture + CQRS (cargo-cult here); no-layer minimal APIs |
| 9 | Hosting | Azure minimal: B1 App Service + SQL serverless + Blob + KV | Full multi-service Azure; free-tier hybrid |
| 10 | Visual direction | "Warm Signature" (cream/ink/gold, Fraunces, photo-first) | Editorial Boutique; Golden Kraft; 7 other explored directions |
