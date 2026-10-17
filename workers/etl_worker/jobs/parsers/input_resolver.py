"""
Input file resolution for ETL Parser Jobs.

Supports priority resolution:
1. Manifest batch files from RabbitMQ message payload (context.parameters["files"])
2. Specific file paths from execution context (auto-resolving archived locations)
3. Auto-discovery from ingestion ledger receipts for registered file definitions
4. Fallback pattern-based discovery from storage (for backward compatibility / manual runs)
"""

from __future__ import annotations

import os
from pathlib import Path, PurePosixPath
from typing import Any, List, Optional
import re

from workers.common.logger import get_worker_logger
from workers.etl_worker.core.base_job import ExecutionContext
from workers.etl_worker.core.storage_provider import StorageProvider

logger = get_worker_logger("parsers.input_resolver")


def normalize_file_uri(path: str) -> str:
    """Normalizes file URI/path for local and cloud storage providers."""
    p = path.strip().replace("\\", "/")
    if p.startswith("file:///"):
        p = p[8:]
        # On Windows, file:///C:/path becomes C:/path
        if len(p) > 2 and p[1] == ":":
            p = p[0].upper() + p[1:]
    return p


def resolve_existing_or_archived_file(uri: str, storage: Optional[StorageProvider] = None) -> str:
    """
    Checks if uri exists; if not and uri was archived after ingestion,
    resolves and returns the archived URI.
    """
    if storage is None:
        storage = StorageProvider()
    normalized = normalize_file_uri(uri)
    if storage.file_exists(normalized):
        return normalized

    if normalized.startswith("s3://"):
        try:
            from workers.common.s3_storage import parse_s3_uri, s3_exists, get_boto3_client
            bucket, key = parse_s3_uri(normalized)
            filename = PurePosixPath(key).name

            # 1. Fast database lookup in storage_ingestion_receipts
            db_url = os.getenv("RECONCILIATION_DB_URL") or os.getenv("ConnectionStrings__DefaultConnection")
            if db_url:
                try:
                    import psycopg2
                    if "Host=" in db_url or "host=" in db_url:
                        conn_kwargs = {}
                        for part in db_url.split(";"):
                            if "=" in part:
                                k, v = part.split("=", 1)
                                k = k.strip().lower()
                                v = v.strip()
                                if k in ("host", "port", "database", "username", "password"):
                                    conn_kwargs[k if k != "database" else "dbname"] = v
                        conn = psycopg2.connect(**conn_kwargs)
                    else:
                        conn = psycopg2.connect(db_url)
                    with conn:
                        with conn.cursor() as cur:
                            cur.execute(
                                """
                                SELECT archived_storage_uri
                                FROM tenderrecon.storage_ingestion_receipts
                                WHERE (storage_uri = %s OR file_name = %s)
                                  AND archived_storage_uri IS NOT NULL
                                ORDER BY detected_at_utc DESC
                                LIMIT 1
                                """,
                                (normalized, filename),
                            )
                            row = cur.fetchone()
                            if row and row[0] and storage.file_exists(row[0]):
                                logger.info(f"Resolved archived file for '{normalized}' via receipt ledger -> '{row[0]}'")
                                return row[0]
                except Exception as db_err:
                    logger.debug(f"DB lookup for archived file '{normalized}' skipped: {db_err}")

            # 2. S3 scan fallback under _archive/
            try:
                s3 = get_boto3_client(bucket)
                paginator = s3.get_paginator("list_objects_v2")
                for page in paginator.paginate(Bucket=bucket, Prefix="_archive/"):
                    for obj in page.get("Contents", []):
                        k = obj.get("Key", "")
                        if k.endswith(filename) and "/duplicates/" not in k:
                            candidate = f"s3://{bucket}/{k}"
                            if storage.file_exists(candidate):
                                logger.info(f"Resolved archived file for '{normalized}' via S3 scan -> '{candidate}'")
                                return candidate
            except Exception as s3_err:
                logger.warning(f"S3 archive search failed for '{normalized}': {s3_err}")
        except Exception as ex:
            logger.warning(f"Failed to check archived location for '{normalized}': {ex}")

    return normalized


