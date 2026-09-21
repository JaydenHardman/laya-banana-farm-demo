"""The stub backend, which stands in for Laya throughout the test suite."""

from __future__ import annotations

import pytest

from app.backends.stub_backend import StubBackend
from app.models import BananaPayload
from tests.conftest import banana


async def classify_one(**overrides) -> object:
    backend = StubBackend()
    await backend.load()
    results = await backend.classify([BananaPayload(**banana(**overrides))])
    return results[0]


@pytest.mark.parametrize(
    ("ripeness", "expected_level"),
    [(0.1, 1), (0.5, 2), (0.9, 3)],
)
async def test_ripeness_maps_onto_three_ordinal_levels(
    ripeness: float,
    expected_level: int,
) -> None:
    assert (await classify_one(ripeness=ripeness)).ripeness_level == expected_level


async def test_an_overripe_banana_is_marked_for_disposal() -> None:
    assert (await classify_one(ripeness=0.9)).throw_away > 0.5


async def test_a_healthy_banana_is_kept() -> None:
    assert (await classify_one(ripeness=0.5)).throw_away < 0.5


async def test_a_golden_banana_keeps_its_grade() -> None:
    assert (await classify_one(grade="Golden")).grade == "Golden"


async def test_a_backend_is_not_ready_until_it_is_loaded() -> None:
    assert StubBackend().ready is False
