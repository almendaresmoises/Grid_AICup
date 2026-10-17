from __future__ import annotations

import io
from pathlib import Path

import polars as pl

from workers.etl_worker.core.storage_provider import StorageProvider


class DeltaUtils:
    """Parquet object writes; each parser execution owns its output object."""

    @staticmethod
    def write_append(df: pl.DataFrame, table_uri: str, partition_by: list[str] | None) -> None:
        target = _object_uri(table_uri)
        storage = StorageProvider()
        if storage.file_exists(target):
            previous = pl.read_parquet(io.BytesIO(storage.read_file(target)))
            df = pl.concat([previous, df], how="vertical")
        _write_parquet(df, table_uri)

    @staticmethod
    def write_idempotent(
        df: pl.DataFrame,
        table_uri: str,
        partition_by: list[str] | None = None,
    ) -> None:
        # Replacing the complete execution-owned object is idempotent and retains
        # duplicate source rows for the reconciliation ambiguity checks.
        _write_parquet(df, table_uri)


def _object_uri(uri: str) -> str:
    return uri if uri.endswith(".parquet") else uri.rstrip("/") + "/data.parquet"


def _write_parquet(df: pl.DataFrame, uri: str) -> None:
    target = _object_uri(uri)
    if target.startswith("gs://"):
        buffer = io.BytesIO()
        df.write_parquet(buffer)
        StorageProvider().write_file(target, buffer.getvalue())
        return
    path = Path(target)
    path.parent.mkdir(parents=True, exist_ok=True)
    df.write_parquet(path)
