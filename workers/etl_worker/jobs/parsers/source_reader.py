"""Content-aware source readers for dynamic parser jobs."""

from __future__ import annotations

import io
import zipfile
from collections.abc import Mapping, Sequence
from typing import Any

import polars as pl


OLE_SIGNATURE = bytes.fromhex("D0CF11E0A1B11AE1")


def unwrap_zip_entry(content: bytes) -> tuple[bytes, str | None]:
    """If content is a generic zip archive containing a supported file, unpack the primary entry."""
    if content.startswith(b"PK\x03\x04"):
        try:
            with zipfile.ZipFile(io.BytesIO(content)) as archive:
                names = set(archive.namelist())
                if "xl/workbook.xml" in names or "xl/workbook.bin" in names:
                    return content, None  # Native Excel file
                for entry in archive.infolist():
                    if entry.is_dir():
                        continue
                    filename = entry.filename.split("/")[-1]
                    if filename.startswith(".") or entry.filename.startswith("__MACOSX/"):
                        continue
                    lower = filename.lower()
                    if lower.endswith((".csv", ".txt", ".xlsx", ".xls")):
                        return archive.read(entry), lower
        except zipfile.BadZipFile:
            pass
    return content, None


def detect_source_format(content: bytes) -> str:
    """Classify workbook containers from signatures; otherwise treat input as text."""
    if content.startswith(OLE_SIGNATURE):
        return "excel"
    if content.startswith(b"PK\x03\x04"):
        try:
            with zipfile.ZipFile(io.BytesIO(content)) as archive:
                names = set(archive.namelist())
                if "xl/workbook.xml" in names or "xl/workbook.bin" in names:
                    return "excel"
                for entry in archive.infolist():
                    if entry.is_dir():
                        continue
                    filename = entry.filename.split("/")[-1]
                    if filename.startswith(".") or entry.filename.startswith("__MACOSX/"):
                        continue
                    lower = filename.lower()
                    if lower.endswith((".xlsx", ".xls")):
                        return "excel"
                    if lower.endswith((".csv", ".txt")):
                        return "text"
        except zipfile.BadZipFile:
            pass
    return "text"


def read_excel(content: bytes, config: Mapping[str, object]) -> pl.DataFrame:
    """Read xlsx/xls/xlsb/xlsm without executing workbook macros, supporting password protection."""
    sheet_name_value = config.get("sheetName")
    sheet_name = sheet_name_value if isinstance(sheet_name_value, str) and sheet_name_value else None
    has_header = config.get("hasHeader", True) is not False
    skip_rows_value = config.get("skipRows", 0)
    skip_rows = skip_rows_value if isinstance(skip_rows_value, int) else 0
    password_value = config.get("password")
    password = str(password_value) if password_value is not None and str(password_value).strip() else None

    excel_bytes = content
    try:
        import msoffcrypto
        office_file = msoffcrypto.OfficeFile(io.BytesIO(content))
        if office_file.is_encrypted():
            if not password:
                raise ValueError("Workbook is password-protected. Please provide a password in reader configuration.")
            decrypted = io.BytesIO()
            office_file.load_key(password=password)
            office_file.decrypt(decrypted)
            excel_bytes = decrypted.getvalue()
    except Exception as e:
        if "password" in str(e).lower() or "encrypted" in str(e).lower() or isinstance(e, ValueError):
            raise
        # Unencrypted OpenXML or non-OLE file, proceed with content

    header_row_value = config.get("headerRowIndex", config.get("header_row", skip_rows if has_header else None))
    header_row = int(header_row_value) if (has_header and header_row_value is not None) else (0 if has_header else None)

    options: dict[str, object] = {
        "header_row": header_row,
    }
    # Even skip_rows=0 changes Calamine's treatment of leading blank rows.
    # Keep header coordinates consistent with Studio's used-range inspection,
    # then skip any remaining rows after the header has been read.
    result: Any = pl.read_excel(
        io.BytesIO(excel_bytes),
        sheet_name=sheet_name,
        engine="calamine",
        read_options=options,
    )
    if isinstance(result, dict):
        if not result:
            return pl.DataFrame()
        first_sheet = next(iter(result.values()))
        if isinstance(first_sheet, pl.DataFrame):
            result = first_sheet
    if isinstance(result, pl.DataFrame):
        data_start = header_row + 1 if header_row is not None else 0
        return result.slice(max(0, skip_rows - data_start))
    raise TypeError("Polars returned an unsupported Excel result")


