"""Classifier backends: the real Laya model, and a deterministic stub for tests."""

from __future__ import annotations

from app.backends.base import ClassifierBackend
from app.backends.stub_backend import StubBackend
from app.settings import Settings


def create_backend(settings: Settings) -> ClassifierBackend:
    """Build the backend named by ``CLASSIFIER_BACKEND``."""
    if settings.backend == "stub":
        return StubBackend()

    # Imported lazily: the stub path must not require torch to be installed.
    from app.backends.laya_backend import LayaBackend

    return LayaBackend(settings)


__all__ = ["ClassifierBackend", "StubBackend", "create_backend"]
