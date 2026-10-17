"""Excel inspection and schema discovery utility for parser studio and worker jobs."""

from __future__ import annotations

import argparse
import io
import json
import sys
from typing import Any

import fastexcel
import msoffcrypto
import polars as pl


def detect_header_row(df: pl.DataFrame) -> int:
    """Detect the most likely header row index by scoring row completeness and text-likeness."""
    if df.height == 0:
        return 0
    best_row = 0
    max_score = -1.0
    for r in range(min(df.height, 15)):
        row_vals = df.row(r)
        non_null_vals = [v for v in row_vals if v is not None and str(v).strip() != ""]
        if not non_null_vals:
            continue
        text_count = 0
        for v in non_null_vals:
            sv = str(v).strip()
            # If purely number or date or currency, it's likely a data row, not a header
            if sv.replace(".", "", 1).replace("-", "", 1).isdigit():
                continue
            if len(sv) <= 64:
                text_count += 1

        completeness = len(non_null_vals) / max(1, df.width)
        text_ratio = text_count / max(1, len(non_null_vals))
        # Title banners typically occupy 1 cell in a multi-column sheet
        banner_penalty = 0.2 if (df.width > 2 and len(non_null_vals) == 1) else 1.0
        score = (completeness * 0.6 + text_ratio * 0.4) * banner_penalty
        if score > max_score and completeness >= 0.25:
            max_score = score
            best_row = r

    return best_row


def inspect_excel(
    file_bytes: bytes,
    sheet_name: str | None = None,
    password: str | None = None,
    header_row_index: int | None = None,
    has_header: bool = True,
    skip_rows: int = 0,
    max_rows: int = 50,
) -> dict[str, Any]:
    """Inspect Excel file structure, sheets, schema, and sample rows with password support."""
    is_encrypted = False
    decrypted_bytes = file_bytes

    # 1. Check for password encryption via msoffcrypto
    try:
        office_file = msoffcrypto.OfficeFile(io.BytesIO(file_bytes))
        if office_file.is_encrypted():
            is_encrypted = True
            if not password or not password.strip():
                return {
                    "success": False,
                    "isPasswordProtected": True,
                    "passwordRequired": True,
                    "error": "Workbook is password-protected. Please provide a password to inspect its contents.",
                    "sheetNames": [],
                    "activeSheet": None,
                    "columns": [],
                    "sampleRowsRaw": [],
                    "sampleRowsFiltered": [],
                }
            try:
                office_file.load_key(password=password.strip())
                decrypted = io.BytesIO()
                office_file.decrypt(decrypted)
                decrypted_bytes = decrypted.getvalue()
            except Exception as dec_err:
                return {
                    "success": False,
                    "isPasswordProtected": True,
                    "passwordRequired": True,
                    "error": f"Failed to decrypt workbook with provided password: {dec_err}",
                    "sheetNames": [],
                    "activeSheet": None,
                    "columns": [],
                    "sampleRowsRaw": [],
                    "sampleRowsFiltered": [],
                }
    except Exception:
        # Not an OLE-encrypted file (standard unencrypted xlsx or non-office archive)
        pass

    # 2. Inspect sheets and data using fastexcel / polars
    try:
        excel_reader = fastexcel.read_excel(decrypted_bytes)
        sheet_names = excel_reader.sheet_names
        if not sheet_names:
            return {
                "success": False,
                "isPasswordProtected": is_encrypted,
                "passwordRequired": False,
                "error": "Workbook contains no worksheets.",
                "sheetNames": [],
                "activeSheet": None,
                "columns": [],
                "sampleRowsRaw": [],
                "sampleRowsFiltered": [],
            }

        target_sheet = sheet_name if sheet_name and sheet_name in sheet_names else sheet_names[0]
        sheet_raw = excel_reader.load_sheet(target_sheet, header_row=None, n_rows=max_rows + 50)
        df_raw = sheet_raw.to_polars()

        if df_raw.height == 0:
            return {
                "success": True,
                "isPasswordProtected": is_encrypted,
                "passwordRequired": False,
                "error": None,
                "sheetNames": sheet_names,
                "activeSheet": target_sheet,
                "headerRowIndex": -1 if not has_header else 0,
                "columns": [],
                "sampleRowsRaw": [],
                "sampleRowsFiltered": [],
            }

        if not has_header:
            effective_header_row = -1
            detected_col_names = [f"col_{idx + 1}" for idx in range(df_raw.width)]
            start_row = max(0, skip_rows)
        else:
            if header_row_index is not None and header_row_index >= 0:
                effective_header_row = min(header_row_index, max(0, df_raw.height - 1))
            else:
                effective_header_row = detect_header_row(df_raw)

            raw_headers = df_raw.row(effective_header_row)
            detected_col_names = []
            seen_names: set[str] = set()
            for idx, val in enumerate(raw_headers):
                name = str(val).strip() if val is not None and str(val).strip() else f"col_{idx + 1}"
                if name.startswith("__UNNAMED__"):
                    name = f"col_{idx + 1}"
                unique_name = name
                cnt = 2
                while unique_name in seen_names:
                    unique_name = f"{name}_{cnt}"
                    cnt += 1
                seen_names.add(unique_name)
                detected_col_names.append(unique_name)

            start_row = max(effective_header_row + 1, skip_rows)
        df_data = df_raw.slice(start_row, max_rows)
        if has_header:
            # Header text coerces native Excel dates and numbers to strings in
            # the raw sheet. Read data with the chosen header before profiling.
            data_offset = max(0, start_row - effective_header_row - 1)
            df_data = excel_reader.load_sheet(
                target_sheet,
                header_row=effective_header_row,
                n_rows=max_rows + data_offset,
            ).to_polars().slice(data_offset, max_rows)
        if df_data.height > 0 and len(detected_col_names) == df_data.width:
            df_data.columns = detected_col_names

        columns_profile: list[dict[str, Any]] = []
        for idx, col_name in enumerate(detected_col_names):
            if df_data.height > 0 and col_name in df_data.columns:
                series = df_data[col_name]
                non_null_count = series.len() - series.null_count()
                null_count = series.null_count()
                inferred_type = _infer_type(series)
                sample_vals = [
                    str(v)
                    for v in series.drop_nulls().head(5).to_list()
                    if v is not None and str(v).strip()
                ]
            else:
                non_null_count = 0
                null_count = 0
                inferred_type = "String"
                sample_vals = []

            columns_profile.append({
                "index": idx,
                "detectedName": col_name,
                "inferredType": inferred_type,
                "nonNullCount": non_null_count,
                "nullCount": null_count,
                "sampleValues": sample_vals,
            })

        sample_rows_raw: list[dict[str, str | None]] = []
        for r_idx in range(min(df_raw.height, 40)):
            raw_tuple = df_raw.row(r_idx)
            raw_row = {
                "__line_num": str(r_idx + 1),
                "__source_row_index": str(r_idx),
            }
            for c_idx, col_name in enumerate(detected_col_names):
                val = raw_tuple[c_idx] if c_idx < len(raw_tuple) else None
                raw_row[col_name] = None if val is None else str(val)
            sample_rows_raw.append(raw_row)

        sample_rows_filtered: list[dict[str, str | None]] = []
        for f_idx in range(min(df_data.height, 30)):
            row_dict = df_data.row(f_idx, named=True)
            filtered_row = {
                "__row_num": str(f_idx + 1),
                "__source_row_index": str(start_row + f_idx),
            }
            for col_name in detected_col_names:
                val = row_dict.get(col_name)
                filtered_row[col_name] = None if val is None else str(val)
            sample_rows_filtered.append(filtered_row)

        return {
            "success": True,
            "isPasswordProtected": is_encrypted,
            "passwordRequired": False,
            "error": None,
            "sheetNames": sheet_names,
            "activeSheet": target_sheet,
            "headerRowIndex": effective_header_row,
            "columns": columns_profile,
            "sampleRowsRaw": sample_rows_raw,
            "sampleRowsFiltered": sample_rows_filtered,
        }
    except Exception as exc:
        return {
            "success": False,
            "isPasswordProtected": is_encrypted,
            "passwordRequired": False,
            "error": f"Failed to parse Excel workbook: {exc}",
            "sheetNames": [],
            "activeSheet": None,
            "headerRowIndex": 0,
            "columns": [],
            "sampleRowsRaw": [],
            "sampleRowsFiltered": [],
        }


