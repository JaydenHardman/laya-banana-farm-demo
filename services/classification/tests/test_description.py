"""
The natural-language rendering handed to Laya.

These assert the structure and wording the live model was tuned against. They do not invoke
the model, but every rule here was derived from probing it, and changing the wording without
re-probing is how classifications silently drift.
"""

from __future__ import annotations

import pytest

from app.description import (
    RIPENESS_APPEARANCE,
    describe_banana,
    describe_grade,
    describe_ripeness,
    describe_size,
)

OVERRIPE_THRESHOLD = 0.75


@pytest.mark.parametrize("ripeness", [0.0, 0.29, 0.30, 0.59, 0.75, 0.89, 0.90, 1.0])
def test_every_ripeness_maps_to_a_description(ripeness: float) -> None:
    assert describe_ripeness(ripeness).strip()


def test_ripeness_bands_are_distinct_and_ordered() -> None:
    bounds = [upper for upper, _ in RIPENESS_APPEARANCE]
    phrases = [phrase for _, phrase in RIPENESS_APPEARANCE]

    assert bounds == sorted(bounds)
    assert len(set(phrases)) == len(phrases)


def test_the_band_boundary_sits_at_the_overripe_threshold() -> None:
    # The farm calls a banana over-ripe above 0.75, so a band edge has to land there or the
    # description would straddle the distinction the routing rules depend on.
    assert OVERRIPE_THRESHOLD in [upper for upper, _ in RIPENESS_APPEARANCE]


def test_overripe_bananas_are_described_as_decayed() -> None:
    # "past its best" scored 0.01 on the throw-away question; concrete decay scores 0.75+.
    decay_words = ("collapsing", "splitting", "mushy", "leaking", "fermented", "gone off")

    for ripeness in (0.76, 0.85, 0.95, 1.0):
        description = describe_ripeness(ripeness)
        assert any(word in description for word in decay_words), description


def test_edible_bananas_are_not_described_as_decayed() -> None:
    for ripeness in (0.0, 0.2, 0.5, 0.74):
        description = describe_ripeness(ripeness)
        assert "gone off" not in description
        assert "mushy" not in description


def test_ordinary_bananas_avoid_superlatives() -> None:
    # Superlatives in the ripeness clause pushed the grade answer to GOLDEN for ordinary
    # bananas, which must remain a 1-in-1000 verdict.
    for ripeness in (0.4, 0.5, 0.7):
        description = describe_ripeness(ripeness)
        assert "at its best" not in description
        assert "flawless" not in description


def test_only_golden_is_described_as_flawless() -> None:
    assert "flawless" in describe_grade("Golden")

    for grade in ("A", "B", "C"):
        assert "flawless" not in describe_grade(grade)


def test_an_unknown_grade_still_produces_a_description() -> None:
    assert describe_grade("Platinum").strip()


@pytest.mark.parametrize(
    ("weight", "expected"),
    [(70.0, "small"), (99.9, "small"), (100.0, "average-sized"), (200.0, "large")],
)
def test_weight_maps_to_a_size_word(weight: float, expected: str) -> None:
    assert describe_size(weight) == expected


def test_decay_is_the_final_clause() -> None:
    """
    Clause order is load-bearing.

    The model weights the last clause most heavily. With the skin described last, a golden
    banana that had rotted scored throwAway=0.0001 and would have been routed to
    goldenBanana instead of pastRipe — the exact case the brief singles out. With decay last
    it scores 0.9641.
    """
    description = describe_banana("XFarm", 120.0, 0.95, "Golden")

    assert description.endswith(describe_ripeness(0.95) + ".")
    assert description.index(describe_grade("Golden")) < description.index(
        describe_ripeness(0.95)
    )


def test_the_description_mentions_farm_size_skin_and_condition() -> None:
    description = describe_banana("KindaCrazyNanas", 160.0, 0.5, "B")

    assert "KindaCrazyNanas" in description
    assert "large" in description
    assert describe_grade("B") in description
    assert describe_ripeness(0.5) in description


def test_no_raw_numbers_leak_into_the_description() -> None:
    # The whole point: the model cannot read "ripeness 0.950 on a scale where 1 is expired".
    description = describe_banana("XFarm", 118.4, 0.95, "C")

    assert not any(character.isdigit() for character in description), description
