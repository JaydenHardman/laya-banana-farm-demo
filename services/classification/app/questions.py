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
    Return the question set with ``{object}`` replaced by the banana's string form.

    A fresh dict is returned each call; the module-level ``QUESTIONS`` is never mutated.
    """
    built: dict[str, dict[str, Any]] = {}

    for key, question in QUESTIONS.items():
        instructions = question["instructions"].replace(_OBJECT_PLACEHOLDER, state)
        built[key] = {**question, "instructions": instructions}

    return built
