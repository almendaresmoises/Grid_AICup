"""Resolve a canonical dataset's Google Cloud Storage output from the Grid API."""

from __future__ import annotations

import requests

from workers.common.config import get_backend_api_url, get_worker_runner_secret


def resolve_output_uri(dataset_code: str) -> str:
    response = requests.get(
        f"{get_backend_api_url()}/api/datasets/by-code/{dataset_code}/output-location",
        headers={"X-Grid-Worker-Secret": get_worker_runner_secret()},
        timeout=30,
    )
    response.raise_for_status()
    payload = response.json()
    uri = payload.get("resolvedOutputUri")
    if not isinstance(uri, str) or not uri.startswith("gs://"):
        raise ValueError(f"Dataset '{dataset_code}' did not return a Cloud Storage output URI")
    return uri
