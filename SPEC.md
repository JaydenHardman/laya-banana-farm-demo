# BananaFarm — System Specification

**Status:** active
**Version:** 1.0.0
**Last updated:** 2026-09-21

A four-service demonstration system that generates synthetic banana harvest data at high
throughput, classifies it with a local System-1 decision model, sorts it into boxes, and
aggregates market metrics. Runs end to end with a single `docker compose up`.

---

## 1. Scope

### 1.1 In scope
- Four services: `farm` (C#), `factory` (C#), `classification` (Python), `market` (C#).
- RabbitMQ for inter-service messaging, Redis for state.
- Single-command startup via Docker Compose.
- Unit tests over the logic that carries risk (see §10).

### 1.2 Out of scope
- Any UI. `market` exposes REST + WebSocket; the dashboard is a later project.
- Authentication, authorization, rate limiting, multi-tenancy.
- Horizontal scaling, production deployment, persistent durable storage.
- HTTP webhook egress (considered and dropped in favour of WebSocket — see §8.4).

---

## 2. Architecture

```
                  ┌──────────┐
                  │   farm   │  generates ~200 bananas/sec in waves
                  └────┬─────┘
                       │ bananaProduction
                       ▼
   ┌───────────────────────────────────┐        REST         ┌────────────────┐
   │             factory               │ ──────────────────▶ │ classification │
   │  cache → classify → route → box   │ ◀────────────────── │  (Laya model)  │
   └───────────────────┬───────────────┘                     └────────────────┘
                       │ goldenBanana · pastRipe · tooLongToFill · filledBox
                       ▼
                  ┌──────────┐
                  │  market  │  REST /stats/{id} + WS /stats/live
                  └──────────┘

   RabbitMQ (topic exchange `banana`)          Redis (boxes, cache, metrics)
```

### 2.1 Technology
| Concern | Choice | Rationale |
|---|---|---|
| C# runtime | .NET 10 (LTS) | Current LTS as of 2025-11. |
| Python runtime | 3.12 | Required floor for `laya` is 3.10; 3.12 is current stable. |
| Classification model | `convaiinnovations/laya` via the `laya` SDK | See §7.1. |
| Broker | RabbitMQ 4 (topic exchange) | Sufficient at 200 msg/s; simpler than Streams. |
| State | Redis 8 | In-memory for this test; abstracted for later replacement (§9). |

### 2.2 Rejected alternatives
- **liteLLM in front of Laya.** Laya is a non-autoregressive encoder decision model with no
  chat-completions surface. liteLLM proxies generative chat APIs and has nothing to talk to
  here. The `laya` SDK is called directly.
- **RabbitMQ Streams.** Offers replay and higher throughput, neither of which this system
  needs at 200 msg/s. A topic exchange with quorum queues is simpler. Streams remain a
  drop-in replacement behind `IMessagePublisher` / the consumer base class.
- **HTTP webhooks from `market`.** Superseded by the WebSocket endpoint (§8.4).

---

## 3. Domain model

### 3.1 Banana

| Field | Type | Notes |
|---|---|---|
| `id` | `Guid` | v4. |
| `dateHarvested` | `DateTimeOffset` | UTC at generation. |
| `farmOrigin` | `FarmOrigin` | `XFarm` \| `TheBananaBoyz` \| `KindaCrazyNanas` |
| `weight` | `float` | Grams. |
| `ripeness` | `float` | `[0,1]`. 0 = raw, 0.5 = perfect, 1 = expired. |
| `grade` | `BananaGrade` | `A` \| `B` \| `C` \| `Golden` |
| `price` | `decimal` | Currency. See §3.2. |

**`price` is `decimal`, not `float`.** It is money, and it is summed across boxes and
averaged by `market`. Binary floating point would accumulate error in both. It serializes
as a JSON number, so the wire format still matches the original brief.

### 3.2 Price rules
Evaluated in this order; first match wins:

1. `grade == Golden` → uniform `[30, 70]`
2. `ripeness > 0.75` → uniform `[1, 4]`
3. otherwise → uniform `[5, 10]`

**Resolved ambiguity:** a banana that is both `Golden` and over-ripe takes the golden price
band, but still routes to `pastRipe` (§6.3). Price and routing are governed independently.

---

## 4. Service: `farm`

Generates bananas and publishes them to `bananaProduction`.

### 4.1 Distribution invariants
All of the following must hold simultaneously over a large sample:

| Invariant | Target |
|---|---|
| `ripeness > 0.75` across all bananas | 3.5% |
| `XFarm` bananas with `ripeness > 0.75` | 0% (never) |
| Share of the over-ripe population from `TheBananaBoyz` | 20% |
| Share of the over-ripe population from `KindaCrazyNanas` | 80% |
| `grade == Golden` | 0.1% (1 in 1000) |

### 4.2 How the invariants are satisfied
Farm origin is sampled first, from configurable weights (default uniform ⅓ each). The
probability of being over-ripe is then *conditional on origin*, derived at startup from the
global targets:

```
P(overripe | origin) = P(overripe) × share(origin) / P(origin)
```

At the default uniform origin weights this yields:

| Origin | P(overripe \| origin) |
|---|---|
| `XFarm` | 0.0% |
| `TheBananaBoyz` | 2.1%  ( = 0.035 × 0.20 / (1/3) ) |
| `KindaCrazyNanas` | 8.4%  ( = 0.035 × 0.80 / (1/3) ) |

Deriving rather than hardcoding these keeps the invariants true when origin weights are
reconfigured. Startup fails fast if the configured weights make a required conditional
probability exceed 1.

### 4.3 Unspecified distributions (defaults, all configurable)
The brief did not constrain these. Defaults chosen for plausibility:

- **Ripeness.** Over-ripe branch: uniform `(0.75, 1.0]`. Normal branch: triangular over
  `[0, 0.75]` with mode at 0.5 ("perfect ripeness"), so most bananas land near ideal.
- **Grade** for non-golden bananas: `A` 30%, `B` 45%, `C` 25%.
- **Weight:** normal `μ=120g, σ=18g`, clamped to `[70, 200]`.
- **Golden** is sampled independently of ripeness, so golden-and-over-ripe can occur. The
  `pastRipe` rule in §6.3 explicitly requires this to be possible.

### 4.4 Wave generation
Target mean throughput is 200 bananas/sec, varying unpredictably.

A three-state Markov chain selects a regime each tick; the regime sets the target of an
Ornstein–Uhlenbeck random walk on a rate multiplier, which scales the base rate. Emission
within a tick is a Poisson process, so inter-arrival times are irregular rather than evenly
spaced.

| Regime | Multiplier target | Mean dwell |
|---|---|---|
| `Downturn` | 0.25× | 8s |
| `Normal` | 1.0× | 25s |
| `Spike` | 2.6× | 4s |

Messages are published in batches to keep broker round-trips off the hot path.

---

## 5. Service: `classification`

FastAPI wrapper over the Laya model. Consumed by `factory` over REST.

### 5.1 Why Laya, called directly
Laya is a multilingual non-autoregressive "System 1" decision model (ModernBERT-large,
421M params for English) with a decision head that answers *typed questions* about a text
state in a single forward pass, returning calibrated probabilities. Its three primitives —
`choice`, `score`, `noul` — are exactly the question schema this system needs. It is loaded
via `laya.load()` and invoked with `agent.predict(state, questions)`.

### 5.2 Question schema
Owned by the classification service and versioned there, not sent by callers. Callers post
bananas; the service decides what to ask. This keeps prompt tuning in one place.

```python
{
  "grade":     {"type": "choice", "instructions": "What Grade is the banana? {object}",
                "criteria": {"GOLDEN": "world class, solid gold", "A": "ideal",
                             "B": "acceptable but not ideal", "C": "not ideal",
                             "OTHER": "everything else"}},
  "ripeness":  {"type": "score",  "instructions": "How close is this banana to being ripe "
                                                  "if 0 is unripe and 1 is expired? {object}",
                "criteria": ["1", "2", "3"]},
  "throwAway": {"type": "noul",   "instructions": "Should this banana be thrown away?"}
}
```

`{object}` is substituted with the banana rendered as a human-readable string.

### 5.3 API
| Route | Purpose |
|---|---|
| `POST /classify` | One banana → typed answers + confidences. |
| `POST /classify/batch` | Many bananas in one request. Used by `factory`. |
| `GET /health` | Liveness. |
| `GET /ready` | Readiness — 503 until model weights are resident. |

`factory` waits on `/ready` via Compose `depends_on: service_healthy`, so no traffic reaches
a cold model.

### 5.4 Backends
`ClassifierBackend` is a Protocol with two implementations:
- `LayaBackend` — the real model. **Default.**
- `StubBackend` — deterministic, derives answers from the banana's own fields. Used by the
  test suite only, so tests need neither weights nor a GPU.

Selected by `CLASSIFIER_BACKEND` (`laya` | `stub`).

### 5.5 Concurrency
Inference is CPU-bound and holds the GIL. `predict` runs in a thread pool bounded by a
semaphore sized from `CLASSIFIER_MAX_CONCURRENCY` (default: CPU count − 1), so the service
degrades in latency rather than thrashing.

---

## 6. Service: `factory`

Consumes `bananaProduction`, classifies, routes, and boxes.

### 6.1 Throughput strategy
Laya on the container's CPU is roughly 300–450ms per banana (no Metal or CUDA access from a
Docker container on Apple Silicon). Classifying every banana at 200/sec is not achievable —
it would need on the order of 60–90 cores.

