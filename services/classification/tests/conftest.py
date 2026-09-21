"""Shared fixtures. Every test runs against the stub backend, so none need model weights."""

from __future__ import annotations

import os
from typing import Any, Iterator

import pytest

os.environ.setdefault("CLASSIFIER_BACKEND", "stub")

from fastapi.testclient import TestClient  # noqa: E402

from app.main import app  # noqa: E402
from app.settings import get_settings  # noqa: E402


@pytest.fixture(autouse=True)
def _stub_backend(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CLASSIFIER_BACKEND", "stub")
    get_settings.cache_clear()


@pytest.fixture
def client() -> Iterator[TestClient]:
    """A client whose lifespan has run, so the stub backend is loaded."""
    with TestClient(app) as test_client:
        yield test_client


def banana(**overrides: Any) -> dict[str, Any]:
    """A banana payload in the shape the farm publishes."""
    payload = {
        "id": "8f14e45f-ceea-467a-9f0a-1c2d3e4f5a6b",
        "dateHarvested": "2026-09-21T10:00:00+00:00",
        "farmOrigin": "XFarm",
        "weight": 120.0,
        "ripeness": 0.5,
        "grade": "B",
        "price": 7.5,
    }
    payload.update(overrides)
    return payload
