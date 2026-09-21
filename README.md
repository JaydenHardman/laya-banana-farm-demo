# BananaFarm

Four microservices that generate synthetic banana harvest data at ~200 items/second,
classify it with a locally-running decision model, sort it into boxes, and aggregate market
metrics.

```bash
docker compose up --build
```

The full specification is in [SPEC.md](SPEC.md); the design decisions and the reasoning
behind them are in
[docs/superpowers/specs/2026-09-21-banana-farm-design.md](docs/superpowers/specs/2026-09-21-banana-farm-design.md).

---

## The services

| Service | Stack | Role |
|---|---|---|
| **farm** | C# / .NET 10 | Generates bananas in unpredictable waves and publishes them. |
| **factory** | C# / .NET 10 | Classifies, routes and boxes them. |
| **classification** | Python 3.12 / FastAPI | Wraps the Laya decision model. |
| **market** | C# / .NET 10 | Aggregates prices, losses and golden counts. |

Plus RabbitMQ (one topic exchange, `banana`) and Redis (boxes, classification cache, market
counters).

```
farm ──bananaProduction──▶ factory ──▶ classification (REST)
                              │
                              ├── goldenBanana
                              ├── pastRipe
                              ├── tooLongToFill
                              └── filledBox ──▶ market
```

---

## First run

The classification image installs torch and the `laya` SDK, and downloads ~1.7GB of weights
on first start. The first `docker compose up --build` therefore takes a while; later ones do
not, because the HuggingFace cache lives in a named volume.

Everything downstream waits on the model's readiness healthcheck, so nothing sends traffic to
a cold classifier.

To pre-warm the weights before starting the stack:

```bash
docker compose run --rm classification python -c "import laya; laya.load('convaiinnovations/laya')"
```

To watch it come up:

```bash
docker compose logs -f farm factory market
```

### Running without the model

The stub backend answers from the banana's own fields — no weights, no inference — which is
the fastest way to exercise the whole pipeline:

```bash
CLASSIFIER_BACKEND=stub docker compose up --build
```

### Running the model on your host (faster)

Containers on Apple Silicon have no Metal access, so in-container inference is CPU-only at
roughly 300–450ms per banana. Your host can use the GPU.

```bash
python3 -m venv .venv && source .venv/bin/activate && pip install -r services/classification/requirements.txt
```

```bash
CLASSIFIER_DEVICE=mps uvicorn app.main:app --app-dir services/classification --port 8000
```

```bash
docker compose -f docker-compose.yml -f docker-compose.host-model.yml up --build
```

If `mps` fails, use `CLASSIFIER_DEVICE=cpu` — host CPU is still faster than the container.

---

## The dashboard

<http://localhost:8083> — served by the market service itself, so it shares an origin with
the API and the WebSocket. No CORS, no extra container, still one `compose up`.

Stat tiles, a time series per metric, and a per-farm breakdown, all fed by the WebSocket at
a 2s interval. It reads `/stats` and renders whatever is advertised, so a metric added to
the market service appears with no change to the page.

Light and dark are separately-chosen palettes rather than an inverted flip, farms keep a
fixed colour so a farm never changes hue as values move, and every bar carries a direct
value label. There is a table view behind the **show** toggle for the same numbers as text.

## Poking at it

```bash
curl -s localhost:8083/stats | jq
```

```bash
curl -s localhost:8083/stats/average-box-price | jq
```

```bash
curl -s localhost:8083/stats/past-due-loss/by-farm | jq
```

```bash
curl -s localhost:8082/stats | jq
```

`localhost:8082/stats` is the factory's classification cache hit rate and open box count —
the quickest way to see whether the cache is doing its job.

Live stats over WebSocket:

```bash
websocat ws://localhost:8083/stats/live
```

then send:

```json
{"subscribe": ["average-box-price", "golden-count"], "intervalSeconds": 5}
```

Snapshots arrive on the interval, and ticks where nothing moved are skipped. Omit
`subscribe` to receive every stat.

The RabbitMQ management UI is at <http://localhost:15672> (guest/guest).

---

## Adding a market stat

Metrics are discovered from DI, so this is the whole job:

```csharp
public sealed class WastedWeightStat : MetricStat
{
    public override string Id => "wasted-weight";
    public override string Name => "Wasted weight";
    public override string Unit => "grams";
    public override string Description => "Total weight of discarded bananas.";
    protected override string Metric => MarketMetrics.PastRipeLoss;
    protected override StatAggregation Aggregation => StatAggregation.Total;
}
```

Register it with `builder.Services.AddSingleton<IStat, WastedWeightStat>();`. The catalogue,
`/stats/{id}`, `/stats/{id}/by-farm` and the WebSocket all pick it up.

---

## Tests

```bash
dotnet test --solution BananaFarm.slnx
```

```bash
docker compose run --rm classification pytest
```

89 C# tests and 28 Python tests. The C# suite covers the distribution invariants, pricing
rules, routing precedence, box fill/timeout semantics and market aggregation. The Python
suite covers the API contract, the question schema and the translation of Laya's raw output.
Both run against deterministic doubles, so neither needs model weights, a broker, or Redis.

`global.json` pins the SDK and selects Microsoft.Testing.Platform, which is how `dotnet test`
runs xunit.v3 projects on .NET 10. If you have no local SDK, the same suite runs in a
container:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --solution BananaFarm.slnx
```

---

## Configuration

Copy `.env.example` to `.env`. Notable knobs:

| Variable | Default | Effect |
|---|---|---|
| `FARM_BASE_RATE` | `200` | Mean bananas per second. |
| `CLASSIFIER_BACKEND` | `laya` | `laya` or `stub`. |
| `CLASSIFIER_DEVICE` | `auto` | `auto`, `cpu`, `cuda`, `mps`. |
| `FACTORY_CACHE_RIPENESS_BUCKET` | `0.05` | Classification cache granularity. |
| `FACTORY_BOX_TIMEOUT_SECONDS` | `10` | Unfilled box expiry. |

---

## Things worth knowing

**Classification is bucketed, and that is deliberate.** Laya costs 300–450ms per banana on
CPU, which cannot sustain 200/second. Bananas are quantised into buckets (farm, grade,
ripeness band, weight band) and results are cached per bucket, so after warm-up the model
sees only novel shapes. Two bananas in the same bucket get the same verdict. Narrow
`FACTORY_CACHE_*` for accuracy, widen for throughput.

**The classifier's verdict wins.** Bananas carry `grade` and `ripeness` from the farm, but
routing and boxing use the classifier's answers. The farm's values ride along for tracking.

**A golden banana that is past ripe goes to `pastRipe`.** Disposal is checked before golden.
It keeps the golden price band (30–70) either way — price and routing are independent.

**Redis is a placeholder.** Boxes, the cache and market counters each sit behind an interface
registered in DI; swapping in a durable store is a registration change. Nothing survives a
restart today.

**There is no authentication** on any endpoint, including the WebSocket. See SPEC §12 for the
full list of accepted gaps.