Bananas are therefore classified through a **bucketed cache** in Redis:

```
key = {origin}|{grade}|{ripeness ÷ 0.05}|{weight ÷ 10g}
```

Distinct buckets number in the low thousands, so after a short warm-up the hit rate exceeds
95% and the model sees only ~10 requests/sec. Cache misses are coalesced into batches and
dispatched through a bounded worker pool.

Bucket granularity and pool size are configuration, not constants. This is an explicit,
documented accuracy-for-throughput trade: two bananas in the same bucket receive the same
classification.

### 6.2 Classification authority
The classifier's answers are authoritative for all routing and boxing decisions. The farm's
own `grade` and `ripeness` are treated as raw input — they still travel on every published
message for tracking, but they do not drive behaviour.

### 6.3 Routing
Evaluated in this order; first match wins:

1. `throwAway == true` → publish to `pastRipe`. **Applies even when the banana is golden.**
2. classified `grade == GOLDEN` → publish alone to `goldenBanana`. Never boxed.
3. otherwise → box it (§6.4).

### 6.4 Boxing
- Box identity is `(classified grade, classified ripeness score)`. Similar, not identical.
- Capacity is 20. A banana with no open matching box opens a new one.
- The timestamp of the **first** banana in a box is recorded on creation.
- A box that reaches 20 is published to `filledBox`, carrying the **sum of its bananas'
  prices**.
