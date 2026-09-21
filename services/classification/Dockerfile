# syntax=docker/dockerfile:1

FROM python:3.12-slim AS base

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    # Weights land here, which compose maps to a named volume so they survive rebuilds.
    HF_HOME=/models

WORKDIR /app

RUN apt-get update \
 && apt-get install --no-install-recommends --yes curl \
 && rm -rf /var/lib/apt/lists/*

# Dependencies first: torch is large and this layer should not rebuild when app code changes.
COPY services/classification/requirements.txt services/classification/requirements-dev.txt ./
RUN pip install --no-cache-dir --requirement requirements-dev.txt

COPY services/classification/app ./app
COPY services/classification/tests ./tests
COPY services/classification/pytest.ini ./pytest.ini

RUN mkdir -p /models && chmod 777 /models

EXPOSE 8000

# Readiness, not liveness: 503 until the model is resident, so dependants wait for a warm model.
HEALTHCHECK --interval=10s --timeout=5s --start-period=300s --retries=60 \
    CMD curl --fail --silent http://localhost:8000/ready || exit 1

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