def read_fixed_width(content: bytes, config: Mapping[str, object]) -> pl.DataFrame:
    """Read character-position fixed-width records using declared column boundaries."""
    encoding_value = config.get("encoding", "utf-8")
    encoding = encoding_value if isinstance(encoding_value, str) else "utf-8"
    definitions = config.get("fixedWidthColumns", [])
    if not isinstance(definitions, Sequence) or isinstance(definitions, (str, bytes)):
        raise ValueError("fixedWidthColumns must be a list")

    columns: list[tuple[str, int, int]] = []
    for index, definition in enumerate(definitions):
        if not isinstance(definition, Mapping):
            raise ValueError(f"Fixed-width column {index + 1} is invalid")
        name = definition.get("name")
        start = definition.get("start")
        length = definition.get("length")
        if not isinstance(name, str) or not isinstance(start, int) or not isinstance(length, int):
            raise ValueError(f"Fixed-width column {index + 1} requires name, start, and length")
        if start < 0 or length < 1:
            raise ValueError(f"Fixed-width column {name} has an invalid boundary")
        columns.append((name, start, length))

    if not columns:
        raise ValueError("At least one fixed-width column is required")
    records: list[dict[str, str]] = []
    for raw_line in content.decode(encoding, errors="replace").splitlines():
        if raw_line.strip():
            records.append({name: raw_line[start:start + length].strip() for name, start, length in columns})
    return pl.DataFrame(records)


def read_delimited(content: bytes, config: Mapping[str, object]) -> pl.DataFrame:
    """Read delimited text using an explicitly detected or user-selected delimiter."""
    delimiter_value = config.get("delimiter", ",")
    delimiter = delimiter_value if isinstance(delimiter_value, str) else ","
    if len(delimiter) != 1:
        raise ValueError("Delimited readers require a one-character delimiter")
    encoding_value = config.get("encoding", "utf-8")
    encoding = encoding_value if isinstance(encoding_value, str) else "utf-8"
    polars_content = content
    polars_encoding = encoding
    if encoding not in {"utf-8", "utf8-lossy"}:
        polars_content = content.decode(encoding, errors="replace").encode("utf-8")
        polars_encoding = "utf-8"
    header_mode_value = config.get("headerMode")
    header_mode = header_mode_value if isinstance(header_mode_value, str) else None
    has_header = config.get("hasHeader", False) is True
    skip_rows_value = config.get("skipRows", 0)
    skip_rows = skip_rows_value if isinstance(skip_rows_value, int) else 0
    if header_mode == "none":
        has_header = False
    elif header_mode == "firstRow":
        has_header = True
        skip_rows = 0
    elif header_mode == "rowIndex":
        has_header = True
        header_row_value = config.get("headerRowIndex", 0)
        skip_rows = header_row_value if isinstance(header_row_value, int) else 0
    column_names_value = config.get("columnNames")
    column_names = (
        list(column_names_value)
        if isinstance(column_names_value, Sequence)
        and not isinstance(column_names_value, (str, bytes))
        and all(isinstance(item, str) for item in column_names_value)
        else None
    )
    return pl.read_csv(
        io.BytesIO(polars_content),
        has_header=has_header,
        separator=delimiter,
        skip_rows=skip_rows,
        new_columns=column_names,
        infer_schema=bool(config.get("inferSchema", True)),
        infer_schema_length=None,
        truncate_ragged_lines=bool(config.get("truncateRaggedLines", True)),
        encoding=polars_encoding,
    )


def read_source(content: bytes, config: Mapping[str, object]) -> pl.DataFrame:
    """Dispatch to the configured reader while preventing extension-based misclassification."""
    content, _ = unwrap_zip_entry(content)
    configured = config.get("format", "auto")
    source_format = configured if isinstance(configured, str) else "auto"
    detected = detect_source_format(content)
    if source_format == "auto":
        source_format = "excel" if detected == "excel" else "delimited"
    if source_format == "excel":
        if detected != "excel":
            raise ValueError("Configured Excel reader received content without an Excel signature")
        return read_excel(content, config)
    if source_format == "fixed_width":
        return read_fixed_width(content, config)
    if detected == "excel":
        raise ValueError("Excel content cannot be parsed by a delimited text reader")
    return read_delimited(content, config)


def read_database(connection_uri: str, query: str) -> pl.DataFrame:
    """Read from PostgreSQL database using ADBC / ConnectorX Arrow readers."""
    if not connection_uri:
        raise ValueError("Database connection URI is required")
    uri = connection_uri.strip()
    if uri.startswith("postgresql://") or uri.startswith("postgres://"):
        uri = "postgresql://" + uri.split("://", 1)[1]
    elif "=" in uri:
        delim = ";" if ";" in uri else " "
        parts: dict[str, str] = {}
        for part in uri.split(delim):
            if "=" in part:
                k, v = part.split("=", 1)
                parts[k.strip().lower()] = v.strip()
        host = parts.get("host", "localhost")
        port = parts.get("port", "5432")
        db = parts.get("database") or parts.get("dbname", "")
        user = parts.get("username") or parts.get("user id") or parts.get("user", "postgres")
        pwd = parts.get("password", "")
        ssl = parts.get("sslmode", "")
        from urllib.parse import quote_plus
        ssl_suffix = f"?sslmode={ssl}" if ssl else ("?sslmode=require" if "rds.amazonaws.com" in host else "")
        uri = f"postgresql://{quote_plus(user)}:{quote_plus(pwd)}@{host}:{port}/{db}{ssl_suffix}"

    try:
        return pl.read_database_uri(query=query, uri=uri, engine="connectorx")
    except Exception as exc:
        try:
            return pl.read_database_uri(query=query, uri=uri, engine="adbc")
        except Exception:
            raise exc


