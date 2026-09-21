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


def test_the_banana_is_not_repeated_inside_the_instructions() -> None:
    """
    The state is passed to predict() separately; repeating it in the instruction collapsed
    the grade answer from GOLDEN (0.752 confidence) to A (0.161) and hid every golden
    banana. See build_questions' docstring.
    """
    built = build_questions("a very ripe banana")

    assert "a very ripe banana" not in built["grade"]["instructions"]
    assert built["grade"]["instructions"] == "What Grade is the banana?"


def test_no_placeholder_survives_into_the_built_questions() -> None:
    built = build_questions("a very ripe banana")

    for question in built.values():
        assert "{object}" not in question["instructions"]
        assert question["instructions"] == question["instructions"].strip()


def test_criteria_survive_the_build() -> None:
    built = build_questions("a banana")

    assert built["grade"]["criteria"] == QUESTIONS["grade"]["criteria"]
    assert built["ripeness"]["criteria"] == QUESTIONS["ripeness"]["criteria"]
    assert built["grade"]["type"] == "choice"


def test_building_questions_does_not_mutate_the_template() -> None:
    build_questions("first")
    build_questions("second")

    assert "{object}" in QUESTIONS["grade"]["instructions"]
    assert "first" not in QUESTIONS["grade"]["instructions"]


def test_the_throwaway_question_needs_no_placeholder() -> None:
    # It asks about the banana already described by the state, so it carries no {object}.
    assert "{object}" not in QUESTIONS["throwAway"]["instructions"]
