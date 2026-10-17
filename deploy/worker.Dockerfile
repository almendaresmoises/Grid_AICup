# Cloud Run image for the parser worker. Build from the repository root:
# docker build -f deploy/worker.Dockerfile -t grid-worker .
FROM python:3.12-slim
WORKDIR /app
COPY workers/etl_worker/requirements.txt workers/etl_worker/requirements.txt
RUN pip install --no-cache-dir -r workers/etl_worker/requirements.txt
COPY workers workers
ENV PYTHONUNBUFFERED=1
ENV PYTHONPATH=/app
ENV PORT=8080
EXPOSE 8080
CMD ["python", "-m", "workers.etl_worker.message_consumer"]