- A box not filled within **10 seconds** of its first banana has all of its contents
  published individually to `tooLongToFill`, and is discarded.

Box mutation is concurrent across consumer threads. "Append banana, and atomically return
the box if that append filled it" is executed as a Redis Lua script, making the read-modify-
write indivisible without a distributed lock.

A sweeper runs every 500ms to flush expired boxes.

### 6.5 Message envelope
Every message published to every topic carries, at minimum, the banana's `id`, `price` and
`farmOrigin`, so downstream consumers can attribute value and origin. `filledBox` carries
the full banana list plus `totalPrice`.

---

## 7. Messaging

Single topic exchange `banana` (durable). Routing keys:

| Routing key | Published by | Payload |
|---|---|---|
| `bananaProduction` | `farm` | `Banana` |
| `goldenBanana` | `factory` | `TrackedBanana` |
| `pastRipe` | `factory` | `TrackedBanana` |
| `tooLongToFill` | `factory` | `TrackedBanana` (one per banana) |
| `filledBox` | `factory` | `FilledBox` |

`factory` binds `bananaProduction`. `market` binds `#`. Queues are durable and quorum;
messages are persistent.

---

## 8. Service: `market`

Consumes every topic and aggregates metrics.

### 8.1 Required metrics
- Average price of banana boxes (`average-box-price`)
- Loss from past-due bananas (`past-due-loss`)
- Count of golden bananas produced (`golden-count`)

### 8.2 Stat registry
Metrics are not hardcoded into endpoints. Each is an `IStat` implementation — `Id`,
`Name`, `Unit`, `ComputeAsync` — auto-discovered into DI at startup. Adding a metric means
adding one class; the catalogue, the per-stat route, the per-farm route and the WebSocket
topic all pick it up with no further edits.