def _infer_type(series: pl.Series) -> str:
    dtype = series.dtype
    if dtype in (pl.Int8, pl.Int16, pl.Int32, pl.Int64, pl.UInt8, pl.UInt16, pl.UInt32, pl.UInt64):
        return "Integer"
    if dtype in (pl.Float32, pl.Float64, pl.Decimal):
        return "Decimal"
    if dtype in (pl.Date, pl.Datetime):
        return "DateTime"
    if dtype == pl.Boolean:
        return "Boolean"
    return "String"


def main() -> None:
    parser = argparse.ArgumentParser(description="Inspect Excel files for ISA Parser Studio")
    parser.add_argument("--input", required=True, help="Path to input Excel file")
    parser.add_argument("--sheet", default=None, help="Target worksheet name")
    parser.add_argument("--password", default=None, help="Decryption password if encrypted")
    parser.add_argument("--header-row", type=int, default=-1, help="0-based header row index (-1 for auto-detect)")
    parser.add_argument("--no-header", action="store_true", help="Treat file as raw data without headers")
    parser.add_argument("--skip-rows", type=int, default=0, help="Rows to skip before headers")
    parser.add_argument("--max-rows", type=int, default=50, help="Maximum preview rows to return")

    args = parser.parse_args()
    try:
        with open(args.input, "rb") as f:
            content = f.read()
        header_row = args.header_row if args.header_row >= 0 else None
        res = inspect_excel(
            content,
            sheet_name=args.sheet,
            password=args.password,
            header_row_index=header_row,
            has_header=not args.no_header,
            skip_rows=args.skip_rows,
            max_rows=args.max_rows,
        )
        print(json.dumps(res))
    except Exception as e:
        print(json.dumps({"success": False, "error": str(e), "columns": [], "sheetNames": [], "headerRowIndex": 0}))
        sys.exit(1)


if __name__ == "__main__":
    main()
