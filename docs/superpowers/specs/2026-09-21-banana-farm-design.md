# BananaFarm — Design Decision Log

**Date:** 2026-09-21
**Outcome:** approved → implementation

The full specification lives in [`SPEC.md`](../../../SPEC.md). This document records the
decisions taken during brainstorming and *why*, so later readers do not re-litigate them.

---

## Corrections to the original brief

Three things in the original request did not survive contact with the facts.

### 1. liteLLM cannot serve Laya

The brief asked for the classification service to "interface with a locally running model
called Laya through liteLLM".

Laya is not a generative LLM. It is a non-autoregressive encoder decision model —
ModernBERT-large (421M) for English, mmBERT-base (322M) multilingual — with a decision head
trained from scratch. It answers typed questions about a text state in a single forward pass
and returns calibrated probabilities. It ships as a Python SDK (`pip install laya`,
`laya.load()`, `agent.predict(state, questions)`) with no chat-completions endpoint and no
OpenAI-compatible server.

liteLLM is a proxy for generative chat APIs. There is nothing here for it to proxy. It was
removed entirely; the FastAPI service calls the SDK directly.

**Silver lining:** the `questions` schema in the brief — with its `choice` / `score` / `noul`
types — is Laya's own native question format, not an invention. `noul` is a real primitive
(calibrated binary probability), not a typo for `bool`. The brief was closer to correct than
the liteLLM framing suggested.

**Terminology note:** "layaAI typesafe model" conflates two products. TypeSafe is a hosted
System-One API whose flagship model is Jev. Laya is a separate open-source System-1 model
from Convai Innovations. Same architectural concept, different vendors. This system uses
Laya.

### 2. 200 bananas/sec cannot each be classified

Laya answers all three questions in one forward pass, so the unit cost is one inference per
banana, not three. But a Docker container on Apple Silicon has no Metal access, so inference
is CPU-only at roughly 300–450ms. Sustaining 200/sec would need 60–90 cores.

Generation at 200/sec is fine. Classification is the wall. Resolved with the bucketed Redis
cache in SPEC §6.1 — a deliberate, documented accuracy-for-throughput trade.

### 3. Two sources of truth for grade and ripeness

The banana carries `grade` and `ripeness` from the farm, and the classifier returns its own
values for both — and those two fields drive every routing and boxing decision.

Resolved: the classifier wins. Farm values are raw input, carried along for tracking.

---

## Decisions taken

| Question | Decision | Reasoning |
|---|---|---|
| Laya integration | `laya` SDK called directly; liteLLM dropped | Only integration the model actually supports. |
| Throughput | Full 200/s + bucketed cache + bounded worker pool | Hits the target rate while keeping the model inside its budget. |
| Truth source | Classifier authoritative | Matches "use the grade, ripeness and throwAway as metrics to group into boxes". |
| Scope | Working system + tests on risk-carrying logic | Full 80% coverage deferred; tests target distributions, boxing, pricing, aggregation. |
| Market API | Per-stat routes over a stat registry | "It may grow in the future" — adding a metric must not reshape existing responses. |
| Live consumption | WebSocket push | Known consumer is a future browser dashboard. |

### Dropped after design: HTTP webhooks

A full webhook subsystem was designed — registration API, HMAC-signed payloads, interval
coalescing, exponential-backoff retry, auto-disable on repeated failure, SSRF egress policy
— and then dropped when the consumer was confirmed to be a browser UI.

A WebSocket needs none of that machinery: no registration, no retries, no delivery-health
tracking, and no outbound request surface to secure. Should a *backend* consumer appear
later, webhooks are worth revisiting; the stat registry and interval dispatcher were built
so that they could be added without restructuring.

### Deferred: webhook egress security

The SSRF question (a service POSTing to caller-supplied URLs, inside a Docker network) was
raised and explicitly deferred. It is moot while there is no outbound egress. If webhooks
return, it returns with them. Recorded in SPEC §12.

---

## Smaller calls made without asking

- **`price` is `decimal`, not `float`.** It is money, summed per box and averaged across the
  market. Binary floating point would accumulate error in both. Serializes as a JSON number.
- **Golden + over-ripe:** golden price band (30–70), but still routes to `pastRipe`. Price
  and routing governed independently. The brief mandated the routing half explicitly.
- **Topic exchange, not RabbitMQ Streams.** At 200 msg/s the replay and throughput benefits
  of Streams buy nothing against real added complexity. Swappable later.
- **Question schema owned by the classification service,** not sent by callers. One place to
  tune prompts.
- **Single English checkpoint, not `Router(preload=True)`.** Bananas are not multilingual,
  and preloading three checkpoints would be wasteful in a 7.75GB Docker VM.
- **Unspecified distributions** (A/B/C grade split, weight, ripeness shape) given plausible
  configurable defaults rather than blocking on them. SPEC §4.3.
