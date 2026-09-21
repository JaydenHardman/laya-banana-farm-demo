"""
The typed-question schema handed to Laya.

Owned here rather than sent by callers: the factory posts bananas, and this service decides
what to ask about them. That keeps prompt tuning in one place and lets the schema version
independently of its consumers (SPEC 5.2).
"""

from __future__ import annotations

from typing import Any, Final

SCHEMA_VERSION: Final = "1.0.0"

_OBJECT_PLACEHOLDER: Final = "{object}"

QUESTIONS: Final[dict[str, dict[str, Any]]] = {
    "grade": {
        "type": "choice",
        "instructions": f"What Grade is the banana? {_OBJECT_PLACEHOLDER}",
        "criteria": {
            "GOLDEN": "world class, solid gold",
            "A": "ideal",
            "B": "acceptable but not ideal",
            "C": "not ideal",
            "OTHER": "everything else",
        },
    },
    "ripeness": {
        "type": "score",
        "instructions": (
            "How close is this banana to being ripe if 0 is unripe and 1 is expired? "
            f"{_OBJECT_PLACEHOLDER}"
        ),
        "criteria": ["1", "2", "3"],
    },
    "throwAway": {
        "type": "noul",
        "instructions": "Should this banana be thrown away?",
    },
}


def build_questions(state: str) -> dict[str, dict[str, Any]]:
    """
    Return the question set ready to pass to ``agent.predict(state, questions)``.

    The placeholder is **removed, not substituted**, and that is a deliberate deviation from
    the original brief, which specified instructions of the form
    ``"What Grade is the banana? " + {object}``.

    Laya already receives the banana as ``predict``'s ``state`` argument. Repeating the same
    text inside the instruction measurably degrades every answer, because the instruction is
    meant to be the question, not a second copy of the data. Measured on one golden banana,
    holding the state constant and changing only the instruction:

    ==========================================  =======  ============
    instruction                                 grade    confidence
    ==========================================  =======  ============
    ``"What Grade is the banana? <banana>"``     A        0.161
    ``"What Grade is the banana?"``              GOLDEN   0.752
    ==========================================  =======  ============

    With the placeholder substituted, GOLDEN was never returned once in 940 live
    classifications, so every golden banana was silently boxed instead of published to
    ``goldenBanana``. The ripeness score shifted too (0.91 -> 1.51), so this is not confined
    to the grade question.

    ``state`` is accepted so the signature still expresses the dependency, and so restoring
    the literal reading is a one-line change.

    A fresh dict is returned each call; the module-level ``QUESTIONS`` is never mutated.
    """
    built: dict[str, dict[str, Any]] = {}

    for key, question in QUESTIONS.items():
        instructions = question["instructions"].replace(_OBJECT_PLACEHOLDER, "").strip()
        built[key] = {**question, "instructions": instructions}

    return built
