"""Partition expression compilation for legacy Studio parser configurations."""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date

import polars as pl


DATE_CANDIDATES = (
    "trx_date",
    "posting_date",
    "pos_date",
    "settlement_date",
    "pf_date",
    "stld_date",
    "order_date",
)


def build_partition_expressions(
    frame: pl.DataFrame,
    configuration: Mapping[str, object],
    run_date: date | None = None,
) -> list[pl.Expr]:
    """Compile each partition rule independently, resolving canonical aliases."""
    effective_run_date = run_date or date.today()
    expressions: list[pl.Expr] = []
    configured_parts: set[str] = set()
    partitioning = configuration.get("partitioning")
    rules = partitioning if isinstance(partitioning, list) else []

    for raw_rule in rules:
        if not isinstance(raw_rule, Mapping):
            continue
        name = raw_rule.get("name")
        if not isinstance(name, str) or not name:
            continue
        configured_part = raw_rule.get("datePart")
        date_part = configured_part if configured_part in {"year", "month", "day"} else name.lower()
        if date_part in {"year", "month", "day"}:
            configured_parts.add(date_part)
            expressions.append(
                _date_part_expression(frame, configuration, raw_rule, name, date_part, effective_run_date)
            )
            continue

        custom = _custom_partition_expression(frame, configuration, raw_rule, name, effective_run_date)
        if custom is not None:
            expressions.append(custom)

    fallback = _fallback_date_expression(frame, configuration)
    for date_part in ("year", "month", "day"):
        if date_part in configured_parts:
            continue
        if fallback is None:
            value = getattr(effective_run_date, date_part)
            expressions.append(pl.lit(value).cast(pl.Int32).alias(date_part))
        else:
            expressions.append(_extract_date_part(fallback, date_part).alias(date_part))

    return expressions


def _date_part_expression(
    frame: pl.DataFrame,
    configuration: Mapping[str, object],
    rule: Mapping[object, object],
    target: str,
    date_part: str,
    run_date: date,
) -> pl.Expr:
    source_type = rule.get("sourceType", "date_column")
    if source_type == "run_date":
        return pl.lit(getattr(run_date, date_part)).cast(pl.Int32).alias(target)
    if source_type == "constant":
        return pl.lit(rule.get("sourceValue")).cast(pl.Int32, strict=False).alias(target)

    source_value = rule.get("sourceValue")
    source = _resolve_working_column(frame, configuration, source_value if isinstance(source_value, str) else "")
    if source is None:
        return pl.lit(None, dtype=pl.Int32).alias(target)
    return _extract_date_part(_as_date_expression(frame, source), date_part).alias(target)


def _custom_partition_expression(
    frame: pl.DataFrame,
    configuration: Mapping[str, object],
    rule: Mapping[object, object],
    target: str,
    run_date: date,
) -> pl.Expr | None:
    source_type = rule.get("sourceType", "source_column")
    if source_type == "constant":
        return pl.lit(rule.get("sourceValue", "")).alias(target)
    if source_type == "run_date":
        return pl.lit(run_date.isoformat()).alias(target)
    source_value = rule.get("sourceValue")
    if source_type == "source_column" and isinstance(source_value, str):
        source = _resolve_working_column(frame, configuration, source_value)
        return pl.col(source).alias(target) if source is not None else None
    return None


def _resolve_working_column(
    frame: pl.DataFrame,
    configuration: Mapping[str, object],
    requested: str,
) -> str | None:
    if requested in frame.columns:
        return requested
    contract = configuration.get("canonicalMapping")
    if not isinstance(contract, Mapping):
        return None
    mappings = contract.get("columns")
    if not isinstance(mappings, list):
        return None
    for raw_mapping in mappings:
        if not isinstance(raw_mapping, Mapping):
            continue
        if raw_mapping.get("targetColumn") != requested or raw_mapping.get("sourceKind") != "column":
            continue
        source = raw_mapping.get("sourceColumn")
        if isinstance(source, str) and source != requested and source in frame.columns:
            return source
    return None


def _fallback_date_expression(
    frame: pl.DataFrame,
    configuration: Mapping[str, object],
) -> pl.Expr | None:
    for candidate in DATE_CANDIDATES:
        source = _resolve_working_column(frame, configuration, candidate)
        if source is not None:
            return _as_date_expression(frame, source)
    return None


def _as_date_expression(frame: pl.DataFrame, source: str) -> pl.Expr:
    expression = pl.col(source)
    data_type = frame.schema[source]
    if data_type in {pl.Date, pl.Datetime}:
        return expression
    str_expr = expression.cast(pl.String).str.strip_chars()
    return pl.coalesce([
        str_expr.str.to_date("%Y-%m-%d", strict=False),
        str_expr.str.to_date("%m/%d/%Y", strict=False),
        str_expr.str.to_date("%Y%m%d", strict=False),
        str_expr.str.to_date("%d/%m/%Y", strict=False),
    ])


def _extract_date_part(expression: pl.Expr, date_part: str) -> pl.Expr:
    if date_part == "year":
        return expression.dt.year().cast(pl.Int32)
    if date_part == "month":
        return expression.dt.month().cast(pl.Int32)
    return expression.dt.day().cast(pl.Int32)
