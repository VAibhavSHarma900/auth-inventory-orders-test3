# Auth, Inventory and Orders Microservices

Three .NET 8 APIs using PostgreSQL, JWT roles and parameterized Npgsql SQL. Each has Controllers, Services, Repositories, DTOs and Entities, with database/HTTP dependency interfaces.

## Setup

Requirements: .NET 8 SDK/runtime, PostgreSQL and `psql`. From the repository root:

```powershell
psql -h localhost -U postgres -d postgres -f database/setup.sql
dotnet build AuthInventoryOrders.sln -c Release
```

Fresh setup requires new databases. The supplied business tables remain unchanged; two supporting operation tables enable recovery. To upgrade existing Test 3 databases, apply `database/inventory-operations.sql` to `inventory_db` and `database/order-operations.sql` to `order_db` once before starting the updated APIs.

In **each of three PowerShell terminals**, set the same JWT configuration:

```powershell
$env:Jwt__Key = 'YOUR_RANDOM_SECRET_OF_AT_LEAST_32_BYTES'
$env:Jwt__Issuer = 'test3-auth'
$env:Jwt__Audience = 'test3-apis'
```

Then set the service-specific configuration and start each API:

```powershell
# Terminal 1: Auth, port 8084
$env:ConnectionStrings__Auth = 'Host=localhost;Database=auth_db;Username=postgres;Password=YOUR_PASSWORD'
dotnet run --project src/Auth.Api
```

```powershell
# Terminal 2: Inventory, port 8085
$env:ConnectionStrings__Inventory = 'Host=localhost;Database=inventory_db;Username=postgres;Password=YOUR_PASSWORD'
$env:InternalStock__Key = 'YOUR_SEPARATE_RANDOM_SECRET_AT_LEAST_32_CHARACTERS'
dotnet run --project src/Inventory.Api
```

```powershell
# Terminal 3: Order, port 8086
$env:ConnectionStrings__Orders = 'Host=localhost;Database=order_db;Username=postgres;Password=YOUR_PASSWORD'
$env:InternalStock__Key = 'THE_SAME_STOCK_SECRET_AS_INVENTORY'
$env:InventoryService__BaseUrl = 'http://localhost:8085/'
dotnet run --project src/Order.Api
```

Keep actual credentials out of Git. Each service accesses only its own database. Local HTTP is for assessment use; use HTTPS across a network.

## Authentication

- `POST /api/auth/register`: creates a `USER`.
- `POST /api/auth/login`: returns `accessToken`, `tokenType`, `expiresAt` and user details.
- `GET /api/auth/me`: returns the authenticated user's details.

Register and login accept:

```json
{"username":"vaibhav","password":"YOUR_PASSWORD"}
```

Usernames are trimmed, case-sensitive and limited to 100 characters. Passwords are hashed; inactive accounts cannot log in. Register an Admin account, promote it in `auth_db`, then log in again:

```sql
UPDATE users SET role = 'ADMIN' WHERE username = 'your-admin-username';
```

Send `Authorization: Bearer YOUR_ACCESS_TOKEN` on protected requests. All APIs validate signature, issuer, audience, expiry and role. Tokens last one hour; existing claims remain valid until expiry.

## Inventory and orders

| Method | Route | Access |
|---|---|---|
| POST | `/api/products` | Admin |
| GET | `/api/products` | Admin, User |
| GET | `/api/products/{id}` | Admin, User |
| PUT | `/api/products/{id}` | Admin |
| DELETE | `/api/products/{id}` | Admin |
| POST | `/api/products/{id}/reduce_stock` | Admin |
| POST | `/api/orders` | User |
| GET | `/api/orders/my-orders` | User: own; Admin: all |
| GET | `/api/orders/{id}` | Owner or Admin |
| PATCH | `/api/orders/{id}/cancel` | Owner with User role |

Product create/update: `{"name":"Keyboard","stockQty":10}`. Name is required and at most 150 characters; stock must be a nonnegative integer. PUT replaces editable fields. DELETE soft-deletes; inactive products cannot be ordered. Direct stock reduction accepts `{"quantity":2}`.

Product listing accepts `?pageNumber=1&pageSize=20` (maximum size 100), returning `items`, `pageNumber`, `pageSize`, `totalCount`, newest first with ID as a tie-breaker. Invalid pagination returns 400.

Create a multi-product order with a User token:

```json
{"items":[{"productId":"FIRST_PRODUCT_UUID","quantity":2},{"productId":"SECOND_PRODUCT_UUID","quantity":1}]}
```

Orders require at least one product and positive quantities. Duplicate product IDs are combined with overflow checks. Ownership comes from the JWT; another user's order returns 404. Completed creation returns 201 and `CONFIRMED`; cancellation takes no body and returns `CANCELLED`.

## Consistency and recovery

Order commits the order/items (`CREATED`) and a `PENDING` operation before calling Inventory. Inventory validates the entire batch and commits stock plus its result atomically. Order then commits `CONFIRMED` and `COMPLETED`. Rejected validation removes the provisional order/items and returns 404 or 409 without partial deductions.

Cancellation records one operation, restores stock, then commits `CANCELLED`. Inventory records outcomes by operation ID and order/action; retries return the saved outcome without changing stock again. Different payloads under the same ID are rejected. Ordered row locks prevent overselling and concurrent duplicate processing.

**202 Accepted means pending**, with `Location`, `Retry-After` and `pendingOperation` (`CREATE`/`CANCEL`). Poll the order URL until `pendingOperation` is null. Pending creation stays `CREATED`; pending cancellation stays `CONFIRMED`. The worker checks every two seconds and schedules failed calls five seconds later, including across process/database restarts. A rejected creation disappears from history; replaying its request key returns its rejection.

Send a client-generated UUID in **`Idempotency-Key`** when creating an order. Reuse it with identical items to retry safely; different items return 409. Without a supplied key, each POST is a new request. Cancellation automatically reuses its recorded operation.

Internal stock APIs require a fresh service JWT and the stock secret; caller tokens are never stored. This provides **eventual consistency and automatic recovery**, not an instantaneous distributed transaction. Dependencies must recover and operation records must be retained. Permanent business rejection, invalid configuration or damaged records require correction. Admin PUT replaces absolute stock, so stale Admin quantities can overwrite changes.

## Errors and logs

Problem Details responses use 400 for validation, 401 authentication, 403 permissions, 404 missing resources, 409 stock/status conflicts and sanitized 500 unexpected errors. Accepted operations remain pending during Inventory dependency failures rather than reporting false completion. Responses and inter-service calls carry `X-Correlation-ID`.

Each service writes separate daily rolling files under its own `logs/` directory, rolling at 10 MB and retaining seven files. Credentials, tokens and full request bodies are excluded from logs; logs and build outputs are ignored by Git.
