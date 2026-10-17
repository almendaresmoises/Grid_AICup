"""Pydantic contract for parser-pipeline DSL 2.x."""

from __future__ import annotations

from typing import Any, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


DataTypeName = Literal[
    "string", "boolean", "int8", "int16", "int32", "int64",
    "uint8", "uint16", "uint32", "uint64", "float32", "float64",
    "decimal(18,2)", "date", "datetime", "time", "binary", "json",
]
ErrorAction = Literal["fail", "quarantine", "null", "keep", "drop"]


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", populate_by_name=True)


class PipelineIdentity(StrictModel):
    id: UUID
    revision: int = Field(ge=1)
    name: str = Field(min_length=1, max_length=200, pattern=r"^[A-Za-z_][A-Za-z0-9_.-]*$")


class PipelineParameter(StrictModel):
    name: str = Field(min_length=1, max_length=200)
    dataType: DataTypeName
    default: Any = None


class ColumnSchema(StrictModel):
    name: str = Field(min_length=1, max_length=200)
    dataType: DataTypeName
    nullable: bool = True
    format: str | None = Field(default=None, max_length=100)


class InputSchema(StrictModel):
    mode: Literal["strict", "permissive", "infer"] = "permissive"
    columns: list[ColumnSchema] = Field(default_factory=list, max_length=2000)


class ContainerSettings(StrictModel):
    """Optional archive envelope around a logical parser input."""

    type: Literal["none", "zip"] = "none"
    entryPattern: str = Field(default="*", min_length=1, max_length=500)
    onMultiple: Literal["fail", "first", "all"] = "fail"
    maxEntries: int = Field(default=100, ge=1, le=10_000)
    maxUncompressedBytes: int = Field(default=100_000_000, ge=1, le=2_000_000_000)
    maxCompressionRatio: float = Field(default=200.0, ge=1, le=10_000)


class RecordPolicy(StrictModel):
    """Rules applied to text records before the configured reader runs."""

    skipBlankRows: bool = True
    ignorePrefixes: list[str] = Field(default_factory=list, max_length=100)
    expectedFieldCount: int | None = Field(default=None, ge=1, le=10_000)
    raggedRows: Literal["fail", "drop"] = "fail"


class PipelineInput(StrictModel):
    id: str = Field(min_length=1, max_length=200)
    connector: Literal[
        "delta", "csv", "delimited", "fixedWidth", "ndjson", "json",
        "parquet", "excel", "xml", "database",
    ]
    binding: str = Field(min_length=1, max_length=500)
    options: dict[str, Any] = Field(default_factory=dict)
    container: ContainerSettings = Field(default_factory=ContainerSettings)
    recordPolicy: RecordPolicy = Field(default_factory=RecordPolicy)
    schema_: InputSchema = Field(default_factory=InputSchema, alias="schema")


class Expression(StrictModel):
    """Validated recursive expression node; node-specific shape is checked below."""

    node: Literal[
        "column", "literal", "parameter", "compare", "all", "any", "not",
        "isNull", "isNotNull", "arithmetic", "call", "case",
    ]
    name: str | None = None
    dataType: DataTypeName | None = None
    value: Any = None
    operator: str | None = None
    left: Expression | None = None
    right: Expression | None = None
    children: list[Expression] | None = None
    expression: Expression | None = None
    function: str | None = None
    arguments: list[Expression] | None = None
    options: dict[str, Any] = Field(default_factory=dict)
    branches: list[dict[str, Any]] | None = None
    otherwise: Expression | None = None
    caseSensitive: bool = True

    @model_validator(mode="after")
    def validate_node_shape(self) -> Expression:
        if self.node in {"column", "parameter"} and not self.name:
            raise ValueError(f"{self.node} requires name")
        if self.node == "literal" and self.dataType is None:
            raise ValueError("literal requires dataType")
        if self.node in {"compare", "arithmetic"} and (not self.operator or self.left is None or self.right is None):
            raise ValueError(f"{self.node} requires operator, left, and right")
        if self.node in {"all", "any"} and not self.children:
            raise ValueError(f"{self.node} requires at least one child")
        if self.node in {"not", "isNull", "isNotNull"} and self.expression is None:
            raise ValueError(f"{self.node} requires expression")
        if self.node == "call" and (not self.function or self.arguments is None):
            raise ValueError("call requires function and arguments")
        if self.node == "case" and (not self.branches or self.otherwise is None):
            raise ValueError("case requires branches and otherwise")
        return self


