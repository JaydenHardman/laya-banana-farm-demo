"""
Renders a banana as the natural-language state Laya judges.

Laya is an encoder doing semantic judgment, not arithmetic. Handing it
``"Ripeness 0.950 on a scale where 0 is raw and 1 is expired"`` produced
``throwAway = 0.0004`` for a banana that is plainly rotten: the model cannot read a number
against a described scale. The same model given ``"black, mushy and leaking, smells
fermented"`` returns ``0.7966``.

So the numeric fields are translated into appearance before they reach the model. The bands
below are the interface between the farm's numbers and the model's semantics, and they are
where to look first if classifications drift.
"""

from __future__ import annotations

from typing import Final

# Upper bound (exclusive) -> how a banana at that ripeness looks and smells.
# 0.75 is the farm's over-ripe threshold, so the last two bands are the ones that should
# drive a throw-away verdict.
RIPENESS_APPEARANCE: Final[tuple[tuple[float, str], ...]] = (
    (0.30, "green along its edges and very firm, clearly not ready to eat yet"),
    # Deliberately not "at its best right now": superlatives here pushed the grade question
    # to GOLDEN for ordinary bananas, which must stay a 1-in-1000 answer.
    (0.60, "an even bright yellow and firm to the touch"),
    (0.75, "yellow with brown freckles spreading across it, very ripe and sweet"),
    # "past its best" alone scored 0.01 on the throw-away question; this wording scores 0.75.
    # The model responds to concrete decay, not to a judgement about quality.
    (0.90, "brown all over and collapsing, with the skin splitting open; it has gone off"),
    (1.01, "black, mushy and leaking, and it smells fermented"),
)

# Harvest grade -> the skin condition that grade implies. Describing the condition rather
# than naming the grade leaves the model something to judge instead of a label to repeat.
GRADE_APPEARANCE: Final[dict[str, str]] = {
    "Golden": "flawless and unusually beautiful, without a single mark",
    "A": "clean and unblemished",
    "B": "sound, with a few small blemishes",
    "C": "noticeably bruised and scarred",
}

# Upper bound (exclusive) in grams -> size word.
SIZE_APPEARANCE: Final[tuple[tuple[float, str], ...]] = (
    (100.0, "small"),
    (145.0, "average-sized"),
    (float("inf"), "large"),
)


def _band(value: float, bands: tuple[tuple[float, str], ...]) -> str:
    for upper, phrase in bands:
        if value < upper:
            return phrase
    return bands[-1][1]


def describe_ripeness(ripeness: float) -> str:
    """How a banana at this ripeness looks and smells."""
    return _band(ripeness, RIPENESS_APPEARANCE)


def describe_size(weight_grams: float) -> str:
    """Size word for a weight in grams."""
    return _band(weight_grams, SIZE_APPEARANCE)


def describe_grade(grade: str) -> str:
    """Skin condition implied by the harvest grade."""
    return GRADE_APPEARANCE.get(grade, "in unremarkable condition")


def describe_banana(
    farm_origin: str,
    weight_grams: float,
    ripeness: float,
    grade: str,
) -> str:
    """
    The full state string handed to the model.

    Clause order is load-bearing, not stylistic. The model weights the final clause most
    heavily, so the decay description must come last. With the skin last, a golden banana
    that has rotted scored ``throwAway = 0.0001`` — "flawless and unusually beautiful"
    drowned out "black, mushy and leaking". With decay last the same banana scores ``0.9641``
    while a healthy one stays at ``0.0000``.

    That case is not incidental: the brief requires a golden banana that should be discarded
    to reach ``pastRipe`` rather than ``goldenBanana``.
    """
    return (
        f"A {describe_size(weight_grams)} banana from the {farm_origin} farm. "
        f"The skin is {describe_grade(grade)}. "
        f"It is now {describe_ripeness(ripeness)}."
    )