def resolve_input_files(
    pattern: Optional[str],
    context: ExecutionContext,
    storage: Optional[StorageProvider] = None,
    job_name: Optional[str] = None,
) -> List[str]:
    """
    Discovers raw files for dynamic parser execution.
    Decoupled ingestion: prefers manifests fed by the Landing Zone Watcher over static patterns,
    with automatic resolution of archived files and ledger receipts.
    """
    if storage is None:
        storage = StorageProvider()
    # 1. Manifest / batch files passed via RabbitMQ event payload
    manifest_files = context.parameters.get("files") or context.parameters.get("batch_files")
    if manifest_files:
        raw_files: List[str] = []
        for item in manifest_files:
            if isinstance(item, str) and item.strip():
                raw_files.append(normalize_file_uri(item))
            elif isinstance(item, dict):
                uri = (
                    item.get("storage_uri")
                    or item.get("storageUri")
                    or item.get("relative_storage_path")
                    or item.get("relativeStoragePath")
                    or item.get("original_file_name")
                )
                if uri:
                    raw_files.append(normalize_file_uri(str(uri)))
        if raw_files:
            resolved = [resolve_existing_or_archived_file(f, storage) for f in raw_files]
            logger.info(f"Resolved {len(resolved)} file(s) from message batch manifest.")
            return resolved

    # 2. Specific file paths in context
    specific_file_paths = context.specific_file_paths
    if specific_file_paths:
        return [
            resolve_existing_or_archived_file(normalize_file_uri(str(p)), storage)
            for p in specific_file_paths
            if p
        ]

    # 3. Single file path in context
    if context.file_path:
        raw_p = context.file_path.strip()
        if ";" in raw_p or "," in raw_p:
            delim = ";" if ";" in raw_p else ","
            return [
                resolve_existing_or_archived_file(normalize_file_uri(p), storage)
                for p in raw_p.split(delim)
                if p.strip()
            ]
        return [resolve_existing_or_archived_file(normalize_file_uri(raw_p), storage)]

    # 4. Auto-discover from ingestion ledger if job has registered file definition
    resolved_job = job_name or context.parameters.get("job_name")
    if resolved_job:
        try:
            db_url = os.getenv("RECONCILIATION_DB_URL")
            if db_url:
                import psycopg2
                with psycopg2.connect(db_url) as conn:
                    with conn.cursor() as cur:
                        cur.execute(
                            """
                            SELECT r.archived_storage_uri, r.storage_uri, r.file_name
                            FROM tenderrecon.storage_ingestion_receipts r
                            JOIN tenderrecon.storage_file_definitions d ON d.id = r.file_definition_id
                            WHERE d.associated_parser_name = %s
                            ORDER BY r.detected_at_utc DESC
                            LIMIT 20
                            """,
                            (resolved_job,),
                        )
                        rows = cur.fetchall()
                        if rows:
                            candidates: List[str] = []
                            for arch_uri, stor_uri, fname in rows:
                                if stor_uri and storage.file_exists(stor_uri):
                                    candidates.append(stor_uri)
                                elif arch_uri and storage.file_exists(arch_uri):
                                    candidates.append(arch_uri)
                            if candidates:
                                logger.info(
                                    f"Auto-discovered {len(candidates)} file(s) from ingestion receipts for '{resolved_job}'"
                                )
                                return candidates
        except Exception as db_ex:
            logger.debug(f"Auto-discovery from ingestion ledger failed: {db_ex}")

    # 5. Fallback pattern discovery (for backfills / ad-hoc runs)
    if not pattern or not pattern.strip():
        logger.info("No input pattern defined and no landed batch manifest provided.")
        return []

    year_str = f"{context.execution_date.year}"
    month_str = f"{context.execution_date.month:02d}"
    day_str = f"{context.execution_date.day:02d}"
    comp_str = context.company_code or "*"
    branch_str = context.branch_code or "*"

    resolved_pattern = (
        pattern.replace("{year}", year_str)
        .replace("{month}", month_str)
        .replace("{day}", day_str)
        .replace("{company}", comp_str)
        .replace("{branch}", branch_str)
        .replace("{YYYY}", year_str)
        .replace("{MM}", month_str)
        .replace("{DD}", day_str)
        .replace("{comp}", comp_str)
    ).replace("\\", "/").lstrip("/")

    parts = resolved_pattern.split("*", 1)
    base_dir_rel = parts[0].rstrip("/")
    glob_suffix = "*" + parts[1] if len(parts) > 1 else ""

    files_found: List[str] = []
    if context.is_local:
        local_base = storage.get_local_path(base_dir_rel)
        if local_base.is_file():
            return [str(local_base)]
        if local_base.exists():
            if glob_suffix:
                files_found.extend([str(p) for p in local_base.glob(glob_suffix)])
                files_found.extend([str(p) for p in local_base.glob(glob_suffix.upper())])
            else:
                files_found.extend([str(p) for p in local_base.glob("**/*") if p.is_file()])
    else:
        files_found.extend(storage.list_files(base_dir_rel))

    return sorted(list(set(files_found)))
