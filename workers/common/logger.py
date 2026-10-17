from __future__ import annotations

import logging
import os
import sys

_INITIALIZED = False


def setup_worker_logging(level: str | None = None) -> None:
    global _INITIALIZED
    if _INITIALIZED:
        return
    log_level = getattr(logging, (level or os.getenv("LOG_LEVEL") or "INFO").upper(), logging.INFO)
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] [%(name)s] %(message)s"))
    root = logging.getLogger("grid")
    root.setLevel(log_level)
    if not root.handlers:
        root.addHandler(handler)
    _INITIALIZED = True


def get_worker_logger(name: str) -> logging.Logger:
    setup_worker_logging()
    if not name.startswith("grid."):
        name = f"grid.{name}"
    return logging.getLogger(name)
