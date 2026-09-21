"""The classifier backend contract."""

from __future__ import annotations

from typing import Protocol, runtime_checkable

from app.models import BananaPayload, Classification


@runtime_checkable
class ClassifierBackend(Protocol):
    """
    A source of banana classifications.

    Implemented by the real Laya model and by a deterministic stub, so the API layer and its
    tests never depend on model weights being present.
    """

    name: str

    async def load(self) -> None:
        """Prepare the backend. Called once at startup; may be slow."""
        ...

    @property
    def ready(self) -> bool:
        """Whether the backend can serve requests."""
        ...

    async def classify(self, bananas: list[BananaPayload]) -> list[Classification]:
        """Classify a batch, returning one result per banana in the same order."""
        ...


def normalise_grade(raw: str) -> str:
    """
    Map a model answer onto the grade vocabulary the C# services expect.

    Laya returns the criterion key verbatim, so ``GOLDEN`` has to become ``Golden`` and an
    unrecognised answer has to degrade to ``Other`` rather than crash the pipeline.
    """
    lookup = {
        "GOLDEN": "Golden",
        "A": "A",
        "B": "B",
        "C": "C",
        "OTHER": "Other",
    }
    return lookup.get(raw.strip().upper(), "Other")
