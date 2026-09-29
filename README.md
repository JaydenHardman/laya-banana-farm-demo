# BananaFarm

## Project description

A set of four services that generate synthetic banana data, classify each banana with the
Laya decision model, pack them into boxes, and total up prices and losses. The services
communicate over RabbitMQ and keep their state in Redis. A web dashboard displays the totals.

---

## Startup

Requires Docker.

```bash
git clone https://github.com/JaydenHardman/laya-banana-farm-demo.git
```

```bash
cd laya-banana-farm-demo
```

```bash
docker compose up --build
```

Dashboard: <http://localhost:8083>
