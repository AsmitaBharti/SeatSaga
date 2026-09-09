# SeatSaga

Event ticket booking platform — a .NET microservices demo built to show real
system-design patterns (optimistic concurrency, saga orchestration, CQRS,
retry/timeout/circuit-breaker, rate limiting) rather than a tutorial CRUD app.

> **Status:** Inventory, Booking, and Payment services are built and wired
> together end-to-end (saga: hold seat → charge → confirm, with compensation
> on failure). Inventory also includes an LLM-backed "Seat Concierge" feature
> (natural-language seat preferences → structured filters → deterministic
> seat search). Still to come: RabbitMQ event bus, CQRS read model + SignalR,
> YARP Gateway, and Jaeger tracing. See `/docs` (coming soon) for the full
> low-level design.

## Services

| Service | Port | Purpose |
|---|---|---|
| Inventory.Api | 5001 | Seat holds (optimistic concurrency), seat map, LLM seat concierge |
| Payment.Api | 5002 | Simulated payment gateway with a chaos-injection endpoint |
| Booking.Api | 5003 | Saga orchestrator: hold → charge → confirm, with compensation |

## The LLM Seat Concierge

`POST /api/events/{eventId}/concierge` with `{ "preferenceText": "2 seats together, under $60, near the front, aisle if possible" }` — an LLM interprets the free text into structured criteria (max price, party size, area, aisle preference); all actual seat filtering/ranking happens deterministically in C# against the real seat data, never via the LLM directly. If the LLM is unreachable or returns unusable output, the feature falls back to default criteria rather than failing the request — see `Llm/SeatCriteriaParser.cs`.

To use it with a real model, set an API key (never commit it):
```bash
export ANTHROPIC_API_KEY=sk-ant-...
docker compose up --build
```
Without a key set, the endpoint still works — it just always falls back to default criteria, which is useful for demoing the graceful-degradation path itself.

## Demoing the resilience pipeline (retry → circuit breaker → compensation)

```bash
# Turn on 100% payment failures
curl -X POST http://localhost:5002/admin/chaos -H "Content-Type: application/json" \
  -d '{"failureRatePct": 100, "latencyMs": 0}'

# Place a booking — watch booking-api's logs show retries, then the circuit
# breaker trip, then the seat get released back to Available in Inventory
curl -X POST http://localhost:5003/api/bookings -H "Content-Type: application/json" \
  -d '{"eventId":1,"seatId":1,"userId":"22222222-2222-2222-2222-222222222222","amount":49.99,"idempotencyKey":"demo-1"}'

# Turn chaos back off
curl -X POST http://localhost:5002/admin/chaos -H "Content-Type: application/json" \
  -d '{"failureRatePct": 0, "latencyMs": 0}'
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose)
- VS Code with the **C# Dev Kit** extension (recommended extensions are pre-configured in `.vscode/extensions.json`)

## Run it

```bash
# From the repo root — builds the image and starts SQL Server + the Inventory API
docker compose up --build
```

The API will be available at `http://localhost:5001`, with Swagger UI at
`http://localhost:5001/swagger` (Development environment only).

Open `src/Services/Inventory/Inventory.Api/Inventory.Api.http` in VS Code
(with the **REST Client** extension) to seed an event and try holding seats
directly — no Postman needed.

## Run it without Docker (debug in VS Code)

1. Start just the database: `docker compose up sqlserver`
2. Press **F5** in VS Code (uses `.vscode/launch.json`, which builds and
   launches `Inventory.Api` with the `Development` connection string already
   pointed at `localhost:1433`)

## First-time database setup

Migrations aren't committed yet since the model is still settling. Once
you've made your first schema change:

```bash
dotnet tool install --global dotnet-ef   # one-time
dotnet ef migrations add InitialCreate --project src/Services/Inventory/Inventory.Api
```

The app applies pending migrations automatically on startup in the
Development environment (see `Program.cs`).

## Try the concurrency guarantee

1. Seed an event via the `.http` file (or Swagger).
2. Grab a `seatId` from the returned seat map.
3. Fire two `POST /api/seats/{id}/hold` requests concurrently (e.g. with
   `k6`, or just two quick clicks in Swagger/REST Client) with different
   `bookingId`s.
4. Exactly one returns `Success`; the other returns `LostRace`. No seat is
   ever double-booked — enforced by the `RowVersion` optimistic-concurrency
   token on `Seat`, not by an application-level lock.

## Project structure

```
SeatSaga/
├── SeatSaga.sln
├── docker-compose.yml
├── .vscode/                          # debug/build/task config for VS Code
└── src/
    └── Services/
        └── Inventory/
            └── Inventory.Api/
                ├── Controllers/      # SeatsController, EventsController
                ├── Data/             # InventoryDbContext
                ├── Models/           # Seat, EventEntity, SeatStatus
                ├── Dtos/             # request/response contracts
                ├── Services/         # SeatHoldExpiryService (background sweep)
                ├── Migrations/       # EF Core migrations (generated)
                ├── Program.cs
                ├── appsettings.json
                ├── appsettings.Development.json
                └── Dockerfile
```

## What's next

- `Booking.Api` — saga orchestrator (MassTransit), calls Payment with a full
  retry/timeout/circuit-breaker pipeline, compensates Inventory on failure
- `Payment.Api` — simulated gateway with a chaos-injection endpoint for
  demoing the circuit breaker live
- RabbitMQ + event contracts between services
- Redis-backed CQRS read model + SignalR live seat map
- YARP Gateway with rate limiting
- OpenTelemetry → Jaeger end-to-end tracing
