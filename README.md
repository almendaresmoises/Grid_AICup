# GRID

Deterministic reconciliation. Parsers turn uploaded files into canonical datasets in Cloud Storage. Profiles match those datasets, and the results stay in Postgres. Gemini can draft a profile when an API key is set. Matching still runs with the key empty.

Development and production use the same API and worker code. Development points them at Docker emulators. Production points them at Cloud Run, Firebase Hosting, and a Postgres connection string.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Node.js 20.19 or newer
- Docker
- Python 3.12, for the parser worker

## 1. Environment

From the repository root:

```powershell
Copy-Item .env.example .env
```

The API, the migrator, and the worker read `.env` from the repository root. A variable already set in the shell is left as it is. Restart those processes after you edit the file.

`GRID_ENV=Development` in the example file is the local setup: Pub/Sub on port 8085 and Cloud Storage on port 4443, project `grid-local`, bucket `grid-local`.

## 2. Postgres

The app uses one setting, `ConnectionStrings__Default`. Swap that value to move between a local database, Supabase, and Cloud SQL. No code change.

### Local

`.env.example` expects this database:

```powershell
docker run -d --name grid-postgres -e POSTGRES_PASSWORD=password2026 -e POSTGRES_USER=postgres -e POSTGRES_DB=grid -p 5432:5432 postgres:16
```

### Supabase

Use the **direct** connection on port **5432**. The transaction pooler (port 6543) is a poor fit for Entity Framework migrations.

1. Open the Supabase project.
2. Go to **Project Settings → Database**.
3. Copy the **Direct connection** string.
4. In `.env`, replace `ConnectionStrings__Default` with:

```
ConnectionStrings__Default=Host=db.YOUR_PROJECT_REF.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_DATABASE_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
```

The username for that host is `postgres`. If the password contains a semicolon or a space, wrap the whole value in double quotes.

### Cloud SQL

Same setting. On Cloud Run, attach the instance and use the socket Cloud Run mounts:

```
ConnectionStrings__Default=Host=/cloudsql/PROJECT:REGION:INSTANCE;Database=grid;Username=...;Password=...
```

A public IP uses host, port 5432, user, password, and SSL, the same shape as Supabase. Existing rows do not move with the setting.

## 3. Pub/Sub and Cloud Storage

```powershell
docker compose up -d
```

That starts the Pub/Sub emulator on port 8085 and the Cloud Storage emulator on port 4443. The API creates the `grid-local` bucket when it starts and `STORAGE_EMULATOR_HOST` is set.

## 4. Web dependencies

```powershell
npm install
```

## 5. Schema and admin user

Run the migrator from its project directory:

```powershell
cd backend\Grid\src\Grid.DbMigrator
dotnet run
```

Wait until it reports that the host database migrations completed. The first run creates the tables and the administrator account.

## 6. API

In another terminal:

```powershell
cd backend\Grid\src\Grid.HttpApi.Host
dotnet run
```

The API listens on [http://localhost:44344](http://localhost:44344).

## 7. UI

From the repository root:

```powershell
npm run dev --workspace web
```

Open [http://localhost:5173](http://localhost:5173). The dev server proxies `/api` and `/connect` to the API.

Sign in with:

- Username: `admin`
- Password: `1q2w3E*`

## 8. Parser worker

From the repository root, once per machine:

```powershell
pip install -r workers\etl_worker\requirements.txt
python -m workers.etl_worker.message_consumer
```

The worker loads `.env`, subscribes to `grid-parser-runs`, and writes parser output to the Cloud Storage emulator.

## Using the app

- **Dashboard** charts daily full-match, partial-match and exception record counts for the last 7, 30 or 90 days using the server's recorded run times; its timezone is shown on the chart. Hover/tap or use arrow keys to inspect dates; legend buttons toggle lines. Below it, the five profiles with the lowest record-weighted match rates use the same period. Match rates include full and partial matches. Dashboard summaries refresh every minute and on returning to the page.
- **Reconciliation Runs** lists runs and opens a data grid.
- **Configuration → Configure with AI** prepares datasets and parsers from two or three CSV files, drafts a profile, and parses the uploads. At completion, choose **Review configuration** or **Run uploaded samples**. It needs `GEMINI_API_KEY` and the parser worker.
- **Configuration → Manually Configure**
  - **Parsers** is the parser studio: setup, source file, transforms, then review and publish.
  - **Datasets** defines canonical schemas and the Cloud Storage directory each parser writes to.
  - **Recon Profiles** holds the reconciliation packages and profiles.

A published parser accepts a file upload, stores it in Cloud Storage, and publishes a Pub/Sub message. The worker runs the pipeline and reports the result back to the API.

For manual setup, create canonical datasets, publish their parsers, then create and approve a reconciliation profile. **Create Run** lets you upload a file through each dataset's parser or select an already processed file. Reconciliation reads the canonical rows from the selected successful parser runs. Each run snapshots those rows and its approved profile so later uploads do not change existing results.

Parser output includes Parquet and a CSV sidecar containing the same canonical rows. Parser runs made before this sidecar was introduced must be reprocessed before they can feed a new reconciliation run. Existing reconciliation snapshots remain readable. The first successful reconciliation run stores the regression baseline; approving a profile does not require uploading a file first.

The dashboard reads compact `AppReconciliationRunSummaries` counters, written in the same transaction as each completed run. It aggregates those counters in the database rather than loading transaction snapshots into the API or browser. Apply the `AddReconciliationRunSummaries` migration before running the updated API; it creates the indexed summary table and backfills existing completed runs once. Deleting a run also deletes its summary through a foreign key cascade. Failed runs without record outcomes are excluded from record-based charts.

## Production

| Piece | Where it runs |
| --- | --- |
| API | Cloud Run |
| Parser worker | Cloud Run |
| Web app | Firebase Hosting |
| Postgres | Supabase for now, Cloud SQL later, via `ConnectionStrings__Default` |
| Pub/Sub and Cloud Storage | The real Google Cloud project |

Leave `PUBSUB_EMULATOR_HOST` and `STORAGE_EMULATOR_HOST` unset. Set `GRID_ENV=Production`, `GoogleCloud__ProjectId`, `GoogleCloud__Bucket`, and `GoogleCloud__WorkerSecret`. Set `GOOGLE_CLOUD_PROJECT`, `GCS_BUCKET`, and `GRID_WORKER_SECRET` to the same project, bucket, and secret for the worker. Set `App__SelfUrl` and `AuthServer__Authority` to the Cloud Run API URL, and `App__CorsOrigins` to the Firebase Hosting origin.

Build the images from the repository root. Both listen on port 8080.

```powershell
docker build -f deploy/api.Dockerfile -t grid-api .
docker build -f deploy/worker.Dockerfile -t grid-worker .
```

The Cloud Run service account needs Pub/Sub and Cloud Storage access. The API service also needs Cloud SQL Client if Postgres is Cloud SQL.

Build the web app with the API URL, then deploy hosting:

```powershell
$env:VITE_API_URL = "https://YOUR_API.run.app"
npm run build --workspace web
```

Replace `YOUR_FIREBASE_PROJECT_ID` in `.firebaserc`, then:

```powershell
firebase deploy --only hosting
```

## Gemini

Set `GEMINI_API_KEY` in `.env` to enable **Configure with AI** and profile suggestions. `GEMINI_MODEL` defaults to `gemini-3.8-flash`. With the key empty, those actions report that the assistant is unavailable and manual configuration still saves.
