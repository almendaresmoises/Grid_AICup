"""
DSL v2 Pipeline Runner for Dynamic Parser Jobs.

Executes compiled declarative transformation pipelines, applies quarantine policies,
and records execution profiling and error summaries.
"""

from __future__ import annotations

import io
import json
import os
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Literal, Optional

import polars as pl

from workers.common.logger import get_worker_logger
from workers.etl_worker.core.base_job import ExecutionContext
from workers.etl_worker.core.delta_utils import DeltaUtils
from workers.etl_worker.core.storage_provider import StorageProvider
from workers.etl_worker.jobs.parsers.canonical_mapping import extract_canonical_coverage
from workers.etl_worker.jobs.parsers.engine import ParserPipeline, PipelineCompileError, PolarsPipelineCompiler
from workers.etl_worker.jobs.parsers.engine.compiler import DROP_COLUMN, ERROR_CODES_COLUMN, QUARANTINE_COLUMN, TYPE_MAP
from workers.etl_worker.jobs.parsers.input_resolver import normalize_file_uri, resolve_input_files
from workers.etl_worker.jobs.parsers.source_reader import read_source, unwrap_zip_entry

logger = get_worker_logger("parsers.v2_pipeline_runner")

CsvEncoding = Literal["utf8", "utf8-lossy"]


def validate_output_contract(frame: pl.DataFrame, fields: list[dict[str, Any]]) -> pl.DataFrame:
    """Enforce the dataset snapshot supplied by the API, including required values."""
    if not fields:
        return frame
    aliases = {"decimal": "decimal(18,2)", "integer": "int64", "float": "float64"}
    columns = []
    for field in fields:
        name = field.get("name") or field.get("Name")
        raw_type = field.get("type") or field.get("Type") or "string"
        is_required = bool(field.get("required") if "required" in field else field.get("IsRequired", False))
        dtype = TYPE_MAP.get(aliases.get(raw_type, raw_type))
        if dtype is None:
            raise ValueError(f"Unsupported dataset output type '{raw_type}'")
        if name not in frame.columns:
            if is_required:
                raise ValueError(f"Required dataset output column '{name}' was not produced")
            columns.append(pl.lit(None, dtype=dtype).alias(name))
        else:
            columns.append(pl.col(name).cast(dtype, strict=True).alias(name))
    output = frame.select(columns)
    for field in fields:
        name = field.get("name") or field.get("Name")
        is_required = bool(field.get("required") if "required" in field else field.get("IsRequired", False))
        values = output[name]
        if is_required and (values.null_count() or (values.dtype == pl.String and values.str.strip_chars().eq("").any())):
            raise ValueError(f"Required dataset column '{name}' contains empty values")
    return output


def strip_engine_columns(frame: pl.DataFrame) -> pl.DataFrame:
    internal = [ERROR_CODES_COLUMN, QUARANTINE_COLUMN, DROP_COLUMN]
    return frame.drop([column for column in internal if column in frame.columns])


def bounded_redacted_rows(
    frame: pl.DataFrame,
    limit: int,
    sensitive_columns: set[str],
) -> List[Dict[str, Any]]:
    sample_columns = frame.columns[:50]
    rows = (
        json.loads(frame.select(sample_columns).head(limit).write_json())
        if limit and sample_columns
        else []
    )
    for row in rows:
        for column, value in list(row.items()):
            if column in sensitive_columns and value is not None:
                text_value = str(value)
                row[column] = "***" + text_value[-4:] if len(text_value) >= 4 else "***"
            elif isinstance(value, str) and len(value) > 500:
                row[column] = value[:497] + "..."
    while rows and len(json.dumps(rows, default=str).encode("utf-8")) > 64 * 1024:
        rows.pop()
    return rows


