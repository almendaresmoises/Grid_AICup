"""Composable container, record-policy, and reader adapters for parser DSL inputs."""

from __future__ import annotations

import csv
import fnmatch
import io
import xml.etree.ElementTree as ET
import zipfile
import re
from collections.abc import Mapping

import polars as pl

from workers.etl_worker.core.storage_provider import StorageProvider
from workers.common.config import get_reconciliation_db_url
from workers.etl_worker.jobs.parsers.engine import PipelineCompileError
from workers.etl_worker.jobs.parsers.engine.models import PipelineInput, RecordPolicy
from workers.etl_worker.jobs.parsers.source_reader import read_source


def load_pipeline_input(
    source: PipelineInput,
    uri: str,
    storage: StorageProvider,
) -> pl.LazyFrame:
    """Load one logical input while keeping archive handling independent of its reader."""
    if source.container.type == "none" and source.connector == "delta":
        return pl.scan_delta(uri, version=source.options.get("version"))
    if source.container.type == "none" and source.connector == "parquet":
        return pl.scan_parquet(uri)
    if source.connector == "database":
        return _read_database(source)

    content = storage.read_file(uri)
    payloads = _extract_payloads(content, source)
    frames = [_read_payload(payload, source) for payload in payloads]
    if not frames:
        raise PipelineCompileError(f"input '{source.id}' did not produce any readable records")
    if len(frames) == 1:
        return frames[0].lazy()
    return pl.concat([frame.lazy() for frame in frames], how="diagonal_relaxed")


def _extract_payloads(content: bytes, source: PipelineInput) -> list[bytes]:
    if source.container.type == "none":
        if content.startswith(b"PK\x03\x04"):
            try:
                with zipfile.ZipFile(io.BytesIO(content)) as archive:
                    names = set(archive.namelist())
                    if not ("xl/workbook.xml" in names or "xl/workbook.bin" in names):
                        for entry in archive.infolist():
                            if entry.is_dir():
                                continue
                            filename = entry.filename.split("/")[-1]
                            if filename.startswith(".") or entry.filename.startswith("__MACOSX/"):
                                continue
                            lower = filename.lower()
                            if lower.endswith((".csv", ".txt", ".xlsx", ".xls")):
                                return [archive.read(entry)]
            except zipfile.BadZipFile:
                pass
        return [content]
    if source.container.type != "zip":
        raise PipelineCompileError(f"unsupported container '{source.container.type}'")

    try:
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            entries = [entry for entry in archive.infolist() if not entry.is_dir()]
            if len(entries) > source.container.maxEntries:
                raise PipelineCompileError(
                    f"ZIP contains {len(entries)} entries; configured maximum is {source.container.maxEntries}"
                )
            _validate_archive_limits(entries, source)
            matching = [
                entry for entry in entries
                if fnmatch.fnmatch(entry.filename.lower(), source.container.entryPattern.lower())
            ]
            if not matching:
                raise PipelineCompileError(
                    f"ZIP has no entry matching '{source.container.entryPattern}'"
                )
            if len(matching) > 1 and source.container.onMultiple == "fail":
                raise PipelineCompileError(
                    f"ZIP entry pattern is ambiguous: {len(matching)} entries match '{source.container.entryPattern}'"
                )
            selected = matching if source.container.onMultiple == "all" else matching[:1]
            return [archive.read(entry) for entry in selected]
    except zipfile.BadZipFile as exception:
        raise PipelineCompileError("configured ZIP input is not a valid ZIP archive") from exception


def _validate_archive_limits(entries: list[zipfile.ZipInfo], source: PipelineInput) -> None:
    total_size = sum(entry.file_size for entry in entries)
    if total_size > source.container.maxUncompressedBytes:
        raise PipelineCompileError(
            f"ZIP expands to {total_size} bytes; configured maximum is {source.container.maxUncompressedBytes}"
        )
    for entry in entries:
        compressed_size = max(entry.compress_size, 1)
        ratio = entry.file_size / compressed_size
        if ratio > source.container.maxCompressionRatio:
            raise PipelineCompileError(
                f"ZIP entry '{entry.filename}' exceeds the configured compression ratio"
            )


