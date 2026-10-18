"""Secure compilation of DSL 2.0 into Polars LazyFrame expressions."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime
from functools import reduce
from typing import Any, Literal, Mapping

import polars as pl

from .models import ErrorAction, Expression, ParserPipeline, PipelineStep


ERROR_CODES_COLUMN = "__isa_error_codes"
QUARANTINE_COLUMN = "__isa_quarantine"
DROP_COLUMN = "__isa_drop"


class PipelineCompileError(ValueError):
    """Raised when a structurally valid DSL cannot be safely compiled."""


@dataclass
class CompileResult:
    frame: pl.LazyFrame
    named_frames: dict[str, pl.LazyFrame]
    quarantine_frames: list[pl.LazyFrame] = field(default_factory=list)
    fail_frames: list[tuple[str, pl.LazyFrame]] = field(default_factory=list)
    profile_requests: list[dict[str, Any]] = field(default_factory=list)
    materialization_steps: list[str] = field(default_factory=list)

    def valid_frame(self) -> pl.LazyFrame:
        return self.frame.filter(~pl.col(QUARANTINE_COLUMN)).filter(~pl.col(DROP_COLUMN))

    def quarantine_frame(self) -> pl.LazyFrame:
        if not self.quarantine_frames:
            return self.frame.limit(0)
        return pl.concat(self.quarantine_frames, how="diagonal_relaxed")


PolarsDataType = pl.DataType | type[pl.DataType]
KeepStrategy = Literal["first", "last", "any", "none"]
FillNullStrategy = Literal["forward", "backward", "min", "max", "mean", "zero", "one"]
ConcatStrategy = Literal[
    "vertical", "vertical_relaxed", "diagonal", "diagonal_relaxed", "horizontal",
    "horizontal_extend", "align", "align_full", "align_inner", "align_left", "align_right",
]


TYPE_MAP: dict[str, PolarsDataType] = {
    "string": pl.String,
    "boolean": pl.Boolean,
    "int8": pl.Int8,
    "int16": pl.Int16,
    "int32": pl.Int32,
    "int64": pl.Int64,
    "uint8": pl.UInt8,
    "uint16": pl.UInt16,
    "uint32": pl.UInt32,
    "uint64": pl.UInt64,
    "float32": pl.Float32,
    "float64": pl.Float64,
    "decimal(18,2)": pl.Decimal(18, 2),
    "date": pl.Date,
    "datetime": pl.Datetime,
    "time": pl.Time,
    "binary": pl.Binary,
    "json": pl.String,
}


class PolarsPipelineCompiler:
    """Compiles only allowlisted operations/functions; never evaluates source text."""

    MAX_EXPRESSION_DEPTH = 32
    MAX_CROSS_JOIN_ROWS = 1_000_000
    MAX_TRANSPOSE_CELLS = 1_000_000

    def __init__(self, pipeline: ParserPipeline, runtime_parameters: Mapping[str, Any] | None = None):
        self.pipeline = pipeline
        self.parameters = {parameter.name: parameter.default for parameter in pipeline.parameters}
        self.parameters.update(runtime_parameters or {})

    def compile(self, inputs: Mapping[str, pl.LazyFrame]) -> CompileResult:
        missing = {item.id for item in self.pipeline.inputs} - set(inputs)
        if missing:
            raise PipelineCompileError(f"missing input bindings: {', '.join(sorted(missing))}")

        frames = dict(inputs)
        current = self._with_telemetry_columns(frames["main"])
        preexisting_quarantine = current.filter(pl.col(QUARANTINE_COLUMN))
        current = current.filter(~pl.col(QUARANTINE_COLUMN))
        result = CompileResult(
            frame=current,
            named_frames=frames,
            quarantine_frames=[preexisting_quarantine],
        )

        for step in self.pipeline.steps:
            if not step.enabled:
                continue
            current = self._compile_step(current, step, result)
            result.frame = current

        return result

    @staticmethod
    def _with_telemetry_columns(frame: pl.LazyFrame) -> pl.LazyFrame:
        names = set(frame.collect_schema().names())
        expressions: list[pl.Expr] = []
        if ERROR_CODES_COLUMN not in names:
            expressions.append(pl.lit([], dtype=pl.List(pl.String)).alias(ERROR_CODES_COLUMN))
        if QUARANTINE_COLUMN not in names:
            expressions.append(pl.lit(False).alias(QUARANTINE_COLUMN))
        if DROP_COLUMN not in names:
            expressions.append(pl.lit(False).alias(DROP_COLUMN))
        return frame.with_columns(expressions) if expressions else frame

    def expression(self, raw: Mapping[str, Any] | Expression, depth: int = 0) -> pl.Expr:
        if depth > self.MAX_EXPRESSION_DEPTH:
            raise PipelineCompileError("expression nesting exceeds 32 levels")
        node = raw if isinstance(raw, Expression) else Expression.model_validate(raw)

        if node.node == "column":
            return pl.col(node.name)  # type: ignore[arg-type]
        if node.node == "literal":
            return pl.lit(self._literal_value(node.value, node.dataType), dtype=TYPE_MAP[node.dataType])  # type: ignore[index]
        if node.node == "parameter":
            if node.name not in self.parameters:
                raise PipelineCompileError(f"runtime parameter '{node.name}' is not declared")
            return pl.lit(self.parameters[node.name])
        if node.node in {"all", "any"}:
            compiled = [self.expression(child, depth + 1) for child in node.children or []]
            operator = (lambda left, right: left & right) if node.node == "all" else (lambda left, right: left | right)
            return reduce(operator, compiled)
        if node.node == "not":
            return ~self.expression(node.expression, depth + 1)  # type: ignore[arg-type]
        if node.node == "isNull":
            return self.expression(node.expression, depth + 1).is_null()  # type: ignore[arg-type]
        if node.node == "isNotNull":
            return self.expression(node.expression, depth + 1).is_not_null()  # type: ignore[arg-type]
        if node.node == "compare":
            return self._comparison(node, depth)
        if node.node == "arithmetic":
            return self._arithmetic(node, depth)
        if node.node == "call":
            return self._call(node, depth)
        if node.node == "case":
            return self._case(node, depth)
        raise PipelineCompileError(f"unsupported expression node '{node.node}'")

    def _comparison(self, node: Expression, depth: int) -> pl.Expr:
        left = self.expression(node.left, depth + 1)  # type: ignore[arg-type]
        right = self.expression(node.right, depth + 1)  # type: ignore[arg-type]
        op = node.operator
        comparisons = {
            "eq": lambda: left == right,
            "ne": lambda: left != right,
            "gt": lambda: left > right,
            "gte": lambda: left >= right,
            "lt": lambda: left < right,
            "lte": lambda: left <= right,
            "contains": lambda: left.cast(pl.String).str.contains(right, literal=True),
            "startsWith": lambda: left.cast(pl.String).str.starts_with(right),
            "endsWith": lambda: left.cast(pl.String).str.ends_with(right),
            "matches": lambda: left.cast(pl.String).str.contains(right),
        }
        if op in comparisons:
            return comparisons[op]()
        if op in {"in", "notIn"}:
            values = node.right.value if node.right and node.right.node == "literal" else None
            if not isinstance(values, list):
                raise PipelineCompileError(f"'{op}' requires a list literal on the right")
            expression = left.is_in(values)
            return ~expression if op == "notIn" else expression
        raise PipelineCompileError(f"unsupported comparison operator '{op}'")

    def _arithmetic(self, node: Expression, depth: int) -> pl.Expr:
        left_raw = self.expression(node.left, depth + 1)  # type: ignore[arg-type]
        left = left_raw.cast(pl.Float64, strict=False)
        right = self.expression(node.right, depth + 1).cast(pl.Float64, strict=False)  # type: ignore[arg-type]
        if (
            node.operator == "divide"
            and node.right
            and node.right.node == "literal"
            and float(node.right.value or 0) == 100.0
        ):
            has_decimal = left_raw.cast(pl.String).str.strip_chars().str.contains(r"\.")
            return pl.when(has_decimal).then(left).otherwise(left / right)
        operations = {
            "add": lambda: left + right,
            "subtract": lambda: left - right,
            "multiply": lambda: left * right,
            "divide": lambda: left / right,
            "modulo": lambda: left % right,
        }
        if node.operator not in operations:
            raise PipelineCompileError(f"unsupported arithmetic operator '{node.operator}'")
        return operations[node.operator]()

    def _call(self, node: Expression, depth: int) -> pl.Expr:
        args = [self.expression(argument, depth + 1) for argument in node.arguments or []]
        name = node.function
        if not args:
            raise PipelineCompileError(f"function '{name}' requires arguments")
        functions = {
            "upper": lambda: args[0].cast(pl.String).str.to_uppercase(),
            "lower": lambda: args[0].cast(pl.String).str.to_lowercase(),
            "title": lambda: args[0].cast(pl.String).str.to_titlecase(),
            "trim": lambda: args[0].cast(pl.String).str.strip_chars(),
            "length": lambda: args[0].cast(pl.String).str.len_chars(),
            "abs": lambda: args[0].abs(),
            "round": lambda: args[0].round(int(node.options.get("decimals", 0))),
            "coalesce": lambda: pl.coalesce(args),
            "year": lambda: args[0].dt.year(),
            "month": lambda: args[0].dt.month(),
            "day": lambda: args[0].dt.day(),
            "quarter": lambda: args[0].dt.quarter(),
        }
        if name not in functions:
            raise PipelineCompileError(f"function '{name}' is not allowlisted")
        return functions[name]()

    def _case(self, node: Expression, depth: int) -> pl.Expr:
        expression = self.expression(node.otherwise, depth + 1)  # type: ignore[arg-type]
        for branch in reversed(node.branches or []):
            expression = (
                pl.when(self.expression(branch["when"], depth + 1))
                .then(self.expression(branch["then"], depth + 1))
                .otherwise(expression)
            )
        return expression

    def _compile_step(self, frame: pl.LazyFrame, step: PipelineStep, result: CompileResult) -> pl.LazyFrame:
        args = step.args
        op = step.op
        if op == "select":
            columns = self._strings(args, "columns")
            telemetry = [name for name in (ERROR_CODES_COLUMN, QUARANTINE_COLUMN, DROP_COLUMN) if name not in columns]
            return frame.select([*columns, *telemetry])
        if op == "dropColumns":
            protected = {ERROR_CODES_COLUMN, QUARANTINE_COLUMN, DROP_COLUMN}
            columns = [column for column in self._strings(args, "columns") if column not in protected]
            return frame.drop(columns, strict=bool(args.get("strict", True)))
        if op == "rename":
            return frame.rename(self._mapping(args, "columns"))
        if op == "cast":
            return self._cast(frame, step, result)
        if op == "derive":
            return frame.with_columns(self.expression(self._required(args, "expression")).alias(self._string(args, "target")))
        if op == "filter":
            return frame.filter(self.expression(self._required(args, "predicate")).fill_null(False))
        if op == "dropNullRows":
            return frame.drop_nulls(subset=self._strings(args, "columns", required=False) or None)
        if op == "fillNull":
            return self._fill_null(frame, args)
        if op == "textFormat":
            return self._text_format(frame, args)
        if op == "trimCharacters":
            return self._trim_characters(frame, args)
        if op == "replaceText":
            return self._replace_text(frame, args)
        if op == "mask":
            return self._mask(frame, step, result)
        if op == "substring":
            source, target = self._source_target(args)
            return frame.with_columns(pl.col(source).cast(pl.String).str.slice(int(args.get("offset", 0)), args.get("length")).alias(target))
        if op == "regexExtract":
            source, target = self._source_target(args)
            pattern = self._safe_regex(args)
            source_expr = pl.col(source).cast(pl.String)
            extracted = source_expr.str.extract(pattern, int(args.get("group", 1)))
            return self._checked_value(frame, step, result, source, target, extracted)
        if op == "split":
            return self._split(frame, args)
        if op == "concatColumns":
            return frame.with_columns(pl.concat_str(self._strings(args, "columns"), separator=str(args.get("separator", "")), ignore_nulls=bool(args.get("ignoreNulls", False))).alias(self._string(args, "target")))
        if op == "deduplicate":
            raw_keep = str(args.get("keep", "first"))
            keep_options: dict[str, KeepStrategy] = {
                "first": "first", "last": "last", "any": "any", "none": "none",
            }
            if raw_keep not in keep_options:
                raise PipelineCompileError(f"unsupported deduplication keep strategy '{raw_keep}'")
            return frame.unique(subset=self._strings(args, "columns", required=False) or None, keep=keep_options[raw_keep], maintain_order=bool(args.get("maintainOrder", False)))
        if op == "sort":
            by = self._strings(args, "columns")
            descending = args.get("descending", False)
            return frame.sort(by, descending=descending, nulls_last=bool(args.get("nullsLast", False)))
        if op == "groupAggregate":
            aggregations = [self._aggregate(item) for item in self._list_of_mappings(args, "aggregations")]
            return frame.group_by(self._strings(args, "keys"), maintain_order=bool(args.get("maintainOrder", False))).agg(aggregations)
        if op == "unpivot":
            return frame.unpivot(on=self._strings(args, "columns", required=False) or None, index=self._strings(args, "index", required=False) or None, variable_name=args.get("variableName"), value_name=args.get("valueName"))
        if op == "pivot":
            on_columns = args.get("onColumns")
            if not isinstance(on_columns, list) or not on_columns:
                raise PipelineCompileError("lazy pivot requires an explicit, non-empty onColumns list")
            if len(on_columns) > 500:
                raise PipelineCompileError("pivot output is limited to 500 declared columns")
            return frame.pivot(
                on=self._strings(args, "on"),
                on_columns=on_columns,
                index=self._strings(args, "index", required=False) or None,
                values=self._strings(args, "values", required=False) or None,
                aggregate_function=args.get("aggregateFunction"),
                maintain_order=bool(args.get("maintainOrder", False)),
            )
        if op == "join":
            return self._join(frame, args, result)
        if op == "union":
            return self._union(frame, args, result)
        if op == "parseDateTime":
            return self._parse_datetime(frame, step, result)
        if op == "datePart":
            return self._date_part(frame, args)
        if op == "duration":
            return self._duration(frame, args)
        if op == "parseJsonColumn":
            return self._parse_json_column(frame, args)
        if op == "unnestStruct":
            return frame.unnest(self._strings(args, "columns"), separator=args.get("separator"))
        if op == "explodeList":
            return frame.explode(self._strings(args, "columns"))
        if op == "qualityRule":
            predicate = self.expression(self._required(args, "predicate")).fill_null(False)
            return self._record_error(frame, frame, ~predicate, step, result)
        if op == "profile":
            result.profile_requests.append(dict(args, stepId=step.id))
            return frame
        if op == "transpose":
            return self._transpose(frame, args, result, step.id)
        if op == "fuzzyJoin":
            return self._fuzzy_join(frame, args, result, step.id)
        raise PipelineCompileError(f"operation '{op}' has no compiler")

    @staticmethod
    def _parse_date_expr(source_expr: pl.Expr, preferred_format: str | None = None) -> pl.Expr:
        raw_clean = source_expr.cast(pl.String).str.strip_chars().str.strip_chars_start("'")
        # Zero-pad 7-digit compact MDDYYYY dates where leading 0 was stripped (e.g. "8122026" -> "08122026")
        clean = (
            pl.when(raw_clean.str.contains(r"^\d{7}$"))
            .then(raw_clean.str.pad_start(8, "0"))
            .otherwise(raw_clean)
        )
        candidates: list[pl.Expr] = []
        if preferred_format and preferred_format not in ("%d/%m/%Y", "%d-%m-%Y"):
            candidates.append(clean.str.strptime(pl.Date, format=preferred_format, strict=False, exact=True))
        for fmt in (
            "%Y-%m-%d",
            "%m/%d/%Y",
            "%m/%d/%y",
            "%d/%m/%Y",
            "%d/%m/%y",
            "%Y/%m/%d",
            "%m-%d-%Y",
            "%d-%m-%Y",
            "%m%d%Y",
            "%Y%m%d",
            "%d%m%Y",
        ):
            if fmt != preferred_format or preferred_format in ("%d/%m/%Y", "%d-%m-%Y"):
                candidates.append(clean.str.strptime(pl.Date, format=fmt, strict=False, exact=True))
        # Handle ISO datetime strings (e.g. "2026-08-17 00:00:00")
        candidates.append(clean.str.slice(0, 10).str.strptime(pl.Date, format="%Y-%m-%d", strict=False, exact=True))
        return pl.coalesce(candidates)

    def _cast(self, frame: pl.LazyFrame, step: PipelineStep, result: CompileResult) -> pl.LazyFrame:
        source, target = self._source_target(step.args)
        data_type_name = self._string(step.args, "dataType")
        if data_type_name not in TYPE_MAP:
            raise PipelineCompileError(f"unsupported data type '{data_type_name}'")
        source_expr = pl.col(source)
        preprocess = step.args.get("preprocess", {})
        if preprocess:
            source_expr = source_expr.cast(pl.String)
            if preprocess.get("trim"):
                source_expr = source_expr.str.strip_chars().str.strip_chars_start("'")
            separator = preprocess.get("removeThousandsSeparator")
            if separator:
                source_expr = source_expr.str.replace_all(str(separator), "", literal=True)
        if data_type_name == "date":
            casted = self._parse_date_expr(source_expr)
        elif data_type_name == "decimal(18,2)":
            str_val = source_expr.cast(pl.String).str.strip_chars().str.strip_chars_start("'").str.replace_all(",", "", literal=True).str.replace_all("$", "", literal=True)
            casted = str_val.cast(pl.Decimal(18, 2), strict=False)
        elif data_type_name == "string":
            casted = source_expr.cast(pl.String).str.strip_chars().str.strip_chars_start("'")
        else:
            casted = source_expr.cast(TYPE_MAP[data_type_name], strict=False)
        return self._checked_value(frame, step, result, source, target, casted)

    def _parse_datetime(self, frame: pl.LazyFrame, step: PipelineStep, result: CompileResult) -> pl.LazyFrame:
        source, target = self._source_target(step.args)
        target_type = step.args.get("dataType", "date")
        if target_type == "date":
            parsed = self._parse_date_expr(pl.col(source), step.args.get("format"))
        else:
            source_expr = pl.col(source).cast(pl.String).str.strip_chars()
            parsed = source_expr.str.strptime(pl.Datetime, format=step.args.get("format"), strict=False, exact=bool(step.args.get("exact", True)))
        return self._checked_value(frame, step, result, source, target, parsed)

    def _checked_value(self, frame: pl.LazyFrame, step: PipelineStep, result: CompileResult,
                       source: str, target: str, value: pl.Expr) -> pl.LazyFrame:
        marker = "__isa_conversion_invalid"
        while marker in frame.collect_schema().names():
            marker += "_"
        present = pl.col(source).is_not_null() & pl.col(source).cast(pl.String).str.strip_chars().ne("")
        original = frame.with_columns((present & value.is_null()).fill_null(False).alias(marker))
        transformed = original.with_columns(value.alias(target))
        return self._record_error(original, transformed, pl.col(marker), step, result).drop(marker)

    def _record_error(
        self,
        original_frame: pl.LazyFrame,
        transformed_frame: pl.LazyFrame,
        invalid: pl.Expr,
        step: PipelineStep,
        result: CompileResult,
    ) -> pl.LazyFrame:
        policy = step.onError
        action: ErrorAction = policy.action if policy else self.pipeline.quality.defaultErrorAction
        code = policy.code if policy else f"{step.id}_FAILED"
        updated = transformed_frame.with_columns(
            pl.when(invalid)
            .then(pl.col(ERROR_CODES_COLUMN).list.concat(pl.lit([code])))
            .otherwise(pl.col(ERROR_CODES_COLUMN))
            .alias(ERROR_CODES_COLUMN),
            (pl.col(QUARANTINE_COLUMN) | (invalid if action == "quarantine" else pl.lit(False))).alias(QUARANTINE_COLUMN),
            (pl.col(DROP_COLUMN) | (invalid if action == "drop" else pl.lit(False))).alias(DROP_COLUMN),
        )
        error_rows = updated.filter(invalid)
        if action == "quarantine":
            result.quarantine_frames.append(error_rows)
            return updated.filter(~invalid)
        if action == "drop":
            return updated.filter(~invalid)
        if action == "fail":
            result.fail_frames.append((step.id, error_rows))
        if action == "keep":
            original_names = original_frame.collect_schema().names()
            transformed_names = transformed_frame.collect_schema().names()
            added = [name for name in transformed_names if name not in original_names]
            if added:
                # A keep policy retains the row and exposes the failed target as null;
                # the untouched source column remains available for intervention.
                return updated
        return updated

    def _fill_null(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        columns = self._strings(args, "columns")
        strategy = str(args.get("strategy", "value"))
        if strategy == "value":
            expressions = [pl.col(column).fill_null(args.get("value")) for column in columns]
        elif strategy in {"forward", "backward", "min", "max", "mean", "zero", "one"}:
            strategy_options: dict[str, FillNullStrategy] = {
                "forward": "forward", "backward": "backward", "min": "min", "max": "max",
                "mean": "mean", "zero": "zero", "one": "one",
            }
            expressions = [pl.col(column).fill_null(strategy=strategy_options[strategy]) for column in columns]
        else:
            raise PipelineCompileError(f"unsupported null fill strategy '{strategy}'")
        return frame.with_columns(expressions)

    def _text_format(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        columns = self._strings(args, "columns")
        mode = self._string(args, "mode")
        expressions = []
        for column in columns:
            expression = pl.col(column).cast(pl.String)
            if mode == "upper": expression = expression.str.to_uppercase()
            elif mode == "lower": expression = expression.str.to_lowercase()
            elif mode == "title": expression = expression.str.to_titlecase()
            elif mode == "trim": expression = expression.str.strip_chars()
            elif mode == "stripNonPrintable": expression = expression.str.replace_all(r"[\p{Cc}\p{Cf}]", "")
            elif mode == "pad":
                length = int(args.get("length", 0))
                pad = str(args.get("padChar", "0")) or "0"
                side = str(args.get("side", "left"))
                if side == "right":
                    expression = expression.str.pad_end(length, pad[0])
                else:
                    expression = expression.str.pad_start(length, pad[0])
            else: raise PipelineCompileError(f"unsupported text format '{mode}'")
            expressions.append(expression.alias(column))
        return frame.with_columns(expressions)

    def _trim_characters(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        source, target = self._source_target(args)
        characters = self._string(args, "characters")
        side = str(args.get("side", "both"))
        expression = pl.col(source).cast(pl.String)
        if side == "start":
            expression = expression.str.strip_chars_start(characters)
        elif side == "end":
            expression = expression.str.strip_chars_end(characters)
        elif side == "both":
            expression = expression.str.strip_chars(characters)
        else:
            raise PipelineCompileError(f"unsupported trim side '{side}'")
        if bool(args.get("emptyAsNull", False)):
            expression = pl.when(expression == "").then(pl.lit(None, dtype=pl.String)).otherwise(expression)
        return frame.with_columns(expression.alias(target))

    def _replace_text(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        source, target = self._source_target(args)
        pattern = self._string(args, "pattern")
        replacement = str(args.get("replacement", ""))
        expression = pl.col(source).cast(pl.String)
        if bool(args.get("regex", False)):
            self._safe_regex({"pattern": pattern})
            expression = expression.str.replace_all(pattern, replacement) if bool(args.get("all", True)) else expression.str.replace(pattern, replacement)
        else:
            expression = expression.str.replace_all(pattern, replacement, literal=True) if bool(args.get("all", True)) else expression.str.replace(pattern, replacement, literal=True)
        return frame.with_columns(expression.alias(target))

    def _mask(self, frame: pl.LazyFrame, step: PipelineStep, result: CompileResult) -> pl.LazyFrame:
        source, target = self._source_target(step.args)
        keep_start = int(step.args.get("keepStart", 0))
        keep_end = int(step.args.get("keepEnd", 0))
        mask_character = str(step.args.get("maskCharacter", "*"))
        if keep_start < 0 or keep_end < 0:
            raise PipelineCompileError("mask keepStart and keepEnd cannot be negative")
        if len(mask_character) != 1:
            raise PipelineCompileError("maskCharacter must contain exactly one character")
        raw_str = pl.col(source).cast(pl.String).str.strip_chars().str.strip_chars_start("'")
        source_expr = (
            pl.when(raw_str.str.contains(r"^000\d{16,17}$"))
            .then(raw_str.str.slice(-16))
            .otherwise(raw_str)
        )
        length = source_expr.str.len_chars().cast(pl.Int64)
        invalid = source_expr.is_not_null() & (length < keep_start + keep_end)
        middle_length = (
            pl.when(invalid | length.is_null())
            .then(0)
            .otherwise((length - keep_start - keep_end).clip(lower_bound=0))
            .cast(pl.UInt32)
        )
        suffix = source_expr.str.slice(-keep_end) if keep_end else pl.lit("")
        masked = pl.concat_str([
            source_expr.str.slice(0, keep_start),
            pl.lit("").str.pad_start(middle_length, mask_character),
            suffix,
        ])
        # Freeze the predicate before overwriting the source. Re-evaluating it on
        # the null target would silently erase the failure and quarantine flag.
        invalid_column = "__isa_mask_invalid"
        while invalid_column in frame.collect_schema().names():
            invalid_column += "_"
        frame = frame.with_columns(invalid.fill_null(False).alias(invalid_column))
        transformed = frame.with_columns(
            pl.when(invalid).then(pl.lit(None, dtype=pl.String)).otherwise(masked).alias(target)
        )
        return self._record_error(frame, transformed, pl.col(invalid_column), step, result).drop(invalid_column)

    def _split(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        source = self._string(args, "source")
        delimiter = self._string(args, "delimiter")
        split_expr = pl.col(source).cast(pl.String).str.split(delimiter)
        targets = self._strings(args, "targets", required=False)
        if not targets:
            return frame.with_columns(split_expr.alias(str(args.get("target", source))))
        return frame.with_columns([split_expr.list.get(index, null_on_oob=True).alias(target) for index, target in enumerate(targets)])

    def _join(self, frame: pl.LazyFrame, args: Mapping[str, Any], result: CompileResult) -> pl.LazyFrame:
        right_id = self._string(args, "rightInput")
        if right_id not in result.named_frames:
            raise PipelineCompileError(f"join input '{right_id}' is not bound")
        how = str(args.get("how", "inner"))
        if how == "rightAnti":
            return result.named_frames[right_id].join(frame, left_on=self._strings(args, "rightOn"), right_on=self._strings(args, "leftOn"), how="anti")
        how_map = {"inner": "inner", "left": "left", "right": "right", "full": "full", "leftAnti": "anti", "cross": "cross"}
        if how not in how_map:
            raise PipelineCompileError(f"unsupported join type '{how}'")
        options: dict[str, Any] = {"how": how_map[how], "suffix": str(args.get("suffix", "_right"))}
        if how != "cross":
            options.update(left_on=self._strings(args, "leftOn"), right_on=self._strings(args, "rightOn"), validate=str(args.get("validate", "m:m")), nulls_equal=bool(args.get("nullsEqual", False)))
        else:
            left_count, right_count = pl.collect_all([
                frame.select(pl.len().alias("count")),
                result.named_frames[right_id].select(pl.len().alias("count")),
            ])
            product = int(left_count.item(0, "count")) * int(right_count.item(0, "count"))
            maximum = int(args.get("maxOutputRows", self.MAX_CROSS_JOIN_ROWS))
            if product > maximum:
                raise PipelineCompileError(f"cross join would produce {product} rows; configured maximum is {maximum}")
        return frame.join(result.named_frames[right_id], **options)

    def _union(self, frame: pl.LazyFrame, args: Mapping[str, Any], result: CompileResult) -> pl.LazyFrame:
        input_ids = self._strings(args, "inputs")
        missing = set(input_ids) - set(result.named_frames)
        if missing:
            raise PipelineCompileError(f"union inputs are not bound: {', '.join(sorted(missing))}")
        frames = [frame, *(result.named_frames[input_id] for input_id in input_ids)]
        raw_how = str(args.get("how", "diagonal_relaxed"))
        concat_options: dict[str, ConcatStrategy] = {
            "vertical": "vertical",
            "vertical_relaxed": "vertical_relaxed",
            "diagonal": "diagonal",
            "diagonal_relaxed": "diagonal_relaxed",
            "horizontal": "horizontal",
            "horizontal_extend": "horizontal_extend",
            "align": "align",
            "align_full": "align_full",
            "align_inner": "align_inner",
            "align_left": "align_left",
            "align_right": "align_right",
        }
        if raw_how not in concat_options:
            raise PipelineCompileError(f"unsupported union strategy '{raw_how}'")
        return pl.concat(frames, how=concat_options[raw_how])

    def _aggregate(self, args: Mapping[str, Any]) -> pl.Expr:
        function = self._string(args, "function")
        source = args.get("source")
        target = self._string(args, "target")
        if function in {"count", "len"} and not source:
            return pl.len().alias(target)
        expression = pl.col(str(source))
        aggregations = {
            "sum": expression.sum, "count": expression.count, "mean": expression.mean,
            "min": expression.min, "max": expression.max, "median": expression.median,
            "std": expression.std, "nUnique": expression.n_unique, "first": expression.first,
            "last": expression.last,
        }
        if function not in aggregations:
            raise PipelineCompileError(f"unsupported aggregation '{function}'")
        return aggregations[function]().alias(target)

    def _date_part(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        source, target = self._source_target(args)
        part = self._string(args, "part")
        expression = pl.col(source)
        parts = {"year": expression.dt.year, "month": expression.dt.month, "day": expression.dt.day, "quarter": expression.dt.quarter, "weekday": expression.dt.weekday, "hour": expression.dt.hour, "minute": expression.dt.minute}
        if part not in parts:
            raise PipelineCompileError(f"unsupported date part '{part}'")
        return frame.with_columns(parts[part]().alias(target))

    def _duration(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        duration = pl.col(self._string(args, "end")) - pl.col(self._string(args, "start"))
        unit = str(args.get("unit", "days"))
        functions = {"days": duration.dt.total_days, "hours": duration.dt.total_hours, "minutes": duration.dt.total_minutes, "seconds": duration.dt.total_seconds, "milliseconds": duration.dt.total_milliseconds}
        if unit not in functions:
            raise PipelineCompileError(f"unsupported duration unit '{unit}'")
        return frame.with_columns(functions[unit]().alias(self._string(args, "target")))

    def _parse_json_column(self, frame: pl.LazyFrame, args: Mapping[str, Any]) -> pl.LazyFrame:
        fields = self._list_of_mappings(args, "fields")
        struct_fields = []
        for field_definition in fields:
            name = self._string(field_definition, "name")
            type_name = self._string(field_definition, "dataType")
            if type_name not in TYPE_MAP:
                raise PipelineCompileError(f"unsupported JSON field data type '{type_name}'")
            struct_fields.append(pl.Field(name, TYPE_MAP[type_name]))
        dtype: pl.DataType = pl.Struct(struct_fields)
        if bool(args.get("isArray", False)):
            dtype = pl.List(dtype)
        source, target = self._source_target(args)
        return frame.with_columns(
            pl.col(source).cast(pl.String).str.json_decode(dtype=dtype).alias(target)
        )

    def _transpose(
        self,
        frame: pl.LazyFrame,
        args: Mapping[str, Any],
        result: CompileResult,
        step_id: str,
    ) -> pl.LazyFrame:
        schema_names = frame.collect_schema().names()
        data_columns = [name for name in schema_names if not name.startswith("__isa_")]
        row_count_frame = frame.select(pl.len().alias("count")).collect()
        row_count = int(row_count_frame.item(0, "count"))
        cells = row_count * len(data_columns)
        maximum = int(args.get("maxCells", self.MAX_TRANSPOSE_CELLS))
        if cells > maximum:
            raise PipelineCompileError(f"transpose requires {cells} cells; configured maximum is {maximum}")
        materialized = frame.select(data_columns).collect().transpose(
            include_header=bool(args.get("includeHeader", False)),
            header_name=str(args.get("headerName", "column")),
            column_names=args.get("columnNames"),
        )
        result.materialization_steps.append(step_id)
        return self._with_telemetry_columns(materialized.lazy())

    def _fuzzy_join(
        self,
        frame: pl.LazyFrame,
        args: Mapping[str, Any],
        result: CompileResult,
        step_id: str,
    ) -> pl.LazyFrame:
        try:
            from rapidfuzz import fuzz
        except ImportError as exception:
            raise PipelineCompileError("fuzzyJoin requires the pinned rapidfuzz worker dependency") from exception
        right_id = self._string(args, "rightInput")
        if right_id not in result.named_frames:
            raise PipelineCompileError(f"fuzzy join input '{right_id}' is not bound")
        left_on = self._string(args, "leftOn")
        right_on = self._string(args, "rightOn")
        threshold = float(args.get("threshold", 85.0))
        block_on = self._strings(args, "blockOn", required=False)
        left_df, right_df = pl.collect_all([frame, result.named_frames[right_id]])
        maximum_candidates = int(args.get("maxCandidatePairs", 1_000_000))
        suffix = str(args.get("suffix", "_right"))
        matches: list[dict[str, Any]] = []
        right_rows = right_df.to_dicts()
        candidate_pairs = 0
        for left_row in left_df.to_dicts():
            candidates = [
                row for row in right_rows
                if not block_on or all(left_row.get(column) == row.get(column) for column in block_on)
            ]
            candidate_pairs += len(candidates)
            if candidate_pairs > maximum_candidates:
                raise PipelineCompileError("fuzzy join candidate limit exceeded; add or strengthen blocking keys")
            scored = []
            left_value = str(left_row.get(left_on) or "")
            for right_row in candidates:
                score = float(fuzz.ratio(left_value, str(right_row.get(right_on) or "")))
                if score >= threshold:
                    scored.append((score, right_row))
            scored.sort(key=lambda item: item[0], reverse=True)
            selected = scored if args.get("keep", "best") == "all" else scored[:1]
            for score, right_row in selected:
                merged = dict(left_row)
                for key, value in right_row.items():
                    merged[key if key not in merged else f"{key}{suffix}"] = value
                merged[str(args.get("scoreColumn", "fuzzy_score"))] = score
                matches.append(merged)
        result.materialization_steps.append(step_id)
        return self._with_telemetry_columns(pl.DataFrame(matches).lazy())

    @staticmethod
    def _literal_value(value: Any, data_type: str | None) -> Any:
        if data_type == "date" and isinstance(value, str): return date.fromisoformat(value)
        if data_type == "datetime" and isinstance(value, str): return datetime.fromisoformat(value.replace("Z", "+00:00"))
        return value

    @staticmethod
    def _required(args: Mapping[str, Any], key: str) -> Any:
        if key not in args:
            raise PipelineCompileError(f"missing required argument '{key}'")
        return args[key]

    @classmethod
    def _string(cls, args: Mapping[str, Any], key: str) -> str:
        value = cls._required(args, key)
        if not isinstance(value, str) or not value:
            raise PipelineCompileError(f"argument '{key}' must be a non-empty string")
        return value

    @classmethod
    def _strings(cls, args: Mapping[str, Any], key: str, required: bool = True) -> list[str]:
        value = cls._required(args, key) if required else args.get(key, [])
        if not isinstance(value, list) or not all(isinstance(item, str) and item for item in value):
            raise PipelineCompileError(f"argument '{key}' must be a list of strings")
        return value

    @classmethod
    def _mapping(cls, args: Mapping[str, Any], key: str) -> dict[str, str]:
        value = cls._required(args, key)
        if not isinstance(value, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in value.items()):
            raise PipelineCompileError(f"argument '{key}' must map strings to strings")
        return value

    @classmethod
    def _list_of_mappings(cls, args: Mapping[str, Any], key: str) -> list[dict[str, Any]]:
        value = cls._required(args, key)
        if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
            raise PipelineCompileError(f"argument '{key}' must be a list of objects")
        return value

    @classmethod
    def _source_target(cls, args: Mapping[str, Any]) -> tuple[str, str]:
        source = cls._string(args, "source")
        target = args.get("target", source)
        if not isinstance(target, str) or not target:
            raise PipelineCompileError("target must be a non-empty string")
        return source, target

    @staticmethod
    def _safe_regex(args: Mapping[str, Any]) -> str:
        pattern = args.get("pattern")
        if not isinstance(pattern, str) or not pattern:
            raise PipelineCompileError("regex pattern must be a non-empty string")
        if len(pattern) > 1000:
            raise PipelineCompileError("regex pattern exceeds 1000 characters")
        return pattern
