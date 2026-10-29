# Auth, Inventory and Orders Microservices

Three .NET 8 APIs with PostgreSQL, JWT authentication and Admin/User roles.

## Run locally

Requirements: .NET 8 SDK/runtime, PostgreSQL and `psql`.

```powershell
psql -h localhost -U postgres -d postgres -f database/setup.sql
dotnet build AuthInventoryOrders.sln -c Release
```

Fresh setup creates three databases. Existing installations need `inventory-operations.sql` applied to `inventory_db` and `order-operations.sql` to `order_db` once. The supplied business tables are unchanged; two supporting tables enable recovery.

Open three terminals. In **each**, set the same signing key:

```powershell
$env:Jwt__Key = 'YOUR_RANDOM_SECRET_OF_AT_LEAST_32_BYTES'
```

Then run one service per terminal:

```powershell
# Auth — localhost:8084
$env:ConnectionStrings__Auth = 'Host=localhost;Database=auth_db;Username=postgres;Password=YOUR_PASSWORD'
dotnet run --project src/Auth.Api
```

```powershell
# Inventory — localhost:8085
$env:ConnectionStrings__Inventory = 'Host=localhost;Database=inventory_db;Username=postgres;Password=YOUR_PASSWORD'
$env:InternalStock__Key = 'YOUR_SEPARATE_SECRET_AT_LEAST_32_CHARACTERS'
dotnet run --project src/Inventory.Api
```

```powershell
# Order — localhost:8086
$env:ConnectionStrings__Orders = 'Host=localhost;Database=order_db;Username=postgres;Password=YOUR_PASSWORD'
$env:InternalStock__Key = 'THE_SAME_STOCK_SECRET_AS_INVENTORY'
$env:InventoryService__BaseUrl = 'http://localhost:8085/'
dotnet run --project src/Order.Api
```

Keep credentials out of Git. HTTP examples are for local use.

## API

| Service | Endpoints |
|---|---|
| Auth | `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me` |
| Inventory | `POST/GET /api/products`, `GET/PUT/DELETE /api/products/{id}`, `POST /api/products/{id}/reduce_stock` |
| Order | `POST /api/orders`, `GET /api/orders/my-orders`, `GET /api/orders/{id}`, `PATCH /api/orders/{id}/cancel` |

Register/login: `{"username":"vaibhav","password":"YOUR_PASSWORD"}`. Registration creates a User. Promote an account in `auth_db`, then log in again:

```sql
UPDATE users SET role = 'ADMIN' WHERE username = 'your-admin-username';
```

Send `Authorization: Bearer ACCESS_TOKEN` on protected requests. Admin manages products and views all orders; Users create/cancel their own orders. Tokens expire after one hour.

- Product create/update: `{"name":"Keyboard","stockQty":10}`.
- Stock reduction: `{"quantity":2}`. DELETE soft-deletes products.
- Product pagination: `?pageNumber=1&pageSize=20` (maximum 100).
- Create order: `{"items":[{"productId":"PRODUCT_UUID","quantity":2}]}`.
- Cancellation takes no body and restores stock.

## Recovery and logs

Atomic stock batches prevent overselling. Durable operation records make deduction/restoration retries safe, and a background worker resumes interrupted work. This provides **eventual consistency**, not a distributed transaction.

`202 Accepted` means pending: poll the returned `Location` until `pendingOperation` is null. Recovery requires healthy dependencies and retained operation records. Send a UUID `Idempotency-Key` on order creation; reuse it with identical items to avoid duplicate orders. Without it, each POST creates a new request.

Errors use Problem Details with correlation IDs. Each service writes daily rolling files in its own `logs/` directory. Tests, credentials, logs and build outputs are not committed.
