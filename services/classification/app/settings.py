"""Configuration for the classification service, read from the environment."""

from __future__ import annotations

import os
from dataclasses import dataclass
from functools import lru_cache


def _int_env(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    try:
        return int(raw)
    except ValueError as exc:
        raise ValueError(f"{name} must be an integer, got {raw!r}") from exc


@dataclass(frozen=True)
class Settings:
    """Immutable runtime settings."""

    backend: str
    """Which classifier to load: 'laya' for the real model, 'stub' for the test double."""

    model_id: str
    """HuggingFace repository holding the Laya checkpoints."""

    model_subfolder: str | None
    """Checkpoint within the repository. None selects the default English checkpoint."""

    device: str
    """'auto', 'cpu', 'cuda' or 'mps'. 'auto' lets Laya pick."""

    max_concurrency: int
    """Upper bound on simultaneous inferences. Laya is CPU-bound and holds the GIL."""

    max_batch_size: int
    """Largest batch accepted by /classify/batch."""

    log_verdicts: bool
    """
    Log the state sent to the model and the answers that came back, one pair per inference.
    Only cache misses reach the model, so this stays readable at production rates.
    """


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Load settings once per process."""
    subfolder = os.getenv("CLASSIFIER_MODEL_SUBFOLDER", "").strip()
    default_concurrency = max(1, (os.cpu_count() or 2) - 1)

    backend = os.getenv("CLASSIFIER_BACKEND", "laya").strip().lower()
    if backend not in {"laya", "stub"}:
        raise ValueError(
            f"CLASSIFIER_BACKEND must be 'laya' or 'stub', got {backend!r}."
        )

    return Settings(
        backend=backend,
        model_id=os.getenv("CLASSIFIER_MODEL_ID", "convaiinnovations/laya").strip(),
        model_subfolder=subfolder or None,
        device=os.getenv("CLASSIFIER_DEVICE", "auto").strip().lower(),
        max_concurrency=_int_env("CLASSIFIER_MAX_CONCURRENCY", default_concurrency),
        max_batch_size=_int_env("CLASSIFIER_MAX_BATCH_SIZE", 64),
        log_verdicts=os.getenv("CLASSIFIER_LOG_VERDICTS", "true").strip().lower()
        not in {"0", "false", "no"},
    )
