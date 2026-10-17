"""Explicit final projection from transformed columns to a canonical dataset contract."""

from __future__ import annotations

from datetime import date, datetime
import hashlib
import json
from typing import Any, Callable, Mapping, Sequence

import polars as pl


PolarsDataType = pl.DataType | type[pl.DataType]


CANONICAL_TYPES: dict[str, PolarsDataType] = {
    "pl.String": pl.String,
    "pl.Date": pl.Date,
    "pl.Datetime": pl.Datetime,
    "pl.Decimal(18,2)": pl.Decimal(18, 2),
    "pl.Int64": pl.Int64,
    "pl.Int32": pl.Int32,
    "pl.Float64": pl.Float64,
    "pl.Boolean": pl.Boolean,
}


SOURCE_FILE_NAME_COLUMN = "__source_file_name"
"""Working column holding the parsed file's name (the inner entry for ZIPs); read by ``file_name`` mappings."""

SOURCE_RECORD_ID_COLUMN = "__source_record_id"
"""Working column holding each row's generated record ID; read by ``generated_record_id`` mappings."""


class CanonicalMappingError(ValueError):
    """Raised when a strict canonical output mapping is incomplete or invalid."""


def _uses_source_kind(configuration: Mapping[str, Any], source_kind: str) -> bool:
    contract = configuration.get("canonicalMapping")
    columns = contract.get("columns") if isinstance(contract, Mapping) else None
    return isinstance(columns, list) and any(
        isinstance(mapping, Mapping) and mapping.get("sourceKind") == source_kind for mapping in columns
    )


def _references_column(configuration: Mapping[str, Any], column: str) -> bool:
    """True when a Studio step parameter or a column-fed canonical mapping names `column`."""
    steps = configuration.get("appliedSteps")
    for step in steps if isinstance(steps, list) else []:
        params = step.get("params") if isinstance(step, Mapping) else None
        if isinstance(params, Mapping) and any(
            value == column or (isinstance(value, list) and column in value) for value in params.values()
        ):
            return True
    contract = configuration.get("canonicalMapping")
    columns = contract.get("columns") if isinstance(contract, Mapping) else None
    return isinstance(columns, list) and any(
        isinstance(mapping, Mapping) and mapping.get("sourceColumn") == column for mapping in columns
    )


def uses_source_file_name(configuration: Mapping[str, Any]) -> bool:
    """
    True when the parsed file's name is needed: a `file_name` mapping, or steps / lookups that read the
    working column (e.g. MID of the file name, leading zeros removed, then a database lookup).
    """
    return _uses_source_kind(configuration, "file_name") or _references_column(configuration, SOURCE_FILE_NAME_COLUMN)


def uses_generated_record_id(configuration: Mapping[str, Any]) -> bool:
    """True when any canonical mapping takes a generated record ID."""
    return _uses_source_kind(configuration, "generated_record_id")


def generated_record_ids(archive_name: str, entry_name: str, row_count: int) -> pl.Series:
    """
    Stable, run-unique record IDs for reconciliation: SHA-256 hex of the landed file name, the parsed
    entry name (inner path for ZIPs, empty archive name for plain files) and the row's position after
    noise-line removal. Re-running the same file with the same parser configuration yields the same IDs.
    """
    prefix = f"{archive_name}\x1f{entry_name}\x1f"
    return pl.Series(
        SOURCE_RECORD_ID_COLUMN,
        [hashlib.sha256(f"{prefix}{index}".encode("utf-8")).hexdigest() for index in range(row_count)],
        dtype=pl.String,
    )


ReferenceResolver = Callable[[Mapping[str, Any], list[str]], Mapping[str, str | None]]
"""Resolves a reference mapping's output value for each (trimmed) key; unmatched keys map to None."""


def unpadded_key(key: str) -> str:
    """The key without leading zeros ("0" when it is all zeros), so 000661078809 matches 661078809."""
    stripped = key.lstrip("0")
    return stripped or ("0" if key else "")


def reference_values_from_rows(
    rows: Sequence[Mapping[str, object]],
    key_column: str,
    output_column: str,
    keys: Sequence[str],
) -> dict[str, str | None]:
    """Value of `output_column` for each key, matching reference rows on `key_column` while ignoring leading zeros."""
    by_key: dict[str, str] = {}
    for row in rows:
        lowered = {str(name).lower(): value for name, value in row.items()}
        key_value = lowered.get(key_column.lower())
        value = lowered.get(output_column.lower())
        if key_value is None or value is None or not str(value).strip():
            continue
        key_text = str(key_value).strip()
        by_key.setdefault(key_text, str(value))
        by_key.setdefault(unpadded_key(key_text), str(value))
    return {key: by_key.get(key, by_key.get(unpadded_key(key))) for key in keys}