def resolve_v2_binding(
    binding: str,
    context: ExecutionContext,
    storage: StorageProvider,
    *,
    output: bool = False,
) -> str:
    if binding == "execution.input":
        location = context.input_location
        layer = "bronze"
    elif binding == "execution.output":
        location = context.output_location
        layer = "silver"
    else:
        location = context.input_bindings.get(binding)
        layer = "bronze"

    if not location:
        if not output:
            discovered = resolve_input_files(None, context, storage)
            if discovered:
                return discovered[0]
        raise PipelineCompileError(f"storage binding '{binding}' has no execution location")

    if os.path.isabs(location) or location.startswith(("s3://", "gs://", "az://")):
        return location
    if output:
        return storage.get_uri(location, layer=layer)
    if layer == "bronze" and not location.lower().endswith((".csv", ".txt", ".json", ".ndjson", ".parquet", ".xlsx", ".xls")):
        return storage.get_uri(location, layer=layer)
    return str(storage.get_local_path(location))


def _apply_configured_columns(
    frame: pl.LazyFrame,
    configured_names: list[str],
    has_header: bool,
) -> pl.LazyFrame:
    actual_names = list(frame.collect_schema().names())
    # Step 1: Normalize unnamed/blank/whitespace header columns so they match Studio's naming
    norm_map: dict[str, str] = {}
    used_names: set[str] = set()
    for i, col in enumerate(actual_names):
        stripped = col.strip()
        if col.startswith("__UNNAMED__") or not stripped:
            candidate = f"column_{i + 1}"
        else:
            candidate = stripped
        if candidate in used_names:
            candidate = f"{candidate}_{i + 1}"
        used_names.add(candidate)
        if candidate != col:
            norm_map[col] = candidate
    if norm_map:
        frame = frame.rename(norm_map)
        actual_names = list(frame.collect_schema().names())

    if not configured_names:
        return frame

    # Step 2: Only apply positional rename when configured_names has the exact same width
    # and is not already satisfied by actual_names (prevents shifting when excluded columns were omitted)
    actual_set = set(actual_names)
    if not set(configured_names).issubset(actual_set) and len(configured_names) == len(actual_names):
        rename_map: dict[str, str] = {}
        final_used: set[str] = set()
        for i, (act, conf) in enumerate(zip(actual_names, configured_names)):
            target = conf.strip() or f"column_{i + 1}"
            if target in final_used:
                target = f"{target}_{i + 1}"
            final_used.add(target)
            if target != act:
                rename_map[act] = target
        if rename_map:
            frame = frame.rename(rename_map)
            actual_names = list(frame.collect_schema().names())
            actual_set = set(actual_names)

    # Step 3: Ensure any missing configured columns (e.g. dropped trailing empty Excel columns) exist
    missing = [col for col in configured_names if col and col not in actual_set]
    if missing:
        seen_missing: set[str] = set()
        unique_missing = []
        for col in missing:
            if col not in seen_missing:
                seen_missing.add(col)
                unique_missing.append(col)
        frame = frame.with_columns([
            pl.lit(None, dtype=pl.String).alias(col)
            for col in unique_missing
        ])
    return frame


