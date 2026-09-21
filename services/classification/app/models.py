"""Request and response models for the classification API."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from app.description import describe_banana

FarmOrigin = Literal["XFarm", "TheBananaBoyz", "KindaCrazyNanas"]
HarvestGrade = Literal["A", "B", "C", "Golden"]
ClassifiedGrade = Literal["A", "B", "C", "Golden", "Other"]


class BananaPayload(BaseModel):
    """A banana as published by the farm service."""

    model_config = ConfigDict(populate_by_name=True)

    id: str
    date_harvested: str = Field(alias="dateHarvested")
    farm_origin: FarmOrigin = Field(alias="farmOrigin")
    weight: float
    ripeness: float = Field(ge=0.0, le=1.0)
    grade: HarvestGrade
    price: float

    def to_model_state(self) -> str:
        """
        Render the banana as the text state Laya reasons over.

        Deliberately qualitative. See :mod:`app.description` for why the numeric fields are
        translated into appearance rather than passed through.
        """
        return describe_banana(
            farm_origin=self.farm_origin,
            weight_grams=self.weight,
            ripeness=self.ripeness,
            grade=self.grade,
        )


class ClassifyRequest(BaseModel):
    """Classify a single banana."""

    banana: BananaPayload


class ClassifyBatchRequest(BaseModel):
    """Classify many bananas in one round-trip."""

    bananas: list[BananaPayload] = Field(min_length=1)


class Classification(BaseModel):
    """
    Laya's verdict on one banana.

    ``throw_away`` is the model's calibrated probability rather than a boolean: the decision
    threshold belongs to the caller, not to the classifier.
    """

    model_config = ConfigDict(populate_by_name=True, serialize_by_alias=True)

    grade: ClassifiedGrade
    grade_confidence: float = Field(alias="gradeConfidence", ge=0.0, le=1.0)
    ripeness_level: int = Field(alias="ripenessLevel", ge=1)
    throw_away: float = Field(alias="throwAway", ge=0.0, le=1.0)


class ClassifyResponse(BaseModel):
    """Result for a single banana."""

    model_config = ConfigDict(populate_by_name=True, serialize_by_alias=True)

    banana_id: str = Field(alias="bananaId")
    classification: Classification
    schema_version: str = Field(alias="schemaVersion")


class ClassifyBatchResponse(BaseModel):
    """Results for a batch, in the order the bananas were supplied."""

    model_config = ConfigDict(populate_by_name=True, serialize_by_alias=True)

    results: list[ClassifyResponse]


class HealthResponse(BaseModel):
    """Liveness and readiness payload."""

    model_config = ConfigDict(populate_by_name=True, serialize_by_alias=True)

    status: str
    backend: str
    model_ready: bool = Field(alias="modelReady")
    detail: str | None = None