def _read_payload(content: bytes, source: PipelineInput) -> pl.DataFrame:
    if source.connector in {"csv", "delimited", "fixedWidth", "excel"}:
        reader_options = dict(source.options)
        reader_options["format"] = {
            "csv": "delimited",
            "delimited": "delimited",
            "fixedWidth": "fixed_width",
            "excel": "excel",
        }[source.connector]
        if source.connector in {"csv", "delimited"}:
            content = _apply_record_policy(content, reader_options, source.recordPolicy)
        return read_source(content, reader_options)
    if source.connector == "ndjson":
        return pl.read_ndjson(io.BytesIO(content))
    if source.connector == "json":
        return pl.read_json(io.BytesIO(content))
    if source.connector == "xml":
        return _read_xml(content, source.options)
    if source.connector == "parquet":
        return pl.read_parquet(io.BytesIO(content))
    raise PipelineCompileError(f"connector '{source.connector}' has no input adapter")


def _apply_record_policy(
    content: bytes,
    options: Mapping[str, object],
    policy: RecordPolicy,
) -> bytes:
    encoding_value = options.get("encoding", "utf-8")
    encoding = encoding_value if isinstance(encoding_value, str) else "utf-8"
    delimiter_value = options.get("delimiter", ",")
    delimiter = delimiter_value if isinstance(delimiter_value, str) and len(delimiter_value) == 1 else ","
    decoded = content.decode(encoding, errors="replace")
    accepted: list[str] = []
    for line_number, line in enumerate(decoded.splitlines(), start=1):
        stripped = line.strip()
        if policy.skipBlankRows and not stripped:
            continue
        if any(stripped.startswith(prefix) for prefix in policy.ignorePrefixes):
            continue
        if policy.expectedFieldCount is not None:
            field_count = len(next(csv.reader([line], delimiter=delimiter)))
            if field_count != policy.expectedFieldCount:
                if policy.raggedRows == "drop":
                    continue
                raise PipelineCompileError(
                    f"record {line_number} has {field_count} fields; expected {policy.expectedFieldCount}"
                )
        accepted.append(line)
    return "\n".join(accepted).encode(encoding)


def _read_xml(content: bytes, options: Mapping[str, object]) -> pl.DataFrame:
    record_tag = options.get("recordTag")
    if not isinstance(record_tag, str) or not record_tag:
        raise PipelineCompileError("XML inputs require options.recordTag")
    root = ET.fromstring(content)
    records = [
        {child.tag: child.text for child in element}
        for element in root.iter(record_tag)
    ]
    return pl.DataFrame(records)


def _read_database(source: PipelineInput) -> pl.LazyFrame:
    profile = source.options.get("connectionProfile")
    if profile != "isa_readonly":
        raise PipelineCompileError("database inputs require the 'isa_readonly' connection profile")
    relation = source.options.get("relation")
    if not isinstance(relation, str) or not re.fullmatch(r"[a-z_][a-z0-9_]*\.[a-z_][a-z0-9_]*", relation):
        raise PipelineCompileError("database relation must be a lowercase snake_case schema.view name")
    columns_value = source.options.get("columns")
    if not isinstance(columns_value, list) or not columns_value:
        raise PipelineCompileError("database inputs require an explicit columns list")
    columns: list[str] = []
    for column in columns_value:
        if not isinstance(column, str) or not re.fullmatch(r"[a-z_][a-z0-9_]*", column):
            raise PipelineCompileError("database columns must be lowercase snake_case identifiers")
        columns.append(column)
    maximum_rows_value = source.options.get("maxRows", 1_000_000)
    if not isinstance(maximum_rows_value, int) or maximum_rows_value < 1 or maximum_rows_value > 10_000_000:
        raise PipelineCompileError("database maxRows must be between 1 and 10,000,000")
    database_uri = get_reconciliation_db_url()
    if not database_uri:
        raise PipelineCompileError("the isa_readonly database connection profile is not configured")
    query = f"SELECT {', '.join(columns)} FROM {relation} LIMIT {maximum_rows_value}"
    return pl.read_database_uri(query, database_uri, engine="adbc").lazy()
