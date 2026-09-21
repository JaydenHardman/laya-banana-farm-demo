"""Translation of Laya's raw response into the contract the C# services consume."""

from __future__ import annotations

import pytest

from app.backends.base import normalise_grade
from app.backends.laya_backend import _to_classification


def raw(grade: str = "A", score: float = 2.0, noul: float = 0.1) -> dict:
    return {
        "answers": {
            "grade": {"choice": grade, "confidence": 0.88},
            "ripeness": {"score": score},
            "throwAway": {"noul": noul},
        }
    }


def test_a_complete_response_is_translated() -> None:
    result = _to_classification(raw(grade="GOLDEN", score=3.0, noul=0.92))

    assert result.grade == "Golden"
    assert result.ripeness_level == 3
    assert result.throw_away == pytest.approx(0.92)
    assert result.grade_confidence == pytest.approx(0.88)


def test_a_fractional_score_rounds_to_a_discrete_level() -> None:
    # Box identity is discrete, so an expected level of 2.4 has to become a level.
    assert _to_classification(raw(score=2.4)).ripeness_level == 2
    assert _to_classification(raw(score=2.6)).ripeness_level == 3


def test_a_score_outside_the_criteria_is_clamped() -> None:
    assert _to_classification(raw(score=0.0)).ripeness_level == 1
    assert _to_classification(raw(score=9.0)).ripeness_level == 3


def test_an_unrecognised_grade_degrades_to_other() -> None:
    assert _to_classification(raw(grade="SPLENDID")).grade == "Other"


def test_grade_normalisation_is_case_insensitive() -> None:
    assert normalise_grade("golden") == "Golden"
    assert normalise_grade(" a ") == "A"
    assert normalise_grade("") == "Other"


def test_a_missing_answers_object_raises_with_the_shape_that_arrived() -> None:
    with pytest.raises(ValueError, match="no 'answers' object"):
        _to_classification({"routing": {"model": "english"}})


def test_a_missing_question_names_what_was_present() -> None:
    payload = {"answers": {"grade": {"choice": "A"}}}

    with pytest.raises(ValueError, match="ripeness"):
        _to_classification(payload)


def test_a_missing_confidence_does_not_fail_the_banana() -> None:
    payload = {
        "answers": {
            "grade": {"choice": "B"},
            "ripeness": {"score": 2},
            "throwAway": {"noul": 0.1},
        }
    }

    assert _to_classification(payload).grade_confidence == 0.0