### 8.3 REST API
| Route | Returns |
|---|---|
| `GET /stats` | Catalogue: ids, names, units, links. **No values** — stays cheap as the list grows. |
| `GET /stats/{statId}` | `{ id, value, unit, asOf, sampleCount }` |
| `GET /stats/{statId}/by-farm` | The same stat broken down by `farmOrigin`. |
| `GET /health` | Liveness. |

Per-stat routes rather than one aggregate payload, so adding a metric never changes the
shape of an existing response.

### 8.4 WebSocket
`GET /stats/live` (upgrade). The client sends a subscription frame; the server pushes a
coalesced snapshot on an interval, skipping ticks where nothing changed.

```
→ { "subscribe": ["average-box-price", "golden-count"], "intervalSeconds": 5 }
← { "asOf": "2026-09-21T10:00:05Z", "stats": { "average-box-price": { ... }, ... } }
```

`intervalSeconds` is clamped to `[1, 300]`. Unknown stat ids are rejected in an error frame
listing the valid ids rather than silently ignored.

HTTP webhooks were designed and then dropped: the known consumer is a browser dashboard, for
which a WebSocket needs no registration, no retry/backoff machinery, no delivery-health
tracking, and no outbound-egress security surface.

---

## 9. Persistence seam

Redis is a deliberate placeholder. Every piece of state sits behind an interface registered
in DI, so replacing it is a registration change rather than a rewrite:

| Interface | Redis implementation | State |
|---|---|---|
| `IBoxRepository` | `RedisBoxRepository` | Open boxes, contents, creation time |
| `IClassificationCache` | `RedisClassificationCache` | Bucket → classification |
| `IMarketMetricsStore` | `RedisMarketMetricsStore` | Counters and running sums |
| `ClassifierBackend` (Python) | `LayaBackend` | — |

`IMarketMetricsStore` uses counters and running sums rather than storing raw events, so a
SQL implementation is a direct translation.

---

## 10. Testing

`xunit` for C#, `pytest` for Python. Coverage is targeted at logic that carries risk rather
than at a global percentage.

**Tested:**
- Distribution sampler over 100k draws: every invariant in §4.1, within statistical
  tolerance, plus per-origin conditional derivation and fail-fast on impossible weights.
- Price rules including the golden + over-ripe precedence case.
- Box manager: fills at exactly 20, `totalPrice` equals the sum of contents, expires at 10s
  against an injected clock, opens a new box when no matching box has space.
- Routing precedence, particularly golden + `throwAway` → `pastRipe`.
- Cache bucket key derivation.
- Market aggregators: average, loss accumulation, golden count, per-farm split.
- Stat registry discovery and WebSocket subscription frame validation.
- Classification service request/response contract against `StubBackend`.

**Not tested:** DI wiring, DTO serialization, health endpoints, Compose glue. These fail
loudly at startup rather than silently in production.

Randomness is injected via `IRandomSource` and time via `TimeProvider`, so every
distribution and timeout test is deterministic and fast.

---

## 11. Configuration

All tunables are environment variables with working defaults; `.env.example` documents them.
Notable:

| Variable | Default | Effect |
|---|---|---|
| `FARM__BASERATEPERSECOND` | `200` | Target mean generation rate. |
| `FARM__GOLDENPROBABILITY` | `0.001` | Golden rate. |
| `FARM__OVERRIPEPROBABILITY` | `0.035` | Global over-ripe rate. |
| `FACTORY__CLASSIFIERCONCURRENCY` | `5` | In-flight classification batches. |
| `FACTORY__BOXCAPACITY` | `20` | Bananas per box. |
| `FACTORY__BOXTIMEOUTSECONDS` | `10` | Unfilled box expiry. |
| `CLASSIFIER_BACKEND` | `laya` | `laya` or `stub`. |
| `CLASSIFIER_DEVICE` | `auto` | `auto`, `cpu`, `cuda`, `mps`. |

---

## 12. Known gaps

Accepted for this iteration, recorded so they are not mistaken for oversights:

1. **No authentication** on any endpoint, including the WebSocket.
2. **Bucketed classification is lossy** by design (§6.1). Two bananas in one bucket share a
   verdict.
3. **Redis is not durable.** A restart loses open boxes and all market metrics.
4. **No dead-letter queue.** A message that fails processing is logged and dropped.
5. **Classification throughput is the ceiling.** The cache makes 200/s viable; a sustained
   stream of genuinely novel bananas would not be.
