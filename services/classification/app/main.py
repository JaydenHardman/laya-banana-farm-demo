"""FastAPI surface for the classification service."""

from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager
from typing import AsyncIterator

from fastapi import FastAPI, HTTPException, Request, status

from app.backends import ClassifierBackend, create_backend
from app.models import (
    ClassifyBatchRequest,
    ClassifyBatchResponse,
    ClassifyRequest,
    ClassifyResponse,
    HealthResponse,
)
from app.questions import QUESTIONS, SCHEMA_VERSION
from app.settings import get_settings

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)-8s %(name)s | %(message)s",
)
logger = logging.getLogger("classification")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    """
    Load the model in the background.

    Loading is deliberately not awaited here: the process must start serving /ready
    immediately so its healthcheck can report "not ready yet" rather than the container
    looking dead while weights download.
    """
    settings = get_settings()
    backend = create_backend(settings)
    app.state.backend = backend
    app.state.load_error = None

    async def _load() -> None:
        try:
            await backend.load()
        except Exception as exc:  # noqa: BLE001 - surfaced through /ready
            app.state.load_error = str(exc)
            logger.exception("Classifier backend %s failed to load.", backend.name)

    task = asyncio.create_task(_load())

    try:
        yield
    finally:
        task.cancel()


app = FastAPI(
    title="BananaFarm Classification",
    version=SCHEMA_VERSION,
    summary="Typed banana judgments from the Laya System-1 decision model.",
    lifespan=lifespan,
)


def _backend(request: Request) -> ClassifierBackend:
    backend: ClassifierBackend = request.app.state.backend

    if not backend.ready:
        detail = request.app.state.load_error or "Classifier is still loading."
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=detail,
        )

    return backend


@app.get("/health", response_model=HealthResponse)
async def health(request: Request) -> HealthResponse:
    """Liveness. Returns 200 as soon as the process is up, model or not."""
    backend: ClassifierBackend = request.app.state.backend
    return HealthResponse(
        status="healthy",
        backend=backend.name,
        modelReady=backend.ready,
        detail=request.app.state.load_error,
    )


@app.get("/ready", response_model=HealthResponse)
async def ready(request: Request) -> HealthResponse:
    """
    Readiness. 503 until the model is resident, so the factory's ``depends_on`` gate holds
    traffic back from a cold model.
    """
    backend: ClassifierBackend = request.app.state.backend
    error = request.app.state.load_error

    if not backend.ready:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=error or "Classifier is still loading.",
        )

    return HealthResponse(
        status="ready",
        backend=backend.name,
        modelReady=True,
        detail=None,
    )


@app.get("/questions")
async def questions() -> dict[str, object]:
    """The typed-question schema this service asks. Exposed for inspection and debugging."""
    return {"schemaVersion": SCHEMA_VERSION, "questions": QUESTIONS}


@app.post("/classify", response_model=ClassifyResponse)
async def classify(payload: ClassifyRequest, request: Request) -> ClassifyResponse:
    """Classify a single banana."""
    backend = _backend(request)
    results = await backend.classify([payload.banana])

    return ClassifyResponse(
        bananaId=payload.banana.id,
        classification=results[0],
        schemaVersion=SCHEMA_VERSION,
    )


@app.post("/classify/batch", response_model=ClassifyBatchResponse)
async def classify_batch(
    payload: ClassifyBatchRequest,
    request: Request,
) -> ClassifyBatchResponse:
    """
    Classify many bananas in one round-trip. This is the factory's normal path: batching
    amortises HTTP overhead across the cache misses collected in a single window.
    """
    settings = get_settings()

    if len(payload.bananas) > settings.max_batch_size:
        raise HTTPException(
            status_code=status.HTTP_413_CONTENT_TOO_LARGE,
            detail=(
                f"Batch of {len(payload.bananas)} exceeds the limit of "
                f"{settings.max_batch_size}. Split the request or raise "
                "CLASSIFIER_MAX_BATCH_SIZE."
            ),
        )

    backend = _backend(request)
    classifications = await backend.classify(payload.bananas)

    return ClassifyBatchResponse(
        results=[
            ClassifyResponse(
                bananaId=banana.id,
                classification=classification,
                schemaVersion=SCHEMA_VERSION,
            )
            for banana, classification in zip(payload.bananas, classifications, strict=True)
        ]
    )
