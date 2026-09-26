# RoadOps

RoadOps is a .NET 9 Web API for recording and querying **road condition assessments** (TMH9-style visual condition surveys).
Survey data is organised as:

```
Workspace (a survey campaign, e.g. "N1 Pretoria – Polokwane · VCI 2026")
 └── RoadSection (a stretch of road, e.g. "N1 S03: km 40.0–60.0")
      └── PavedRoadRecord (one observation at a chainage: distress type, degree, extent, rut depth, GPS, photos...)
```

Deleting a workspace cascades to its sections and records.

## Architecture

Clean architecture, four projects under `src/`:

| Project | Responsibility |
|---|---|
| `RoadOps.Domain` | Entities (`Workspace`, `RoadSection`, `PavedRoadRecord`) and enums. No dependencies. |
| `RoadOps.Application` | DTOs, repository interfaces, services (validation + mapping). |
| `RoadOps.Infrastructure` | EF Core `RoadOpsDbContext`, entity configurations, repositories, migrations (PostgreSQL via Npgsql). |
| `RoadOps.Api` | ASP.NET Core controllers and DI wiring. |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the dependency graph, CRUD map and a full request trace.

## Prerequisites

- [.NET SDK 9.0](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL and the integration/stress tests)
- Optional: `dotnet tool install -g dotnet-ef` to manage migrations

## Getting started

### 1. Start PostgreSQL

```bash
docker compose up -d
```

This builds `docker/postgres/Dockerfile` (PostgreSQL 16) and exposes it on `localhost:5432`:

| Setting | Default | Override with env var |
|---|---|---|
| Database | `roadops` | `POSTGRES_DB` |
| User | `roadops_user` | `POSTGRES_USER` |
| Password | `roadops_dev_password` | `POSTGRES_PASSWORD` |
| Host port | `5432` | `POSTGRES_PORT` |

Data persists in the `roadops-pgdata` volume. `docker compose down -v` deletes it.

The API connection string in `src/RoadOps.Api/appsettings.json` matches these defaults:

```
Host=localhost;Port=5432;Database=roadops;Username=roadops_user;Password=roadops_dev_password
```

> These are **local development credentials only**. In other environments set
> `ConnectionStrings__DefaultConnection` as an environment variable or use user secrets.

### 2. Run the API

```bash
dotnet run --project src/RoadOps.Api --launch-profile http
```

The API listens on `http://localhost:5277`. In Development, pending EF Core migrations are applied automatically on startup
(controlled by `Database:ApplyMigrationsOnStartup`). The OpenAPI document is at `/openapi/v1.json`.

### 3. Load synthetic data (optional)

```bash
dotnet run --project tools/RoadOps.DataSeeder
```

