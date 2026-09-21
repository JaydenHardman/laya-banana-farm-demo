"""The typed-question schema handed to Laya."""

from __future__ import annotations

from app.questions import QUESTIONS, build_questions


def test_the_three_primitives_are_declared() -> None:
    assert QUESTIONS["grade"]["type"] == "choice"
    assert QUESTIONS["ripeness"]["type"] == "score"
    # `noul` is Laya's binary primitive, returning a calibrated probability.
    assert QUESTIONS["throwAway"]["type"] == "noul"


def test_grade_criteria_cover_the_full_vocabulary() -> None:
    assert set(QUESTIONS["grade"]["criteria"]) == {"GOLDEN", "A", "B", "C", "OTHER"}


def test_the_object_placeholder_is_replaced_with_the_banana() -> None:
    built = build_questions("a very ripe banana")

    assert "a very ripe banana" in built["grade"]["instructions"]
    assert "{object}" not in built["grade"]["instructions"]
    assert "{object}" not in built["ripeness"]["instructions"]


def test_building_questions_does_not_mutate_the_template() -> None:
    build_questions("first")
    build_questions("second")

    assert "{object}" in QUESTIONS["grade"]["instructions"]
    assert "first" not in QUESTIONS["grade"]["instructions"]


def test_the_throwaway_question_needs_no_placeholder() -> None:
    # It asks about the banana already described by the state, so it carries no {object}.
    assert "{object}" not in QUESTIONS["throwAway"]["instructions"]
