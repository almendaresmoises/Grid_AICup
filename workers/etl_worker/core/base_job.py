from __future__ import annotations

from abc import ABC, abstractmethod
from datetime import date
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, PrivateAttr


class ExecutionContext(BaseModel):
    model_config = ConfigDict(arbitrary_types_allowed=True)

    execution_date: date = Field(default_factory=date.today)
    end_date: date | None = None
    is_local: bool = False
    company_code: str | None = None
    branch_code: str | None = None
    file_path: str | None = None
    specific_file_paths: list[str] | None = None
    parameters: dict[str, Any] = Field(default_factory=dict)
    input_location: str | None = None
    output_location: str | None = None
    partition_by: list[str] | None = None
    task_id: str | None = None
    pipeline_definition: dict[str, Any] | None = None
    pipeline_revision: int | None = None
    pipeline_hash: str | None = None
    execution_mode: str = "full"
    preview_limit: int | None = None
    input_bindings: dict[str, str] = Field(default_factory=dict)

    _rows_processed: int = PrivateAttr(default=0)
    _parser_report: dict[str, Any] = PrivateAttr(default_factory=dict)

    @property
    def is_multi_branch(self) -> bool:
        return bool(self.parameters.get("is_multi_branch", False) or (not self.company_code and not self.branch_code))


class BaseJob(ABC):
    @property
    def dependencies(self) -> list[str]:
        return []

    @abstractmethod
    def execute(self, context: ExecutionContext) -> None:
        pass

    @property
    @abstractmethod
    def job_type(self) -> str:
        pass
