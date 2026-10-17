"""Execution of Parser Studio applied steps for legacy dynamic parser jobs."""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from decimal import Decimal, InvalidOperation

import polars as pl

from workers.etl_worker.jobs.parsers.canonical_mapping import ReferenceResolver, reference_expression


INTEGER_TYPES = {"integer", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64"}
DECIMAL_TYPES = {"decimal", "decimal18_2", "decimal(18,2)"}
FLOAT_TYPES = {"float", "float32", "float64"}
DATE_FILTER_FORMATS = (
    "%Y-%m-%d", "%Y/%m/%d", "%Y.%m.%d",
    "%Y%m%d",
    "%m%d%Y", "%d%m%Y",
    "%m/%d/%Y", "%d/%m/%Y",
    "%m-%d-%Y", "%d-%m-%Y",
)


def apply_legacy_steps(
    frame: pl.DataFrame,
    raw_steps: object,
    reference_resolver: ReferenceResolver | None = None,
) -> pl.DataFrame:
    """
    Apply enabled Studio steps sequentially to a frame. `reference_resolver` serves LookupReference steps;
    without it their output column is empty.
    """
    if not isinstance(raw_steps, Sequence) or isinstance(raw_steps, (str, bytes)):
        return frame
    result = frame
    for raw_step in raw_steps:
        if not isinstance(raw_step, Mapping) or raw_step.get("isEnabled", True) is False:
            continue
        step_type = raw_step.get("type")
        raw_params = raw_step.get("params")
        params = raw_params if isinstance(raw_params, Mapping) else {}
        result = _apply_step(result, step_type if isinstance(step_type, str) else "", params, reference_resolver)
    return result


def _apply_step(
    frame: pl.DataFrame,
    step_type: str,
    params: Mapping[object, object],
    reference_resolver: ReferenceResolver | None,
) -> pl.DataFrame:
    source = _string(params.get("sourceColumn"))
    target = _string(params.get("targetColumn")) or source
    if step_type == "LookupReference" and target is not None:
        # Params carry mappingId / mappingName / mappingOutputColumn, the shape the resolver expects.
        lookup = {str(key): value for key, value in params.items()}
        expression = reference_expression(frame, lookup, set(frame.columns), reference_resolver)
        return frame.with_columns(expression.cast(pl.String).alias(target))

    if step_type == "ParseDate" and source in frame.columns and target is not None:
        expression = pl.col(source).cast(pl.String)
        pad_length = params.get("padStart")
        pad_character = _string(params.get("padChar")) or "0"
        if isinstance(pad_length, int) and pad_length > 0:
            expression = expression.str.pad_start(pad_length, pad_character)
        date_format = _string(params.get("format")) or "%m%d%Y"
        return frame.with_columns(expression.str.strptime(pl.Date, format=date_format, strict=False).alias(target))

    if step_type in {"FilterInvalidDate", "FilterNonDateRows"}:
        col = source if source in frame.columns else (target if target in frame.columns else None)
        if col:
            return frame.filter(_valid_date(frame, col))

    if step_type == "FilterPrefixRows":
        return _drop_keyword_rows(frame, params)

    if step_type == "TrimCharacters" and source in frame.columns and target is not None:
        characters = _string(params.get("characters")) or " "
        side = _string(params.get("side")) or "both"
        expression = pl.col(source).cast(pl.String)
        if side == "start":
            expression = expression.str.strip_chars_start(characters)
        elif side == "end":
            expression = expression.str.strip_chars_end(characters)
        else:
            expression = expression.str.strip_chars(characters)
        return frame.with_columns(expression.alias(target))

    if step_type in {"Mask", "MaskCardNumber"} and source in frame.columns and target is not None:
        keep_start = _integer(params.get("keepStart"), 6)
        keep_end = _integer(params.get("keepEnd"), 4)
        mask_character = (_string(params.get("maskCharacter")) or "*")[0]

        def mask_value(value: object) -> str:
            text = str(value).strip()
            if step_type == "MaskCardNumber":
                text = text.lstrip("0")
            hidden = len(text) - keep_start - keep_end
            if hidden < 0:
                return text
            suffix = text[-keep_end:] if keep_end > 0 else ""
            return f"{text[:keep_start]}{mask_character * hidden}{suffix}"

        return frame.with_columns(
            pl.col(source).map_elements(mask_value, return_dtype=pl.String).alias(target)
        )

    if step_type in {"ScaleNumeric", "MultiplyNumeric"} and source in frame.columns and target is not None:
        factor_value = params.get("scaleFactor", "0.01") if step_type == "ScaleNumeric" else params.get("factor", "1")
        try:
            factor = Decimal(str(factor_value))
        except InvalidOperation:
            factor = Decimal("1")
        return frame.with_columns(
            (pl.col(source).cast(pl.Decimal(38, 10), strict=False) * pl.lit(factor))
            .cast(pl.Decimal(18, 2), strict=False)
            .alias(target)
        )

    if step_type == "ChangeType" and source in frame.columns and target is not None:
        target_type = (_string(params.get("targetType")) or "string").lower()
        if target_type in INTEGER_TYPES:
            data_type: pl.DataType | type[pl.DataType] = pl.Int64
        elif target_type in DECIMAL_TYPES:
            data_type = pl.Decimal(18, 2)
        elif target_type in FLOAT_TYPES:
            data_type = pl.Float64
        elif target_type in {"boolean", "bool"}:
            data_type = pl.Boolean
        else:
            data_type = pl.String
        return frame.with_columns(pl.col(source).cast(data_type, strict=False).alias(target))

    if step_type == "DropColumns":
        raw_columns = params.get("columns")
        dropped = [column for column in raw_columns if isinstance(column, str) and column in frame.columns] \
            if isinstance(raw_columns, Sequence) and not isinstance(raw_columns, (str, bytes)) else []
        return frame.drop(dropped)

    if step_type == "PadString" and source in frame.columns and target is not None:
        length = _integer(params.get("length", params.get("padStart")), 6)
        pad_character = (_string(params.get("padChar")) or "0")[0]
        side = _string(params.get("side")) or "left"
        expression = pl.col(source).cast(pl.String).str.strip_chars()
        expression = expression.str.pad_end(length, pad_character) if side == "right" \
            else expression.str.pad_start(length, pad_character)
        return frame.with_columns(expression.alias(target))

    if step_type == "ExtractText" and source in frame.columns and target is not None:
        mode = _string(params.get("mode")) or "left"
        length = max(_integer(params.get("length"), 1), 0)
        text = pl.col(source).cast(pl.String)
        if mode == "right":
            expression = text.str.tail(length)
        elif mode == "mid":
            start = max(_integer(params.get("start"), 1), 1)
            expression = text.str.slice(start - 1, length)
        else:
            expression = text.str.head(length)
        return frame.with_columns(expression.alias(target))

    if step_type == "TrimWhitespace":
        raw_columns = params.get("columns")
        selected = [column for column in raw_columns if isinstance(column, str) and column in frame.columns] \
            if isinstance(raw_columns, Sequence) and not isinstance(raw_columns, (str, bytes)) else []
        targets = selected or [column for column in frame.columns if frame[column].dtype == pl.String]
        return frame.with_columns(pl.col(column).str.strip_chars() for column in targets)

    if step_type == "ReplaceText" and source in frame.columns and target is not None:
        pattern = _string(params.get("pattern")) or ""
        replacement = _string(params.get("replacement")) or ""
        if not pattern:
            return frame
        is_regex = params.get("isRegex") is True
        expression = pl.col(source).cast(pl.String).str.replace_all(pattern, replacement, literal=not is_regex)
        return frame.with_columns(expression.alias(target))

    if step_type == "ConditionalDerive" and source in frame.columns and target is not None:
        rules_value = params.get("rules")
        rules = rules_value if isinstance(rules_value, Sequence) else []
        expression = pl.lit(params.get("default", ""))
        valid_rules = [raw_rule for raw_rule in rules if isinstance(raw_rule, Mapping)]
        for raw_rule in reversed(valid_rules):
            condition = pl.col(source) == pl.lit(raw_rule.get("when"))
            expression = pl.when(condition).then(pl.lit(raw_rule.get("then"))).otherwise(expression)
        return frame.with_columns(expression.alias(target))

    return frame


def _valid_date(frame: pl.DataFrame, column: str) -> pl.Expr:
    """True where the column holds a date: parsed dates are non-null; text must match a known date format."""
    if frame.schema[column] in {pl.Date, pl.Datetime}:
        return pl.col(column).is_not_null()
    text = pl.col(column).cast(pl.String).str.strip_chars()
    # Strip time part if separated by space or 'T'
    date_part = text.str.split(" ").list.get(0).str.split("T").list.get(0)
    # Strip trailing decimals if formatted as float string e.g. '20260815.000000' -> '20260815'
    date_part = pl.when(date_part.str.contains(r"^\d{8}\.\d+$")).then(date_part.str.slice(0, 8)).otherwise(date_part)
    parsed = pl.coalesce([date_part.str.strptime(pl.Date, format=fmt, strict=False) for fmt in DATE_FILTER_FORMATS])
    return pl.col(column).is_not_null() & (text != "") & (text != "null") & (text != "None") & parsed.is_not_null()


def _drop_keyword_rows(frame: pl.DataFrame, params: Mapping[object, object]) -> pl.DataFrame:
    """Drop total/trailer rows: the configured column (else the first three) contains a keyword, case-insensitively."""
    raw_prefixes = params.get("prefixes")
    keywords = [
        prefix.upper() for prefix in raw_prefixes if isinstance(prefix, str) and prefix.strip()
    ] if isinstance(raw_prefixes, Sequence) and not isinstance(raw_prefixes, (str, bytes)) else []
    if not keywords or frame.width == 0:
        return frame
    column = _string(params.get("column"))
    candidates = [column] if column and column in frame.columns else frame.columns[:3]
    matches = [
        pl.col(candidate).cast(pl.String).str.to_uppercase().str.contains(keyword, literal=True).fill_null(False)
        for candidate in candidates
        for keyword in keywords
    ]
    return frame.filter(~pl.any_horizontal(matches))


def _string(value: object) -> str | None:
    return value if isinstance(value, str) else None


def _integer(value: object, default: int) -> int:
    return value if isinstance(value, int) and value >= 0 else default