def reference_expression(
    frame: pl.DataFrame,
    raw_mapping: Mapping[str, Any],
    input_columns: set[str],
    reference_resolver: ReferenceResolver | None,
) -> pl.Expr:
    """
    Looks up the key column named by `sourceColumn` and yields the reference output value. Used by
    reference-mapping fields (after all Studio steps) and by the LookupReference step (mid-pipeline).
    """
    source = raw_mapping.get("sourceColumn")
    if not isinstance(source, str) or source not in input_columns or reference_resolver is None:
        return pl.lit(None)
    keys_expression = pl.col(source).cast(pl.String).str.strip_chars()
    keys = [key for key in frame.select(keys_expression.unique()).to_series().to_list() if isinstance(key, str) and key]
    resolved = {key: value for key, value in reference_resolver(raw_mapping, keys).items() if value is not None}
    if not resolved:
        return pl.lit(None)
    return keys_expression.replace_strict(resolved, default=None, return_dtype=pl.String)


def apply_configured_canonical_mapping(
    frame: pl.DataFrame,
    parser_config: Mapping[str, Any],
    reference_resolver: ReferenceResolver | None = None,
) -> pl.DataFrame:
    """Apply the optional legacy Studio canonicalMapping block as a final projection."""
    raw_configuration = parser_config.get("configurationJson", {})
    configuration = json.loads(raw_configuration) if isinstance(raw_configuration, str) else raw_configuration
    if not isinstance(configuration, Mapping):
        return frame
    raw_contract = configuration.get("canonicalMapping")
    if not isinstance(raw_contract, Mapping):
        return frame
    raw_columns = raw_contract.get("columns")
    if not isinstance(raw_columns, list) or not raw_columns:
        return frame

    strict = bool(raw_contract.get("strict", True))
    input_columns = set(frame.columns)
    projected_targets: set[str] = set()
    expressions: list[pl.Expr] = []

    for raw_mapping in raw_columns:
        if not isinstance(raw_mapping, Mapping):
            if strict:
                raise CanonicalMappingError("canonical mapping entries must be objects")
            continue
        target = raw_mapping.get("targetColumn")
        target_type = raw_mapping.get("targetType")
        source_kind = raw_mapping.get("sourceKind", "unmapped")
        required = bool(raw_mapping.get("required", False))
        if not isinstance(target, str) or not target:
            raise CanonicalMappingError("canonical targetColumn must be a non-empty string")
        if target in projected_targets:
            raise CanonicalMappingError(f"canonical target '{target}' is configured more than once")
        if not isinstance(target_type, str) or target_type not in CANONICAL_TYPES:
            raise CanonicalMappingError(f"canonical target '{target}' has unsupported type '{target_type}'")
        projected_targets.add(target)

        expression: pl.Expr
        if source_kind == "column":
            source = raw_mapping.get("sourceColumn")
            if not isinstance(source, str) or not source:
                raise CanonicalMappingError(f"canonical target '{target}' requires a source column")
            if source not in input_columns:
                if strict:
                    raise CanonicalMappingError(
                        f"canonical target '{target}' references unavailable source '{source}'"
                    )
                expression = pl.lit(None)
            else:
                expression = _with_date_part(frame, source, raw_mapping.get("datePart"))
        elif source_kind == "constant":
            expression = pl.lit(raw_mapping.get("constantValue", ""))
        elif source_kind == "run_date":
            today = date.today()
            if target == "year":
                expression = pl.lit(today.year)
            elif target == "month":
                expression = pl.lit(today.month)
            elif target == "day":
                expression = pl.lit(today.day)
            elif target_type == "pl.Date":
                expression = pl.lit(today)
            else:
                expression = pl.lit(today.isoformat())
        elif source_kind == "file_name":
            if SOURCE_FILE_NAME_COLUMN in input_columns:
                expression = pl.col(SOURCE_FILE_NAME_COLUMN)
            elif strict and required:
                raise CanonicalMappingError(f"required canonical target '{target}' has no source file name")
            else:
                expression = pl.lit(None)
        elif source_kind == "generated_record_id":
            if SOURCE_RECORD_ID_COLUMN not in input_columns:
                raise CanonicalMappingError(f"canonical target '{target}' has no generated record ID")
            expression = pl.col(SOURCE_RECORD_ID_COLUMN)
        elif source_kind == "reference_mapping":
            expression = reference_expression(frame, raw_mapping, input_columns, reference_resolver)
        elif source_kind == "null":
            if required:
                raise CanonicalMappingError(f"required canonical target '{target}' cannot map to NULL")
            expression = pl.lit(None)
        elif source_kind == "unmapped":
            if strict:
                raise CanonicalMappingError(f"canonical target '{target}' has no explicit mapping decision")
            expression = pl.lit(None)
        else:
            raise CanonicalMappingError(
                f"canonical target '{target}' has unsupported source kind '{source_kind}'"
            )

        if target_type == "pl.Date":
            expression = _parse_date_robust(expression)
        elif target_type == "pl.Datetime":
            expression = expression.cast(pl.String).str.to_datetime(strict=False)
        else:
            expression = expression.cast(CANONICAL_TYPES[target_type], strict=False)
        expressions.append(expression.alias(target))

    passthrough = raw_contract.get("passthroughColumns", [])
    if not isinstance(passthrough, list) or not all(isinstance(column, str) for column in passthrough):
        raise CanonicalMappingError("canonical passthroughColumns must be a list of strings")
    for column in passthrough:
        if column in input_columns and column not in projected_targets:
            expressions.append(pl.col(column))

    return frame.select(expressions)


