"""Pub/Sub consumer for manual parser uploads."""

from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path
from typing import Any

import requests
from google.api_core.exceptions import AlreadyExists
from google.cloud import pubsub_v1

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from workers.common.config import get_backend_api_url, get_worker_runner_secret
from workers.common.logger import get_worker_logger
from workers.etl_worker.core.base_job import ExecutionContext
from workers.etl_worker.core.storage_provider import StorageProvider
from workers.etl_worker.jobs.parsers.v2_pipeline_runner import execute_v2

logger = get_worker_logger("etl.pubsub")


def process_payload(data: dict[str, Any]) -> None:
    run_id = str(data["run_id"])
    pipeline = data.get("pipeline")
    if not isinstance(pipeline, dict):
        raise ValueError("Parser message is missing a pipeline object")
    input_uri = str(data["input_uri"])
    output_uri = str(data["output_uri"])
    if not get_worker_runner_secret():
        raise ValueError("Set GRID_WORKER_SECRET so parser results can be reported to the API")
    storage = StorageProvider()
    suffix = Path(str(data.get("file_name") or input_uri)).suffix
    with tempfile.TemporaryDirectory() as directory:
        local_input = str(Path(directory) / f"input{suffix}")
        Path(local_input).write_bytes(storage.read_file(input_uri))
        context = ExecutionContext(
            task_id=run_id,
            file_path=input_uri,
            input_location=local_input,
            output_location=output_uri,
            pipeline_definition=pipeline,
            pipeline_revision=data.get("pipeline_revision"),
            pipeline_hash=data.get("pipeline_hash"),
            execution_mode="full",
            parameters={"source_file": data.get("file_name") or input_uri, "output_contract": data.get("output_contract", [])},
        )
        try:
            execute_v2(context, storage)
            input_rows = context._parser_report.get("input_rows", 0) if context._parser_report else 0
            if context._rows_processed == 0 and input_rows > 0:
                raise ValueError("The parser produced no usable rows. Review the mappings and quarantined records.")
        except BaseException as error:
            if isinstance(error, (KeyboardInterrupt, SystemExit)):
                raise
            logger.exception("Parser execution failed: %s", error)
            _report(run_id, "Failed", str(error), {})
            return
        _report(run_id, "Succeeded", None, context._parser_report)


def _report(run_id: str, status: str, error: str | None, report: dict[str, Any]) -> None:
    secret = get_worker_runner_secret()
    if not secret:
        raise ValueError("GRID_WORKER_SECRET is required to report parser results")
    if error and len(error) > 3000:
        error = error[:3000] + "..."
    response = requests.post(
        f"{get_backend_api_url()}/api/parsers/runs/{run_id}/complete",
        headers={"X-Grid-Worker-Secret": secret, "Content-Type": "application/json"},
        json={"status": status, "error": error, "reportJson": json.dumps(report, default=str)},
        timeout=30,
    )
    response.raise_for_status()


def _load_env() -> None:
    try:
        from dotenv import load_dotenv
    except ImportError:
        return
    for parent in Path(__file__).resolve().parents:
        candidate = parent / ".env"
        if candidate.is_file():
            load_dotenv(candidate, override=False)
            return


def main() -> None:
    _load_env()
    project = os.environ["GOOGLE_CLOUD_PROJECT"]
    topic = os.getenv("PUBSUB_PARSER_TOPIC", "grid-parser-runs")
    subscription = os.getenv("PUBSUB_PARSER_SUBSCRIPTION", "grid-parser-runs-etl")
    publisher = pubsub_v1.PublisherClient()
    subscriber = pubsub_v1.SubscriberClient()
    topic_path = publisher.topic_path(project, topic)
    subscription_path = subscriber.subscription_path(project, subscription)
    try:
        publisher.create_topic(name=topic_path)
    except AlreadyExists:
        pass
    try:
        subscriber.create_subscription(name=subscription_path, topic=topic_path)
    except AlreadyExists:
        pass

    def callback(message: pubsub_v1.subscriber.message.Message) -> None:
        try:
            process_payload(json.loads(message.data.decode("utf-8")))
            message.ack()
        except BaseException as error:
            if isinstance(error, (KeyboardInterrupt, SystemExit)):
                raise
            logger.exception("Parser run message failed: %s", error)
            message.ack()

    logger.info("Listening on %s", subscription_path)
    future = subscriber.subscribe(subscription_path, callback=callback)
    future.result()


if __name__ == "__main__":
    main()