See [Synthetic data](#synthetic-data) below.

## API

Enums are serialised as integers
(`SurfaceType`: 0 Asphalt, 1 SurfaceSeal, 2 Concrete; `WorkspaceStatus`: 0 Active, 1 Suspended, 2 Archive, 3 Unknown).
Chainage is in kilometres.

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/workspaces` | List workspaces (paged) |
| `GET` | `/api/workspaces/{id}` | Get a workspace |
| `POST` | `/api/workspaces` | Create (`name`, `assessmentType`, `createdBy` required) |
| `PUT` | `/api/workspaces/{id}` | Update name, assessment type, status |
| `DELETE` | `/api/workspaces/{id}` | Delete (cascades) |
| `GET` | `/api/road-sections` | List sections (paged) |
| `GET` | `/api/road-sections/{id}` | Get a section |
| `GET` | `/api/road-sections/workspace/{workspaceId}` | Sections in a workspace (paged) |
| `POST` | `/api/road-sections` | Create (`sectionName`, `workspaceId`, `createdBy` required; workspace must exist) |
| `PUT` | `/api/road-sections/{id}` | Rename |
| `DELETE` | `/api/road-sections/{id}` | Delete (cascades) |
| `GET` | `/api/paved-road-records` | List records (paged, oldest first) |
| `GET` | `/api/paved-road-records/{id}` | Get a record |
| `GET` | `/api/paved-road-records/workspace/{workspaceId}` | Records in a workspace, by chainage (paged) |
| `GET` | `/api/paved-road-records/section/{sectionId}` | Records in a section, by chainage (paged) |
| `GET` | `/api/paved-road-records/chainage?chainageFrom=&chainageTo=&workspaceId=` | Records lying fully inside a chainage range; `workspaceId` optional (paged) |
| `POST` | `/api/paved-road-records` | Create a record (workspace and section must exist, and the section must belong to the workspace) |
| `PUT` | `/api/paved-road-records/{id}` | Update a record |
| `DELETE` | `/api/paved-road-records/{id}` | Delete a record |

### Paging

Every list endpoint takes `page` (1-based, default `1`) and `pageSize` (default `50`, max `500`) and returns:

```json
{
  "items": [ ... ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 18261,
  "totalPages": 366,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

Ordering is stable (ties are broken by id), so walking pages never skips or repeats a record. A page past the end returns
empty `items` with the real `totalCount`. Chainage lists are ordered by `chainageFrom`, other lists by creation time.

### Errors

- `400` for validation errors, invalid paging values, or a reference to a workspace/section that doesn't exist.
  Controller validation returns a plain-text message.
- `404` for unknown ids on get/update.
- If a database foreign-key violation still gets through (e.g. the workspace is deleted mid-request), a global handler
  returns `400` as `application/problem+json` rather than `500`. Unique violations return `409`.

Example requests are in [src/RoadOps.Api/RoadOps.Api.http](src/RoadOps.Api/RoadOps.Api.http).

## Testing

```
tests/
├── Shared/                     # API host fixture (WebApplicationFactory + PostgreSQL Testcontainer) and test data builders
├── RoadOps.UnitTests/          # Services and controllers with mocked repositories (xUnit + Moq). No database.
├── RoadOps.IntegrationTests/   # Every endpoint over HTTP against a real PostgreSQL database
└── RoadOps.StressTests/        # Concurrent load: latency percentiles, throughput, error rate, data integrity
```

### Run everything

```powershell
./scripts/run-tests.ps1
```

The script checks prerequisites, builds once, runs each suite, prints a pass/fail summary and the stress report, and
writes TRX results and `stress-report.md` to `TestResults/<timestamp>/`. It exits non-zero if any suite fails.

```powershell
./scripts/run-tests.ps1 -Suite unit          # unit | integration | stress | all
./scripts/run-tests.ps1 -Suite stress -StressDuration 60 -StressConcurrency 50 -StressMaxP95Ms 300
./scripts/run-tests.ps1 -Suite stress -StressBaseUrl http://localhost:5277   # load-test a running API (creates test data in it)
```

Or run a suite directly: `dotnet test tests/RoadOps.UnitTests`.

### How the integration and stress tests get a database

By default each run starts a throwaway `postgres:16-alpine` container via Testcontainers, applies migrations,
and removes the container afterwards. Docker must be running. To use an existing database instead:

```powershell
$env:ROADOPS_TEST_CONNECTION = "Host=localhost;Port=5432;Database=roadops_test;Username=roadops_user;Password=roadops_dev_password"
```

The tests create their own uniquely named data, but don't point them at a database whose contents you care about.

### Stress tests

| Test | What it checks |
|---|---|
| Mixed read/write workload | N concurrent workers for D seconds: 40% get-by-id, 20% list-by-section, 15% chainage range, 15% create, 10% update. Fails if error rate or p95 latency exceeds budget. |
| Concurrent write burst | Fires `STRESS_BURST_SIZE` simultaneous creates, then verifies every record was persisted exactly once. |

Tunable with environment variables (the script sets them from its parameters): `STRESS_DURATION_SECONDS` (20),
`STRESS_CONCURRENCY` (25), `STRESS_MAX_P95_MS` (500), `STRESS_MAX_ERROR_RATE` (0), `STRESS_BURST_SIZE` (500),
`STRESS_BASE_URL`, `STRESS_REPORT_PATH`.

## Synthetic data

`tools/RoadOps.DataSeeder` fills the database with realistic survey data for development, demos and analytics.

```bash
dotnet run --project tools/RoadOps.DataSeeder                     # seed an empty database
dotnet run --project tools/RoadOps.DataSeeder -- --reset          # wipe ALL existing data first
dotnet run --project tools/RoadOps.DataSeeder -- --seed 7 --segment-km 0.2 --connection "<connection string>"
```

What it generates (seed 42, 100 m segments: 12 workspaces, about 90 sections, about 18,000 records):

- **6 real South African corridors:** N1 Pretoria–Polokwane, N4 Pretoria–Emalahleni, N3 Heidelberg–Harrismith,
  R21 Pretoria–OR Tambo, N2 Cape Town–Somerset West, R61 Mthatha–Port St Johns. Coordinates follow approximate route
  waypoints.
- **Two survey campaigns per corridor:** 2024 (Archived) and 2026 (Active). 2026 shows deterioration, except for one
  stretch per route that was rehabilitated in between. The 2026 R61 survey is *Suspended* at 60% coverage.
- **Consistent condition:** each corridor has a hidden condition profile along its chainage (a random walk plus local
  hotspots). Distress type, degree and extent (1–5), rut depth, riding quality, skid resistance and recommended action
  are all derived from it, so poor stretches have potholes, deep ruts and poor ride quality.
- Surface types in runs of 2–15 km (asphalt, surface seal, some concrete on the N3) with the matching distress
  vocabulary, photo paths for degree ≥ 3 observations, inspectors, weekday survey timestamps, and occasional QA edits.

The seeder refuses to run on a non-empty database unless you pass `--reset`.

## Database

Tables and columns use PostgreSQL snake_case (`paved_road_records.chainage_from`) via
[EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions), configured in `RoadOpsDbContext`.
Composite indexes on `(workspace_id, chainage_from)`, `(section_id, chainage_from)` and `(workspace_id, created_at)`
back the paged queries.

### Migrations

```bash
dotnet ef migrations add <Name> --project src/RoadOps.Infrastructure --startup-project src/RoadOps.Api --output-dir Migrations
dotnet ef database update      --project src/RoadOps.Infrastructure --startup-project src/RoadOps.Api
```

## Repository layout

```
├── docker-compose.yml          # PostgreSQL for local development
├── docker/postgres/            # Postgres Dockerfile + first-run init scripts
├── docs/ARCHITECTURE.md
├── scripts/run-tests.ps1       # Test runner
├── src/                        # Domain, Application, Infrastructure, Api
├── tests/                      # Unit, integration, stress
└── tools/RoadOps.DataSeeder/   # Synthetic data generator
```