def _parse_date_robust(expr: pl.Expr) -> pl.Expr:
    str_expr = expr.cast(pl.String).str.strip_chars()
    return pl.coalesce([
        str_expr.str.to_date("%Y-%m-%d", strict=False),
        str_expr.str.to_date("%m/%d/%Y", strict=False),
        str_expr.str.to_date("%Y%m%d", strict=False),
        str_expr.str.to_date("%d/%m/%Y", strict=False),
    ])


def _with_date_part(frame: pl.DataFrame, source: str, date_part: object) -> pl.Expr:
    """The source column, or only its year/month/day when the mapping extracts a date part."""
    if date_part not in {"year", "month", "day"}:
        return pl.col(source)
    if frame.schema[source] in {pl.Date, pl.Datetime}:
        as_date = pl.col(source)
    else:
        as_date = _parse_date_robust(pl.col(source))
    if date_part == "year":
        return as_date.dt.year()
    if date_part == "month":
        return as_date.dt.month()
    return as_date.dt.day()


def extract_canonical_coverage(frame: pl.DataFrame) -> list[dict[str, Any]]:
    """Extracts partition coverage summaries (partition_key, company_code, branch_code, business_date, row_count, is_tombstone)
    from a canonical or normalized DataFrame.
    """
    coverage: list[dict[str, Any]] = []
    if frame.is_empty():
        return coverage

    date_col = next((c for c in ["transaction_date", "business_date", "trx_date"] if c in frame.columns), None)
    comp_col = next((c for c in ["company_code", "company"] if c in frame.columns), None)
    branch_col = next((c for c in ["branch_code", "branch"] if c in frame.columns), None)

    group_cols = [c for c in [comp_col, branch_col, date_col] if c is not None]
    if not group_cols:
        return coverage

    try:
        part_summary = frame.group_by(group_cols).len()
        for row in part_summary.iter_rows(named=True):
            c_code = str(row.get(comp_col, "000")) if comp_col and row.get(comp_col) is not None else "000"
            b_code = str(row.get(branch_col, "0000")) if branch_col and row.get(branch_col) is not None else "0000"
            raw_date = row.get(date_col) if date_col else None
            b_date_val: str | None = str(raw_date)[:10] if raw_date is not None and str(raw_date).strip() != "" else ""
            if b_date_val and b_date_val != "None":
                part_key = f"{c_code}:{b_code}:{b_date_val}"
            else:
                part_key = f"{c_code}:{b_code}"
                b_date_val = None

            coverage.append({
                "partition_key": part_key,
                "company_code": c_code,
                "branch_code": b_code,
                "business_date": b_date_val,
                "row_count": row.get("len", 0),
                "is_tombstone": False,
            })
    except Exception:
        pass
    return coverage


def make_tombstone_coverage(company_code: str, branch_code: str, business_date: str | None = None) -> dict[str, Any]:
    """Constructs an authoritative tombstone coverage record for a partition scope."""
    b_date = str(business_date)[:10] if business_date and str(business_date).strip() != "" else None
    part_key = f"{company_code}:{branch_code}:{b_date}" if b_date else f"{company_code}:{branch_code}"
    return {
        "partition_key": part_key,
        "company_code": company_code,
        "branch_code": branch_code,
        "business_date": b_date,
        "row_count": 0,
        "is_tombstone": True,
    }

