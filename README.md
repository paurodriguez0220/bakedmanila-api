# bakedmanila-api

Pre-order API for BakedManila — cookies & banana bread, Quezon City.

## Architecture

Pragmatic layered monolith: Controllers → OrderService → Repositories → EF Core.
Clean Architecture/CQRS were considered and rejected per
[standards-docs/design-patterns.md](https://github.com/paurodriguez0220/standards-docs) —
one database, one consumer; the layers would be indirection without benefit.
Growth seams: `IPaymentMethod` (PayMongo later), `INotificationSender` (ACS Email in Plan 2),
`Order.CustomerId` (customer accounts later).

Spec: `docs/superpowers/specs/2026-07-04-bakedmanila-design.md`.

## Run locally

Requires .NET 10 SDK and SQL Server LocalDB (`sqllocaldb info MSSQLLocalDB`).

    dotnet run --project src/BakedManila.Api

Dev startup migrates and seeds the `BakedManila.Dev` LocalDB database.
OpenAPI: `GET /openapi/v1.json` (Development only).

## Test

    dotnet test

Integration tests create throwaway LocalDB databases and delete them on dispose.
