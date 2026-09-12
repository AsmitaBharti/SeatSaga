# SeatSaga

**A production-style event ticket booking platform built with .NET 8 and microservices.**

SeatSaga is a hands-on distributed-systems project focused on solving real backend problems such as **concurrency, distributed transactions, idempotency, service resilience, asynchronous messaging, observability, and scalability**.

Rather than being a simple CRUD application, the system intentionally introduces failure scenarios and uses patterns commonly found in production distributed systems.

## Architecture

```text
                         ┌──────────────────┐
                         │    API Gateway   │
                         │      YARP        │
                         └────────┬─────────┘
                                  │
                 ┌────────────────┼────────────────┐
                 │                │                │
                 ▼                ▼                ▼
        ┌────────────────┐ ┌──────────────┐ ┌───────────────┐
        │ Inventory.Api  │ │ Booking.Api  │ │ Payment.Api   │
        │                │ │              │ │               │
        │ Seat inventory │ │ Saga         │ │ Payment       │
        │ Seat holds     │ │ orchestrator │ │ simulation    │
        └───────┬────────┘ └──────┬───────┘ └───────┬───────┘
                │                 │                  │
                ▼                 │                  │
        ┌───────────────┐         │                  │
        │  Service DB   │         │                  │
        └───────────────┘         │                  │
                                  ▼
                         ┌─────────────────┐
                         │ Azure Service   │
                         │ Bus             │
                         └─────────────────┘

              ┌──────────────────────────────────────┐
              │           Observability              │
              │ Logs • Metrics • Distributed Traces │
              └──────────────────────────────────────┘
```

The services are independently deployable and own their respective data. Communication happens through REST APIs and asynchronous messaging rather than a shared database.

## Services

| Service               | Responsibility                                             |
| --------------------- | ---------------------------------------------------------- |
| **Inventory.Api**     | Events, seat inventory, seat holds, optimistic concurrency |
| **Booking.Api**       | Booking workflow and Saga orchestration                    |
| **Payment.Api**       | Simulated payment processing and failure injection         |
| **API Gateway**       | Routing and rate limiting                                  |
| **Azure Service Bus** | Asynchronous service communication                         |

---

# Key Distributed-System Patterns

### Saga Orchestration

Booking involves multiple services:

```text
Booking
   │
   ▼
Hold Seat
   │
   ▼
Process Payment
   │
   ▼
Confirm Booking
```

If a downstream operation fails, the Booking service executes a **compensating action** instead of attempting a distributed database rollback.

Example:

```text
Hold Seat
   ↓
Payment fails
   ↓
Release Seat
   ↓
Booking Cancelled
```

This demonstrates how distributed workflows maintain business consistency across independently owned databases.

### Optimistic Concurrency

Two customers may attempt to reserve the same seat simultaneously.

SeatSaga uses optimistic concurrency to ensure only one request successfully updates the seat:

```text
Customer A ──► Hold Seat ──► Success
Customer B ──► Hold Seat ──► LostRace
```

The database concurrency token prevents the second update from overwriting the first.

### Idempotent APIs

Booking requests support an **Idempotency Key** to prevent duplicate bookings when clients retry requests.

```text
POST /api/bookings
Idempotency-Key: booking-123
```

If the same key is received again, the existing booking is returned instead of creating another booking.

A database unique constraint provides an additional protection layer for concurrent duplicate requests.

### Resilience

Service-to-service communication uses resilience patterns including:

* Retry
* Timeout
* Circuit breaker
* Compensation
* Graceful failure handling

Payment includes a chaos-injection endpoint that allows failures and latency to be reproduced locally.

```text
Booking
   │
   ▼
Payment
   │
   ├── Timeout
   ├── Retry
   ├── Retry
   └── Circuit Opens
          │
          ▼
     Compensation
          │
          ▼
      Release Seat
```

---

# Asynchronous Messaging

SeatSaga uses **Azure Service Bus** for asynchronous communication between services.

The messaging architecture is being extended to demonstrate:

* Asynchronous communication
* At-least-once delivery
* Duplicate message handling
* Idempotent consumers
* Retry
* Dead-letter queues
* Outbox Pattern

The goal is to decouple services and make communication more resilient as the system scales.

---

# Observability

Distributed systems are difficult to debug because a single request can cross multiple services.

SeatSaga is being instrumented for end-to-end observability using:

