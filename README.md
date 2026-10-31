# Inventory Hold Microservice

Places temporary holds on inventory during checkout so held stock cannot be sold to someone else. Holds expire after a configurable time (default 15 minutes), and expired stock is returned automatically.

**Stack:** .NET 10 Web API (DDD layering) · MongoDB 8 (replica set, multi-document transactions) · Redis 7 · RabbitMQ 4 · React 19 + TypeScript + TanStack Query (Vite) · NUnit + Moq · Docker Compose.

---

## Quick start

```bash
docker-compose up --build
```

| What | URL |
|---|---|
| Web UI | http://localhost:5173 |
| API / Swagger | http://localhost:8080/swagger |
| Health | http://localhost:8080/health |
| RabbitMQ management | http://localhost:15672 (`inventory` / `inventory-secret`) |

The API container runs the unit tests during its image build; the image is not produced if any test fails. Compose waits for MongoDB (replica set primary), Redis and RabbitMQ health checks before starting the API.

Optional overrides (shell env or a `.env` file): `HOLD_DURATION_MINUTES` (e.g. `0.5` to watch expiry in the UI), `RABBITMQ_USER`, `RABBITMQ_PASSWORD`.

```bash
docker-compose down      # stop, keep data
docker-compose down -v   # stop and wipe MongoDB/RabbitMQ volumes
```

### Running pieces locally

```bash
# infrastructure only
docker-compose up -d mongo redis rabbitmq

# API (appsettings.json already points at localhost)
dotnet run --project src/InventoryHold.WebApi

# UI with hot reload (proxies /api to localhost:8080)
cd frontend && npm ci && npm run dev

# tests (no infrastructure needed)
dotnet test
```

### Smoke test

```bash
curl -s localhost:8080/api/inventory
curl -s -X POST localhost:8080/api/holds -H 'Content-Type: application/json' \
     -d '{"items":[{"productId":"SKU-1001","quantity":2},{"productId":"SKU-1003","quantity":1}]}'
curl -s localhost:8080/api/inventory                    # SKU-1001 down by 2, SKU-1003 by 1
curl -s -X DELETE localhost:8080/api/holds/<holdId>      # 200, stock restored
curl -s -X DELETE localhost:8080/api/holds/<holdId>      # 409 hold_not_active
curl -s -X POST localhost:8080/api/holds -H 'Content-Type: application/json' \
     -d '{"items":[{"productId":"SKU-1006","quantity":99}]}'   # 409 insufficient_stock
```

Published events can be inspected in the RabbitMQ UI → Queues → `inventory.holds.events` → *Get messages*.

---

## Solution layout

```
src/
├── InventoryHold.Contracts/        DTOs, HoldStatus enum, integration event contract (Events/)
├── InventoryHold.Domain/
│   ├── Entities/                   Hold aggregate (lifecycle rules), HoldLine, Product
│   ├── Exceptions/                 Typed domain errors (each maps to one HTTP status)
│   ├── Messaging/                  IEventPublisher
│   ├── Repositories/               IHoldRepository, IInventoryRepository, IOutboxRepository,
│   │                               IInventoryCache, IUnitOfWork
│   └── Services/                   HoldService, InventoryService, EventRelayService
├── InventoryHold.Infrastructure/
│   ├── Mongo/                      Repositories, transactional unit of work, documents, init/seed
│   ├── Caching/                    Version-keyed Redis read-through cache
│   ├── Messaging/                  RabbitMQ connection + confirming publisher
│   └── Workers/                    Hold expiry sweep, outbox relay (BackgroundServices)
├── InventoryHold.WebApi/           Controllers, ProblemDetails mapping, health checks, Program.cs
└── InventoryHold.UnitTests/        NUnit + Moq, 49 tests
frontend/                           React + TS SPA (api/, components/, lib/)
```

Dependencies point inward: WebApi → Infrastructure → Domain → Contracts. The domain has no reference to MongoDB, Redis or RabbitMQ.

---

## API

