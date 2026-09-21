"""The real classifier: the Laya System-1 decision model."""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Any

from app.backends.base import normalise_grade
from app.models import BananaPayload, Classification
from app.questions import build_questions
from app.settings import Settings

logger = logging.getLogger(__name__)

# Number of ordinal levels in the ripeness `score` question's criteria.
_RIPENESS_LEVELS = 3


class LayaBackend:
    """
    Wraps ``laya.load()`` and ``agent.predict()``.

    Laya is a non-autoregressive encoder model with a decision head, not a generative LLM: it
    answers all three typed questions in a single forward pass and returns calibrated
    probabilities. There is no chat-completions surface to proxy, so the SDK is called
    directly rather than through an LLM gateway.
    """

    name = "laya"

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._agent: Any | None = None
        self._semaphore = asyncio.Semaphore(settings.max_concurrency)

    @property
    def ready(self) -> bool:
        return self._agent is not None

    async def load(self) -> None:
        """
        Load the checkpoint. Blocking and slow on first run (weights are downloaded), so it
        runs off the event loop and readiness stays false until it completes.
        """
        logger.info(
            "Loading Laya checkpoint %s (subfolder=%s) on device=%s",
            self._settings.model_id,
            self._settings.model_subfolder or "<default>",
            self._settings.device,
        )

        self._agent = await asyncio.to_thread(self._load_blocking)

        logger.info(
            "Laya ready; serving up to %d concurrent inferences.",
            self._settings.max_concurrency,
        )

    def _load_blocking(self) -> Any:
        import laya  # imported lazily so the stub backend needs no torch install

        kwargs: dict[str, Any] = {}

        if self._settings.model_subfolder:
            kwargs["subfolder"] = self._settings.model_subfolder

        # 'auto' means let Laya detect the best available device itself.
        if self._settings.device != "auto":
            kwargs["device"] = self._settings.device

        return laya.load(self._settings.model_id, **kwargs)

    async def classify(self, bananas: list[BananaPayload]) -> list[Classification]:
        if self._agent is None:
            raise RuntimeError(
                "Laya is not loaded yet. Wait for GET /ready to return 200 before "
                "sending classification requests."
            )

        async with self._semaphore:
            predictions = await asyncio.to_thread(self._predict_blocking, bananas)

        classifications = []

        for banana, (state, raw, elapsed_ms) in zip(bananas, predictions, strict=True):
            classification = _to_classification(raw)

            if self._settings.log_verdicts:
                # Only cache misses get this far, so one pair of lines per inference stays
                # readable even while the farm is running at full rate.
                short_id = banana.id[:8]
                logger.info("laya <- [%s] %s", short_id, state)
                logger.info(
                    "laya -> [%s] grade=%s conf=%.2f ripeness=%d throwAway=%.4f (%.0f ms)",
                    short_id,
                    classification.grade,
                    classification.grade_confidence,
                    classification.ripeness_level,
                    classification.throw_away,
                    elapsed_ms,
                )

            classifications.append(classification)

        return classifications

    def _predict_blocking(
        self,
        bananas: list[BananaPayload],
    ) -> list[tuple[str, dict[str, Any], float]]:
        assert self._agent is not None  # guarded by the caller
        results = []

        for banana in bananas:
            state = banana.to_model_state()
            started = time.perf_counter()
            raw = self._agent.predict(state, build_questions(state))
            results.append((state, raw, (time.perf_counter() - started) * 1000))

        return results


def _answers_of(result: dict[str, Any]) -> dict[str, Any]:
    answers = result.get("answers")

    if not isinstance(answers, dict):
        raise ValueError(
            "Laya returned no 'answers' object. Got keys: "
            f"{sorted(result.keys()) if isinstance(result, dict) else type(result).__name__}."
        )

    return answers


def _answer_value(answers: dict[str, Any], question: str, primitive: str) -> Any:
    """
    Pull one answer out of Laya's response.

    The value lives under the primitive's own name (``choice``, ``score``, ``noul``). A
    couple of generic fallbacks are accepted so a minor SDK revision does not take the
    pipeline down, and anything else raises with the shape that actually arrived.
    """
    answer = answers.get(question)

    if not isinstance(answer, dict):
        raise ValueError(
            f"Laya returned no answer for question {question!r}. "
            f"Present questions: {sorted(answers.keys())}."
        )

    for key in (primitive, "value", "answer"):
        if key in answer:
            return answer[key]

    raise ValueError(
        f"Answer for {question!r} has no {primitive!r} value. Keys present: "
        f"{sorted(answer.keys())}."
    )


def _confidence_of(answers: dict[str, Any], question: str) -> float:
    answer = answers.get(question)

    if not isinstance(answer, dict):
        return 0.0

    raw = answer.get("confidence", answer.get("probability", 0.0))

    try:
        return max(0.0, min(1.0, float(raw)))
    except (TypeError, ValueError):
        return 0.0


def _to_classification(result: dict[str, Any]) -> Classification:
    answers = _answers_of(result)

    grade = normalise_grade(str(_answer_value(answers, "grade", "choice")))
    raw_ripeness = _answer_value(answers, "ripeness", "score")
    raw_throw_away = _answer_value(answers, "throwAway", "noul")

    # The score primitive returns an expected level over the criteria, which can be
    # fractional. Rounding to the nearest level keeps box identity discrete.
    ripeness_level = int(round(float(raw_ripeness)))
    ripeness_level = max(1, min(_RIPENESS_LEVELS, ripeness_level))

    throw_away = max(0.0, min(1.0, float(raw_throw_away)))

    return Classification(
        grade=grade,
        gradeConfidence=_confidence_of(answers, "grade"),
        ripenessLevel=ripeness_level,
        throwAway=throw_away,
    )
