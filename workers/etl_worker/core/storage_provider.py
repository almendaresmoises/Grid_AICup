from __future__ import annotations

import os
from pathlib import Path


class StorageProvider:
    """Reads and writes local files and Google Cloud Storage objects."""

    def __init__(self) -> None:
        self.storage_root = Path(os.getenv("GRID_STORAGE_ROOT", "storage"))

    def get_uri(self, relative_path: str, layer: str | None = None) -> str:
        clean = relative_path.replace("\\", "/").lstrip("/")
        if clean.startswith("gs://") or os.path.isabs(relative_path):
            return relative_path
        bucket = os.getenv("GCS_BUCKET", "")
        if bucket:
            prefix = f"{layer}/" if layer and not clean.startswith(f"{layer}/") else ""
            return f"gs://{bucket}/{prefix}{clean}"
        return str((self.storage_root / clean).resolve())

    def get_local_path(self, relative_path: str) -> Path:
        if relative_path.startswith("gs://"):
            raise ValueError(f"'{relative_path}' is a Cloud Storage object, not a local path")
        path = Path(relative_path)
        if path.is_absolute():
            return path
        return self.storage_root / relative_path.replace("\\", "/").lstrip("/")

    def read_file(self, uri: str) -> bytes:
        if uri.startswith("gs://"):
            bucket_name, blob_name = _split_gs_uri(uri)
            return _storage_client().bucket(bucket_name).blob(blob_name).download_as_bytes()
        return Path(uri).read_bytes()

    def write_file(self, uri: str, data: bytes) -> None:
        if uri.startswith("gs://"):
            bucket_name, blob_name = _split_gs_uri(uri)
            _storage_client().bucket(bucket_name).blob(blob_name).upload_from_string(data)
            return
        path = Path(uri)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def file_exists(self, uri: str) -> bool:
        if uri.startswith("gs://"):
            bucket_name, blob_name = _split_gs_uri(uri)
            return _storage_client().bucket(bucket_name).blob(blob_name).exists()
        return Path(uri).is_file()

    def list_files(self, prefix: str) -> list[str]:
        if prefix.startswith("gs://"):
            bucket_name, blob_prefix = _split_gs_uri(prefix)
            return [
                f"gs://{bucket_name}/{blob.name}"
                for blob in _storage_client().list_blobs(bucket_name, prefix=blob_prefix)
            ]
        path = Path(prefix)
        if path.is_dir():
            return [str(item) for item in path.glob("**/*") if item.is_file()]
        return []


def _storage_client():
    from google.cloud import storage

    host = os.getenv("STORAGE_EMULATOR_HOST", "")
    project = os.getenv("GOOGLE_CLOUD_PROJECT") or os.getenv("GoogleCloud__ProjectId") or None
    if host:
        from google.auth.credentials import AnonymousCredentials

        return storage.Client(project=project, credentials=AnonymousCredentials(), client_options={"api_endpoint": host})
    return storage.Client(project=project)


def _split_gs_uri(uri: str) -> tuple[str, str]:
    without_scheme = uri[5:]
    bucket, _, blob = without_scheme.partition("/")
    if not bucket or not blob:
        raise ValueError(f"Invalid Cloud Storage URI '{uri}'")
    return bucket, blob
