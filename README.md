# GRID

Deterministic reconciliation. You upload CSV datasets, edit a versioned profile, and run it. Matching happens in the API and the results are stored in Postgres. Gemini can draft a profile when an API key is set. The same screens work without it.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Node.js 20.19 or newer
- Either Docker, for a local database, or a [Supabase](https://supabase.com) project

## 1. Choose the database

Copy the example environment file from the repository root:

```powershell
Copy-Item .env.example .env
```

The API and the migrator read `.env` from the repository root when they start.

### Local Postgres

Start a database that matches the connection string already in `.env`:

```powershell
docker run -d --name grid-postgres -e POSTGRES_PASSWORD=grid -e POSTGRES_USER=grid -e POSTGRES_DB=Grid -p 5433:5432 postgres:16
```

### Supabase

Use the **direct** connection on port **5432**. The transaction pooler (port 6543) is a poor fit for Entity Framework migrations.

1. Open the Supabase project.
2. Go to **Project Settings → Database**.
3. Copy the **Direct connection** string. It looks like `postgresql://postgres:[PASSWORD]@db.[PROJECT_REF].supabase.co:5432/postgres`.
4. In `.env`, comment out the local `ConnectionStrings__Default` line.
5. Uncomment the Supabase line and fill in the project ref and database password:

```
ConnectionStrings__Default=Host=db.YOUR_PROJECT_REF.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_DATABASE_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
```

The username for that direct host is `postgres`. If the password contains a semicolon or a space, wrap the whole value in double quotes.

## 2. Install the web dependencies

From the repository root:

```powershell
npm install
```

## 3. Create the schema and the admin user

Run the migrator from its project directory so it finds its settings:

```powershell
cd backend\Grid\src\Grid.DbMigrator
dotnet run
```

Wait until it reports that the host database migrations completed. The first run creates the tables and the administrator account.

## 4. Start the API

In a second terminal:

```powershell
cd backend\Grid\src\Grid.HttpApi.Host
dotnet run
```

The API listens on [http://localhost:44344](http://localhost:44344).

## 5. Start the UI

In a third terminal, from the repository root:

```powershell
npm run dev --workspace web
```

Open [http://localhost:5173](http://localhost:5173). The dev server proxies `/api` and `/connect` to the API, so the browser stays on the Vite origin.

Sign in with:

- Username: `admin`
- Password: `1q2w3E*`

Then upload CSVs on **Ingest**, review them on **Datasets**, create a package, edit and approve its profile, and run it. The results page lists match groups and unresolved rows.

## Optional: Gemini

Set `GEMINI_API_KEY` in `.env` to enable **Draft with AI** on a profile and **Suggest a profile change** after a run. `GEMINI_MODEL` defaults to `gemini-3.8-flash`. Restart the API after changing `.env`. With the key empty, those actions report that the assistant is unavailable and the profile form still saves by hand.

A proposal stays inactive until it passes validation, still matches the groups from the original batch, and you approve it. Approving a profile does not start a run. You start the run yourself.