All errors are [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `application/problem+json` with a stable `code`, plus context such as `productId` / `requested` / `available`.

| Method & path | Success | Errors |
|---|---|---|
| `POST /api/holds` | **201** + `Location`, hold body | **400** `validation_failed` · **409** `insufficient_stock` · **422** `product_not_found` |
| `GET /api/holds/{holdId}` | **200** (overdue holds are expired on read and returned as `Expired`) | **404** `hold_not_found` |
| `GET /api/holds?status=Active&limit=50` | **200**, newest first (limit 1–200) | **400** |
| `DELETE /api/holds/{holdId}` | **200**, released hold body | **404** · **409** `hold_not_active` (already released/expired) · **410** `hold_expired` (was overdue; it is expired and stock returned) |
| `GET /api/inventory` | **200**, cached | — |

Request: `{ "items": [ { "productId": "SKU-1001", "quantity": 2 } ] }` — 1–50 lines, positive quantities, no duplicate products.

Why these codes: *insufficient stock* is a conflict with current resource state (409); an *unknown product* is a well-formed but semantically invalid request (422); a hold that *expired* is gone for good (410), which lets a client tell "too late" apart from "someone already released it" (409).

---

## Design decisions

### Data model (MongoDB)

- **`inventory`** — one document per product: `{ _id: "SKU-1001", Name, AvailableQuantity }`. Only available stock is stored; held stock is derivable from active holds, so there is no second counter to keep in sync.
- **`holds`** — the hold aggregate with its lines **embedded** (`Items[]`). Lines are immutable and always loaded with the hold, so embedding gives single-document reads. Product names are snapshotted so a hold reads correctly even if a product is renamed. `Status` is stored as a string; `ClosedAtUtc` records release or expiry.
- **`outbox`** — pending integration events (`Payload` is the exact JSON to publish). A TTL index removes published entries after 7 days.
- **Indexes:** `{Status, ExpiresAtUtc}` for the expiry sweep, `{Status, CreatedAtUtc desc}` and `{CreatedAtUtc desc}` for listing, `{PublishedAtUtc, CreatedAtUtc}` for the relay.
- **Seeding** uses `$setOnInsert` upserts: idempotent, safe for concurrent instances, and never resets stock on restart.

### Concurrency and atomicity

Two layers, each covering a different failure:

1. **Atomic conditional updates (race safety).** Reservation is a single `findOneAndUpdate({_id, AvailableQuantity: {$gte: q}}, {$inc: {AvailableQuantity: -q}})`: checking and decrementing happen in one operation, so stock can never go negative. Hold transitions are compare-and-set (`updateOne({_id, Status: "Active"}, {$set: {Status: ...}})`), so when a release, a read-time expiry and the background sweep race on the same hold, exactly one wins and only the winner returns stock. Unit tests cover this with 50 concurrent buyers for 10 units and 20 concurrent releases of one hold.
2. **Multi-document transactions (all-or-nothing).** A hold touches several documents (N products + the hold + an outbox event). Each operation runs in an `IUnitOfWork` backed by a MongoDB transaction (which is why Compose runs a single-node replica set). If item 3 of 3 is out of stock, or the process dies half-way, nothing is committed. No hand-written compensation code is needed. Write conflicts between concurrent transactions are retried automatically by the driver's `WithTransactionAsync`. Items are reserved in product-id order to keep conflicts predictable.

The unit of work exposes the session to repositories through an ambient `AsyncLocal`, so the domain-facing repository interfaces carry no MongoDB types.

### Expiry

A hold is expired in two complementary ways, both using the same compare-and-set transition:

- **Lazily**, whenever it is read or someone tries to release it (so an API response is never stale).
- **Proactively**, by `HoldExpirationWorker`, which sweeps `{Status: Active, ExpiresAtUtc <= now}` every 10 s, so stock comes back even if nobody looks at the hold again.

### Messaging (RabbitMQ)

- **Transactional outbox:** events are inserted in the same transaction as the state change, so an event exists if and only if the change committed. A broker outage never fails a checkout; events wait in the outbox. `OutboxRelayWorker` publishes them in order and stops at the first failure to avoid reordering.
- **Publisher confirms + persistent messages:** the relay marks an event published only after the broker confirms it. Delivery is **at-least-once**, so consumers should de-duplicate on `EventId` (also the AMQP `message-id`).
- **Topology:** durable topic exchange `inventory.holds`, routing keys `hold.created` / `hold.released` / `hold.expired`. Consumers bind only what they need (e.g. a notification service binds `hold.expired`). A durable queue `inventory.holds.events` bound to `hold.#` keeps events even before any consumer exists. Topology is declared once per channel, on one long-lived channel.
- **Payload** (camelCase JSON, header `schema-version`):

```json
{
  "eventId": "5f0c…", "eventType": "HoldReleased", "schemaVersion": 1,
  "occurredAtUtc": "2026-10-31T10:15:00Z",
  "holdId": "a1b2…", "status": "Released",
  "createdAtUtc": "2026-10-31T10:05:00Z", "expiresAtUtc": "2026-10-31T10:20:00Z",
  "items": [ { "productId": "SKU-1001", "name": "Wireless Mouse", "quantity": 2 } ]
}
```

The event carries the full hold snapshot, so a consumer (e.g. a storefront availability service or analytics) can act without calling back into this service.

### Caching (Redis)

- **What:** `GET /api/inventory` — the hottest read (dashboard and every product/checkout page) and identical for all callers. Holds are **not** cached: they are per-customer, read rarely, and must reflect expiry exactly.
- **Consistency:** every stock change invalidates the cache *after* its transaction commits. Invalidation increments a **version key** instead of deleting the entry, and readers write back under the version they read *before* querying MongoDB. A slow reader that loaded pre-change data can only write to a version nobody reads anymore, which closes the classic "stale write after delete" race.
- **TTL:** 30 s (`Redis:InventoryTtlSeconds`). This is a safety net that limits staleness if an invalidation is ever lost; correctness does not depend on it.
- **Failure mode:** if Redis is down, reads go straight to MongoDB and invalidations are logged. `/health` reports *Degraded* rather than *Unhealthy*, because the service still works.

### Error handling

Business rules throw typed `DomainException`s (`InsufficientStockException`, `HoldExpiredException`, …). One `IExceptionHandler` maps them to ProblemDetails. Anything else becomes a generic 500 with no internals leaked, so an infrastructure exception can never pass as a business 409.

### Configuration

All settings live in `appsettings.json` under `Mongo`, `Redis`, `RabbitMq`, `Holds`, `Workers` and `Cors`, and any of them can be overridden with environment variables (`Mongo__ConnectionString`, `Holds__DurationMinutes`, …), as `docker-compose.yml` does. Options are validated at startup, so a bad configuration fails fast.

### Frontend

- **State management: TanStack Query.** Almost all state here is *server* state. TanStack Query provides caching, loading/error flags and background refresh, and above all one place (`api/queries.ts`) to declare that a hold mutation makes both inventory and holds stale. Creating or releasing a hold invalidates both queries, so the dashboard updates without a page refresh. Invalidation also runs **on error**, because a 409/410 means the client's view was stale. A newly created hold is inserted into the cache immediately. Redux or Zustand would only duplicate this logic for server state; the remaining UI state (form lines, confirmation) is local `useState`.
- **Typed API layer:** `api/types.ts` mirrors the contracts (including the `HoldStatus` union). `api/client.ts` turns ProblemDetails into an `ApiError` carrying `status` and `code`.
- **UX:** multi-product holds; client-side checks (available stock, duplicates) that the server still re-validates; per-hold live countdown that asks the server to expire the hold at zero; inline release confirmation; loading skeletons; error banners with retry.
- **Same origin:** nginx (Docker) and the Vite dev server proxy `/api`, so no API URL is baked into the bundle.

---

## Testing

`dotnet test` runs 49 NUnit tests with no infrastructure. Repositories, outbox, cache and publisher are Moq mocks (strict where unexpected calls would hide bugs).

| Area | Examples |
|---|---|
| Validation | null/empty items, zero/negative quantity, blank product, duplicates after trimming, too many lines; asserts nothing is touched |
| Create | stock reserved + hold persisted + `HoldCreated` queued in one unit of work; configurable duration; 404-vs-409 distinction; mid-request failure leaves no hold, no event, no compensating writes; deterministic reservation order |
| Lifecycle | get/release/expire happy paths; release of released/expired holds; overdue release → 410 with stock returned; lost transition races return the winner's state without double-restocking |
| Concurrency | 50 parallel buyers for 10 units → exactly 10 holds, stock 0; 20 parallel releases → one success, stock restored once |
| Expiry sweep | counts only its own wins; one failing hold does not stop the batch |
| Messaging | relay publishes in order and marks published; stops at first broker failure and records it |
| Caching | cache hit skips the repository; miss loads and sorts |
| HTTP mapping | every domain exception → expected status + `code` |

---

## Trade-offs and what I'd add next

- **Replica set requirement:** transactions need a replica set. For a standalone local mongod, set `Mongo:UseTransactions=false`; the conditional updates still prevent overselling, but multi-item holds lose all-or-nothing behaviour.
- **Integration tests** with Testcontainers (Mongo/Redis/RabbitMQ) to exercise the real repositories and transactions.
- **Outbox at scale:** with several API instances the relay can publish the same event twice (allowed under at-least-once). A lease/claim field would reduce duplicates.
- **Dead-lettering** for events that keep failing (attempt counts are already recorded), plus a sample consumer.
- **Idempotency key** on `POST /api/holds`, so a client retry after a timeout cannot create two holds.
- Authentication and per-customer hold ownership (out of scope for this assignment).