def load_v2_inputs(
    pipeline: ParserPipeline,
    context: ExecutionContext,
    storage: StorageProvider,
) -> Dict[str, pl.LazyFrame]:
    frames: Dict[str, pl.LazyFrame] = {}
    manifest_files = resolve_input_files(None, context, storage)

    for source in pipeline.inputs:
        if source.id == "main" and manifest_files and not context.input_location:
            uri = manifest_files[0] if len(manifest_files) == 1 else manifest_files
        else:
            uri = resolve_v2_binding(source.binding, context, storage)

        options = source.options
        if source.connector == "delta":
            frame = pl.scan_delta(uri if isinstance(uri, str) else uri[0], version=options.get("version"))
        elif source.connector in {"csv", "delimited"}:
            raw_encoding = str(options.get("encoding", "utf8"))
            encodings: dict[str, CsvEncoding] = {"utf8": "utf8", "utf8-lossy": "utf8-lossy"}
            if raw_encoding not in encodings:
                raise PipelineCompileError(f"unsupported CSV encoding '{raw_encoding}'")
            file_bytes = storage.read_file(uri if isinstance(uri, str) else uri[0])
            unwrapped, inner_name = unwrap_zip_entry(file_bytes)
            text_data = unwrapped if inner_name is not None else file_bytes
            if not text_data.strip():
                cols = options.get("columnNames") or ["column_1"]
                frame = pl.DataFrame(schema={col: pl.String for col in cols}).lazy()
            else:
                has_header = bool(options.get("hasHeader", True))
                frame = pl.read_csv(
                    io.BytesIO(text_data),
                    separator=str(options.get("delimiter", ",")),
                    has_header=has_header,
                    skip_rows=int(options.get("skipRows", 0)),
                    encoding=encodings[raw_encoding],
                    infer_schema=False,
                    truncate_ragged_lines=bool(options.get("truncateRaggedLines", True)),
                ).lazy()
                configured_names = options.get("columnNames") or []
                frame = _apply_configured_columns(frame, configured_names, has_header)
        elif source.connector == "parquet":
            frame = pl.scan_parquet(uri)
        elif source.connector == "ndjson":
            frame = pl.scan_ndjson(uri if isinstance(uri, str) else uri[0])
        elif source.connector == "excel":
            excel_path = uri if isinstance(uri, str) else uri[0]
            file_bytes = storage.read_file(excel_path)
            unwrapped, inner_name = unwrap_zip_entry(file_bytes)
            excel_bytes = unwrapped if inner_name is not None else file_bytes
            has_header = bool(options.get("hasHeader", True))
            configured_names = options.get("columnNames") or []
            header_idx = int(options.get("headerRowIndex", options.get("skipRows", 0)) or 0)
            read_options = {"header_row": header_idx} if has_header else None
            sheets = pl.read_excel(
                io.BytesIO(excel_bytes),
                sheet_name=options.get("sheetName") or None,
                has_header=has_header,
                read_options=read_options,
                infer_schema_length=0,
                drop_empty_cols=False,
            )
            raw_frame = next(iter(sheets.values())) if isinstance(sheets, dict) else sheets
            named_targets = {
                c.strip()
                for c in configured_names
                if isinstance(c, str) and c.strip() and not c.strip().startswith("column_")
            }
            if has_header and named_targets:
                actual_stripped = {c.strip() for c in raw_frame.columns}
                if len(named_targets & actual_stripped) < max(1, len(named_targets) // 2):
                    best_row = -1
                    best_hits = len(named_targets & actual_stripped)
                    for r_idx, row_vals in enumerate(raw_frame.head(40).iter_rows()):
                        row_set = {str(v).strip() for v in row_vals if v is not None and str(v).strip()}
                        hits = len(named_targets & row_set)
                        if hits > best_hits:
                            best_hits = hits
                            best_row = r_idx
                    if best_row >= 0:
                        header_vals = raw_frame.row(best_row)
                        new_cols: list[str] = []
                        seen_cols: set[str] = set()
                        for i, val in enumerate(header_vals):
                            c_name = str(val).strip() if val is not None and str(val).strip() else f"__UNNAMED__{i}"
                            if c_name in seen_cols:
                                c_name = f"{c_name}_{i}"
                            seen_cols.add(c_name)
                            new_cols.append(c_name)
                        raw_frame.columns = new_cols
                        raw_frame = raw_frame.slice(best_row + 1)
            if not has_header and int(options.get("skipRows", 0)) > 0:
                raw_frame = raw_frame.slice(int(options.get("skipRows", 0)))
            frame = raw_frame.lazy()
            frame = _apply_configured_columns(frame, configured_names, has_header)
        elif source.connector == "fixedWidth":
            fw_path = uri if isinstance(uri, str) else uri[0]
            frame = read_source(
                storage.read_file(fw_path),
                {
                    "format": "fixed_width",
                    "encoding": options.get("encoding", "utf-8"),
                    "fixedWidthColumns": options.get("columns", []),
                },
            ).lazy()
        elif source.connector == "json":
            json_path = uri if isinstance(uri, str) else uri[0]
            frame = pl.read_json(json_path).lazy()
        elif source.connector == "xml":
            record_tag = options.get("recordTag")
            if not isinstance(record_tag, str) or not record_tag:
                raise PipelineCompileError("XML inputs require options.recordTag")
            xml_path = uri if isinstance(uri, str) else uri[0]
            root = ET.fromstring(storage.read_file(xml_path))
            records = [
                {child.tag: child.text for child in element}
                for element in root.iter(record_tag)
            ]
            frame = pl.DataFrame(records).lazy()
        elif source.connector in {"postgres", "postgresql", "database", "sql"}:
            from workers.etl_worker.jobs.parsers.source_reader import read_database
            conn_uri = str(options.get("connectionUri") or options.get("connectionString") or (uri if isinstance(uri, str) else uri[0]))
            query = options.get("query") or options.get("sql")
            if not query:
                table_name = options.get("tableName") or options.get("table")
                if not table_name:
                    raise PipelineCompileError("Database source requires options.query or options.tableName")
                query = f"SELECT * FROM {table_name}"
            frame = read_database(conn_uri, str(query)).lazy()
        else:
            raise PipelineCompileError(f"connector '{source.connector}' requires a source adapter that is not enabled")

        declared_columns = source.schema_.columns
        if declared_columns:
            actual = set(frame.collect_schema().names())
            missing = [column.name for column in declared_columns if column.name not in actual]
            if missing and source.schema_.mode == "strict":
                raise PipelineCompileError(f"input '{source.id}' is missing required columns: {', '.join(missing)}")
            if source.schema_.mode == "strict":
                casts = [
                    pl.col(column.name).cast(TYPE_MAP[column.dataType], strict=True)
                    for column in declared_columns
                    if column.name in actual and column.dataType != "json"
                ]
                if casts:
                    frame = frame.with_columns(casts)
            elif source.schema_.mode == "permissive":
                frame = frame.with_columns(
                    pl.lit([], dtype=pl.List(pl.String)).alias(ERROR_CODES_COLUMN),
                    pl.lit(False).alias(QUARANTINE_COLUMN),
                    pl.lit(False).alias(DROP_COLUMN),
                )
                for column in declared_columns:
                    if column.name not in actual or column.dataType == "json":
                        continue
                    source_expr = pl.col(column.name)
                    casted = source_expr.cast(TYPE_MAP[column.dataType], strict=False)
                    invalid = source_expr.is_not_null() & casted.is_null()
                    frame = frame.with_columns(
                        casted.alias(column.name),
                        pl.when(invalid)
                        .then(pl.col(ERROR_CODES_COLUMN).list.concat(pl.lit([f"INPUT_CAST_{column.name}"])))
                        .otherwise(pl.col(ERROR_CODES_COLUMN))
                        .alias(ERROR_CODES_COLUMN),
                        (pl.col(QUARANTINE_COLUMN) | invalid).alias(QUARANTINE_COLUMN),
                    )

        if source.id == "main":
            frame = frame.with_row_index("__isa_source_row_number")
            frame = frame.with_columns(
                pl.concat_str(
                    [
                        pl.lit(context.task_id or "adhoc"),
                        pl.lit(source.id),
                        pl.col("__isa_source_row_number").cast(pl.String),
                    ],
                    separator=":"
                ).hash(seed=0).cast(pl.String).alias("__isa_row_id")
            )
            if context.execution_mode == "preview":
                frame = frame.head(context.preview_limit or 100)
        frames[source.id] = frame
    return frames


def _ensure_referenced_source_columns(frame: pl.LazyFrame, pipeline: ParserPipeline) -> pl.LazyFrame:
    try:
        existing = set(frame.collect_schema().names())
    except Exception:
        return frame
    created = set()
    referenced = set()
    for step in pipeline.steps:
        target = step.args.get("target")
        if isinstance(target, str):
            created.add(target)
        source = step.args.get("source")
        if isinstance(source, str):
            referenced.add(source)
        cols = step.args.get("columns")
        if isinstance(cols, list):
            for c in cols:
                if isinstance(c, str):
                    referenced.add(c)
        def _extract(obj: Any) -> None:
            if isinstance(obj, dict):
                if obj.get("node") == "column" and isinstance(obj.get("name"), str):
                    referenced.add(obj["name"])
                for v in obj.values():
                    _extract(v)
            elif isinstance(obj, list):
                for item in obj:
                    _extract(item)
        _extract(step.args)
    missing = [c for c in referenced if c not in existing and c not in created]
    if missing:
        frame = frame.with_columns([pl.lit(None, dtype=pl.String).alias(c) for c in missing])
    return frame


def execute_v2(context: ExecutionContext, storage: StorageProvider) -> None:
    pipeline = ParserPipeline.model_validate(context.pipeline_definition)
    inputs = load_v2_inputs(pipeline, context, storage)
    if "main" in inputs:
        inputs["main"] = _ensure_referenced_source_columns(inputs["main"], pipeline)
    input_count = int(
        inputs["main"].select(pl.len().alias("count")).collect().item(0, "count")
    )
    compiler = PolarsPipelineCompiler(pipeline, runtime_parameters=context.parameters)
    compiled = compiler.compile(inputs)

    if compiled.fail_frames:
        fail_counts = pl.collect_all(
            [frame.select(pl.len().alias("count")) for _, frame in compiled.fail_frames]
        )
        failures = [
            f"{step_id}: {int(count.item(0, 'count'))} row(s)"
            for (step_id, _), count in zip(compiled.fail_frames, fail_counts)
            if int(count.item(0, "count")) > 0
        ]
        if failures:
            raise ValueError("Fail-policy parser errors detected: " + "; ".join(failures))

    valid_lazy = compiled.valid_frame()
    quarantine_lazy = compiled.quarantine_frame()
    valid_df, quarantine_df = pl.collect_all([valid_lazy, quarantine_lazy])
    if input_count > 100_000:
        raise ValueError("Parser input exceeds the reconciliation limit of 100,000 rows")
    canonical_df = validate_output_contract(strip_engine_columns(valid_df), context.parameters.get("output_contract", []))
    if "trx_date" in canonical_df.columns and canonical_df["trx_date"].is_not_null().any():
        canonical_df = canonical_df.filter(pl.col("trx_date").is_not_null())
        valid_df = valid_df.head(canonical_df.height)

    quarantine_percentage = (quarantine_df.height / input_count * 100.0) if input_count else 0.0
    dropped_rows = max(0, input_count - valid_df.height - quarantine_df.height)
    threshold = pipeline.quality.failExecutionWhen.quarantinePercentageGreaterThan
    if threshold is not None and quarantine_percentage > threshold:
        raise ValueError(
            f"Quarantine rate {quarantine_percentage:.2f}% exceeds configured threshold {threshold:.2f}%"
        )

    target_uri = resolve_v2_binding(pipeline.sink.binding, context, storage, output=True)
    partition_cols = context.partition_by or pipeline.sink.partitionBy

    # Attach lineage tracking columns
    batch_id = str(context.parameters.get("batch_id") or context.task_id or "adhoc")
    ingested_at = datetime.now(timezone.utc).isoformat()
    source_file = context.file_path or context.parameters.get("source_file") or ""
    source_file_name = Path(source_file).name if source_file else ""

    if context.execution_mode != "preview":
        output_df = canonical_df
        dataset_output_columns = context.parameters.get("dataset_output_columns") or []
        if dataset_output_columns:
            selected_columns = [column for column in dataset_output_columns if column in output_df.columns]
            output_df = output_df.select(selected_columns)
            missing_partitions = [column for column in partition_cols if column not in output_df.columns]
            if missing_partitions:
                raise ValueError(
                    "Dataset partition fields were not produced: " + ", ".join(missing_partitions)
                )
        output_df = output_df.with_columns([
            pl.lit(batch_id).alias("_batch_id"),
            pl.lit(source_file_name).alias("_source_file"),
            pl.lit(ingested_at).alias("_ingested_at"),
        ])
        if pipeline.sink.writeMode == "append":
            DeltaUtils.write_append(output_df, target_uri, partition_cols)
        else:
            DeltaUtils.write_idempotent(
                df=output_df,
                table_uri=target_uri,
                partition_by=partition_cols,
            )
        csv_uri = target_uri + ".csv" if target_uri.endswith(".parquet") else target_uri.rstrip("/") + "/data.parquet.csv"
        storage.write_file(csv_uri, canonical_df.write_csv().encode("utf-8"))

    quarantine_uri: Optional[str] = None
    if (
        context.execution_mode != "preview"
        and pipeline.quality.quarantine.enabled
        and not quarantine_df.is_empty()
    ):
        if target_uri.startswith(("s3://", "gs://", "az://")):
            quarantine_uri = "/".join((
                target_uri.rstrip("/"),
                pipeline.quality.quarantine.relativePath.strip("/"),
                f"execution={context.task_id or 'adhoc'}",
            ))
        else:
            quarantine_uri = str(
                Path(target_uri)
                / pipeline.quality.quarantine.relativePath
                / f"execution={context.task_id or 'adhoc'}"
            )
        DeltaUtils.write_append(quarantine_df, quarantine_uri, [])

    sample_limit = pipeline.quality.quarantine.sampleLimit
    sensitive_columns = {
        str(column)
        for source in pipeline.inputs
        for column in source.options.get("sensitiveColumns", [])
        if isinstance(column, str)
    }
    error_counts: Dict[str, int] = {}
    if not quarantine_df.is_empty() and ERROR_CODES_COLUMN in quarantine_df.columns:
        exploded = quarantine_df.select(
            pl.col(ERROR_CODES_COLUMN).explode(empty_as_null=True).alias("code")
        ).group_by("code").len()
        error_counts = {str(row[0]): int(row[1]) for row in exploded.iter_rows() if row[0] is not None}

    column_profiles: List[Dict[str, Any]] = []
    for request in compiled.profile_requests:
        requested_columns = request.get("columns") or [
            column for column in valid_df.columns
            if not column.startswith("__isa_")
        ]
        requested_metrics = set(request.get("metrics") or ["min", "max", "mean", "std", "distinct", "null"])
        for column in requested_columns:
            if column not in valid_df.columns:
                continue
            series = valid_df[column]
            profile: Dict[str, Any] = {
                "step_id": request.get("stepId"),
                "column": column,
                "data_type": str(series.dtype),
                "row_count": valid_df.height,
            }
            if "null" in requested_metrics:
                profile["null_count"] = series.null_count()
                profile["null_percentage"] = round(
                    (series.null_count() / valid_df.height * 100.0) if valid_df.height else 0.0,
                    4,
                )
            if "distinct" in requested_metrics:
                profile["distinct_count"] = series.n_unique()
            if "min" in requested_metrics:
                profile["min"] = series.min()
            if "max" in requested_metrics:
                profile["max"] = series.max()
            if series.dtype.is_numeric():
                if "mean" in requested_metrics:
                    profile["mean"] = series.mean()
                if "std" in requested_metrics:
                    profile["std"] = series.std()
            column_profiles.append(profile)

    canonical_coverage = extract_canonical_coverage(valid_df)

    context._rows_processed = valid_df.height
    context._parser_report = {
        "dataset_code": pipeline.pipeline.name,
        "completeness_mode": "CompleteSnapshot" if valid_df.height > 0 else "Empty",
        "canonical_coverage": canonical_coverage,
        "pipeline_revision": context.pipeline_revision or pipeline.pipeline.revision,
        "pipeline_hash": context.pipeline_hash,
        "execution_mode": context.execution_mode,
        "input_rows": input_count,
        "output_rows": valid_df.height,
        "dropped_rows": dropped_rows,
        "quarantined_rows": quarantine_df.height,
        "quarantine_percentage": round(quarantine_percentage, 4),
        "quarantine_location": quarantine_uri,
        "errors_by_code": error_counts,
        "column_profiles": json.loads(json.dumps(column_profiles, default=str)),
        "error_samples": bounded_redacted_rows(quarantine_df, sample_limit, sensitive_columns),
        "preview_rows": bounded_redacted_rows(
            canonical_df, context.preview_limit or 100, sensitive_columns
        ) if context.execution_mode == "preview" else [],
    }
    logger.info(
        "Dynamic parser DSL v2 '%s' revision %s completed: %s valid, %s quarantined",
        pipeline.pipeline.name,
        pipeline.pipeline.revision,
        valid_df.height,
        quarantine_df.height,
    )
