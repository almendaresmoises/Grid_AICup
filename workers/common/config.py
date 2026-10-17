from __future__ import annotations

import os


def get_backend_api_url() -> str:
    return os.getenv("GRID_API_URL", "http://localhost:44344").rstrip("/")


def get_worker_runner_secret() -> str:
    return os.getenv("GRID_WORKER_SECRET", "")


def get_reconciliation_db_url() -> str:
    return os.getenv("GRID_DATABASE_URL", "")