* Structured logging
* Application Insights
* OpenTelemetry
* Distributed tracing
* Metrics
* Correlation / trace IDs

The goal is to answer questions such as:

> Where did a request fail?

> Which downstream service caused the latency?

> How long did each dependency take?

> Was the failure caused by the application, database, network, or another service?

Example:

```text
Client
  │
  ▼
Booking.Api
  │
  ├──► Inventory.Api ──► Database
  │
  ├──► Payment.Api
  │        │
  │        └── Payment failure
  │
  ▼
Compensation
  │
  ▼
Inventory.Api
  │
  ▼
Seat Released
```

---

# LLM Seat Concierge

Inventory includes an LLM-backed **Seat Concierge** that converts natural-language seat preferences into structured search criteria.

Example:

```json
{
  "preferenceText": "2 seats together, under $60, near the front, aisle if possible"
}
```

The LLM produces structured criteria such as:

```text
Party size: 2
Maximum price: $60
Area: Front
Aisle preference: true
```

The LLM does **not** directly select seats.

```text
Natural Language
       ↓
      LLM
       ↓
Structured Criteria
       ↓
Deterministic C# Filtering / Ranking
       ↓
Real Seat Data
```

If the LLM is unavailable or returns unusable output, the service falls back to default criteria rather than failing the request.

---

# Technology Stack

### Backend

* C#
* .NET 8
* ASP.NET Core
* Entity Framework Core
* REST APIs

### Distributed Systems

* Microservices
* Saga orchestration
* Optimistic concurrency
* Idempotency
* Retry / timeout / circuit breaker
* CQRS
* Asynchronous messaging

### Messaging

* Azure Service Bus
* RabbitMQ concepts
* Idempotent consumers
* Dead-letter queues
* Outbox Pattern

### Data

* Relational databases
* Redis
* Database indexing and query optimization

### Observability

* OpenTelemetry
* Application Insights
* Distributed tracing
* Structured logging
* Metrics
* Prometheus
* Grafana
* Jaeger

### Infrastructure

* Docker
* Docker Compose
* YARP
* Kubernetes
* CI/CD

### Cloud

* Microsoft Azure

---

# Running Locally

## Prerequisites

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Docker Desktop](https://www.docker.com/products/docker-desktop/)
* Azure Service Bus namespace for messaging scenarios
* Optional LLM API key for the Seat Concierge

## Start the services

```bash
docker compose up --build
```

Swagger is available on the individual APIs when running in the Development environment.

---

# Project Structure

```text
SeatSaga/
├── SeatSaga.sln
├── docker-compose.yml
├── docs/
├── .vscode/
└── src/
    └── Services/
        ├── Inventory/
        │   └── Inventory.Api/
        ├── Booking/
        │   └── Booking.Api/
        └── Payment/
            └── Payment.Api/
```

---

# Roadmap

### Completed

* [x] .NET 8 microservices
* [x] Independent service boundaries
* [x] Separate databases
* [x] REST communication
* [x] Saga orchestration
* [x] Compensation
* [x] Optimistic concurrency
* [x] Retry
* [x] Timeout
* [x] Circuit breaker
* [x] Idempotent booking requests
* [x] Docker / Docker Compose
* [x] Azure Service Bus integration

### In Progress

* [ ] Structured logging
* [ ] Application Insights
* [ ] OpenTelemetry
* [ ] Distributed tracing
* [ ] Idempotent consumers
* [ ] Dead-letter queue handling
* [ ] Outbox Pattern
* [ ] PostgreSQL performance optimization
* [ ] Redis
* [ ] CQRS read model

### Planned

* [ ] SignalR live seat availability
* [ ] YARP API Gateway
* [ ] API rate limiting
* [ ] Prometheus / Grafana
* [ ] Jaeger
* [ ] Kubernetes deployment
* [ ] CI/CD pipeline
* [ ] Load testing and benchmarking
* [ ] Azure Container Apps
* [ ] Autoscaling
* [ ] Infrastructure as Code

---

# Why I Built This

The goal of SeatSaga is to go beyond CRUD APIs and gain hands-on experience solving problems that appear in production distributed systems.

The project focuses on:

**Consistency → Reliability → Observability → Messaging → Performance → Scalability**

Each feature is introduced to solve a specific system-design problem rather than simply adding another technology to the stack.
