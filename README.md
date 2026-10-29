# Auth, Inventory and Orders Microservices

Three .NET 8 APIs using PostgreSQL, JWT roles and parameterized SQL through Npgsql. Each service has Controllers, Services, Repositories, DTOs and Entities, with dependency injection at database and HTTP boundaries.

## Setup

Requirements: .NET 8 SDK/runtime, PostgreSQL and `psql`. From the repository root:

```powershell
psql -h localhost -U postgres -d postgres -f database/setup.sql
dotnet build AuthInventoryOrders.sln -c Release
```

The databases must not already exist. For existing empty databases, apply the individual schema scripts. The supplied tables and constraints are unchanged.

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

Usernames are case-sensitive, trimmed and limited to 100 characters. Passwords are hashed. Duplicate usernames return 409; inactive users cannot log in. To create Admin access, register an account, run the following in `auth_db`, then log in again:

```sql
UPDATE users SET role = 'ADMIN' WHERE username = 'your-admin-username';
```

Send `Authorization: Bearer YOUR_ACCESS_TOKEN` on all other requests. Tokens expire after one hour; every API validates signature, issuer, audience, expiry and role. Existing tokens retain their claims until expiry; there is no refresh or revocation flow.

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

At least one item and positive integer quantities are required. Repeated product IDs are combined with overflow checks. Ownership comes from the JWT. Creation returns 201 with `CONFIRMED` status and items. Users cannot read or cancel another user's order (404). Cancellation takes no body, restores stock and returns `CANCELLED`; repeating a completed cancellation does not restore twice.

Order uses `POST /internal/stock/deduct` and `/internal/stock/restore`, protected by a User JWT plus the service secret. Clients do not supply that secret. These accept the same `items` body. Each inventory batch validates and changes all items in one transaction with consistently ordered row locks. Restoration supports soft-deleted products. Order/items use a separate local transaction; cancellation locks the order to serialize concurrent requests.

**Consistency limitation:** separate databases cannot commit atomically. A lost stock response or subsequent Order commit failure can leave stock changed without a matching committed order state. Retrying an uncertain cancellation can restore twice. Calls time out after five seconds; stock mutations are not automatically retried. Reconcile order IDs, items and correlated logs before manually correcting uncertain outcomes. The supplied schema has no durable operation tracking or automatic recovery. Admin PUT sets absolute stock and can overwrite deductions when using stale quantities.

## Errors and logs

Problem Details responses use 400 for validation, 401 authentication, 403 permissions, 404 missing resources, 409 stock/status conflicts, 503 Inventory dependency failures and sanitized 500 unexpected errors. Responses and inter-service calls carry `X-Correlation-ID`.

Each service writes separate daily rolling files under its own `logs/` directory, rolling at 10 MB and retaining seven files. Credentials, tokens and full request bodies are excluded from logs; logs and build outputs are ignored by Git.