class ErrorPolicy(StrictModel):
    action: ErrorAction
    code: str = Field(min_length=1, max_length=200)
    message: str | None = Field(default=None, max_length=500)


STEP_OPERATIONS = {
    "select", "dropColumns", "rename", "cast", "derive", "filter", "dropNullRows", "fillNull",
    "textFormat", "trimCharacters", "replaceText", "mask", "substring", "regexExtract", "split", "concatColumns",
    "deduplicate", "sort", "groupAggregate", "pivot", "unpivot", "transpose",
    "join", "fuzzyJoin", "union", "parseDateTime", "datePart", "duration",
    "parseJsonColumn", "unnestStruct", "explodeList", "profile", "qualityRule",
}


class PipelineStep(StrictModel):
    id: str = Field(min_length=1, max_length=200)
    op: str
    enabled: bool = True
    label: str | None = Field(default=None, max_length=200)
    args: dict[str, Any] = Field(default_factory=dict)
    onError: ErrorPolicy | None = None

    @field_validator("op")
    @classmethod
    def supported_operation(cls, value: str) -> str:
        if value not in STEP_OPERATIONS:
            raise ValueError(f"unsupported parser operation: {value}")
        return value


class FailureThreshold(StrictModel):
    quarantinePercentageGreaterThan: float | None = Field(default=None, ge=0, le=100)


class QuarantineSettings(StrictModel):
    enabled: bool = True
    relativePath: str = Field(default="_quarantine", min_length=1, max_length=200, pattern=r"^[A-Za-z0-9_./=-]+$")
    sampleLimit: int = Field(default=20, ge=0, le=100)


class QualitySettings(StrictModel):
    defaultErrorAction: ErrorAction = "quarantine"
    failExecutionWhen: FailureThreshold = Field(default_factory=FailureThreshold)
    quarantine: QuarantineSettings = Field(default_factory=QuarantineSettings)


class PipelineSink(StrictModel):
    connector: Literal["delta", "parquet", "gcs"] = "parquet"
    binding: Literal["execution.output"] = "execution.output"
    writeMode: Literal["existingIdempotent", "append", "overwrite"] = "existingIdempotent"
    partitionBy: list[str] = Field(default_factory=list, max_length=32)


class ParserPipeline(StrictModel):
    dslVersion: Literal["2.0", "2.1"]
    pipeline: PipelineIdentity
    parameters: list[PipelineParameter] = Field(default_factory=list, max_length=100)
    inputs: list[PipelineInput] = Field(min_length=1, max_length=20)
    steps: list[PipelineStep] = Field(default_factory=list, max_length=500)
    quality: QualitySettings = Field(default_factory=QualitySettings)
    sink: PipelineSink = Field(default_factory=PipelineSink)

    @model_validator(mode="after")
    def validate_unique_identifiers(self) -> ParserPipeline:
        input_ids = [item.id for item in self.inputs]
        step_ids = [item.id for item in self.steps]
        parameter_names = [item.name for item in self.parameters]
        for label, values in (("input", input_ids), ("step", step_ids), ("parameter", parameter_names)):
            if len(values) != len(set(values)):
                raise ValueError(f"duplicate {label} identifier")
        if "main" not in input_ids:
            raise ValueError("pipeline requires an input with id 'main'")
        return self
