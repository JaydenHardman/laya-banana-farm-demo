"""The HTTP contract the factory depends on."""

from __future__ import annotations

from fastapi.testclient import TestClient

from tests.conftest import banana


def test_health_reports_the_backend(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json()["backend"] == "stub"


def test_ready_succeeds_once_the_backend_is_loaded(client: TestClient) -> None:
    response = client.get("/ready")

    assert response.status_code == 200
    assert response.json()["modelReady"] is True


def test_classify_answers_every_question(client: TestClient) -> None:
    response = client.post("/classify", json={"banana": banana()})

    assert response.status_code == 200
    body = response.json()

    assert body["bananaId"] == banana()["id"]
    assert set(body["classification"]) == {
        "grade",
        "gradeConfidence",
        "ripenessLevel",
        "throwAway",
    }


def test_classify_batch_answers_in_the_order_supplied(client: TestClient) -> None:
    ids = ["a" * 8, "b" * 8, "c" * 8]

    response = client.post(
        "/classify/batch",
        json={"bananas": [banana(id=identifier) for identifier in ids]},
    )

    assert response.status_code == 200
    assert [result["bananaId"] for result in response.json()["results"]] == ids


def test_an_oversized_batch_is_rejected_with_a_usable_message(client: TestClient) -> None:
    response = client.post(
        "/classify/batch",
        json={"bananas": [banana() for _ in range(200)]},
    )

    assert response.status_code == 413
    assert "CLASSIFIER_MAX_BATCH_SIZE" in response.json()["detail"]


def test_an_empty_batch_is_rejected(client: TestClient) -> None:
    assert client.post("/classify/batch", json={"bananas": []}).status_code == 422


def test_ripeness_outside_the_unit_interval_is_rejected(client: TestClient) -> None:
    response = client.post("/classify", json={"banana": banana(ripeness=1.4)})

    assert response.status_code == 422


def test_the_question_schema_is_published(client: TestClient) -> None:
    body = client.get("/questions").json()

    assert set(body["questions"]) == {"grade", "ripeness", "throwAway"}
    assert body["questions"]["throwAway"]["type"] == "noul"
