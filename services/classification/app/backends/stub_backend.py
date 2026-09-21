"""Deterministic classifier used by the test suite."""

from __future__ import annotations

from app.models import BananaPayload, Classification

# Ripeness boundaries for the three ordinal levels the score primitive returns.
_UNDER_RIPE_CEILING = 0.35
_RIPE_CEILING = 0.75

# Above this ripeness the stub says to discard, matching the farm's over-ripe threshold.
_THROW_AWAY_RIPENESS = 0.75


class StubBackend:
    """
    Derives a classification from the banana's own fields, with no model.

    Exists so the API contract, the factory's routing and the box logic can all be tested
    without downloading weights or owning a GPU. It is never the production default.
    """

    name = "stub"

    def __init__(self) -> None:
        self._ready = False

    async def load(self) -> None:
        self._ready = True

    @property
    def ready(self) -> bool:
        return self._ready

    async def classify(self, bananas: list[BananaPayload]) -> list[Classification]:
        return [self._classify_one(banana) for banana in bananas]

    @staticmethod
    def _classify_one(banana: BananaPayload) -> Classification:
        if banana.ripeness < _UNDER_RIPE_CEILING:
            ripeness_level = 1
        elif banana.ripeness < _RIPE_CEILING:
            ripeness_level = 2
        else:
            ripeness_level = 3

        grade = "Golden" if banana.grade == "Golden" else banana.grade
        throw_away = 0.95 if banana.ripeness > _THROW_AWAY_RIPENESS else 0.02

        return Classification(
            grade=grade,
            gradeConfidence=0.99,
            ripenessLevel=ripeness_level,
            throwAway=throw_away,
        )
