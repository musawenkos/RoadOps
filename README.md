# RoadOps

RoadOps is a .NET 9 Web API for recording and querying **road condition assessments** (TMH9-style visual condition surveys).
Survey data is organised as:

```
Workspace (a survey campaign, e.g. "N1 Pretoria – Polokwane · VCI 2026")
 └── RoadSection (a stretch of road with its km range, e.g. "N1 S03 (Hammanskraal)", km 40.0–60.0)
      └── PavedRoadRecord (one observation at a chainage: distress type, degree, extent, rut depth, GPS, photos...)
```

Deleting a workspace cascades to its sections and records.

## Architecture

Clean architecture, four projects under `src/`:

| Project | Responsibility |
|---|---|
| `RoadOps.Domain` | Entities (`Workspace`, `RoadSection`, `PavedRoadRecord`) and enums. No dependencies. |
| `RoadOps.Application` | DTOs, repository interfaces, services (validation + mapping), shared TMH9 rules (`Rules/`), condition analytics and GPS location services (`Analytics/`). |
| `RoadOps.Infrastructure` | EF Core `RoadOpsDbContext`, entity configurations, repositories, migrations (PostgreSQL via Npgsql). |
| `RoadOps.Api` | ASP.NET Core controllers and DI wiring. |
| `RoadOps.Mcp` | MCP server (Streamable HTTP) exposing agent tools, API-key auth and photo upload. See [MCP server](#mcp-server-alexa-agent-tools). |

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

Every endpoint needs an API key. Create one and store only its SHA-256 hash; these keys are separate from the MCP
server's inspector keys, because the REST API can create, rename and delete surveys:

```powershell
$hash = dotnet run --project src/RoadOps.Api --no-launch-profile -- hash-key "<a long random key>"
dotnet user-secrets --project src/RoadOps.Api set "Api:ApiKeys:0:User" "admin"
dotnet user-secrets --project src/RoadOps.Api set "Api:ApiKeys:0:KeyHash" $hash
dotnet user-secrets --project src/RoadOps.Api set "Api:ApiKeys:0:Role" "admin"
```

Each key has one role; each role includes the ones above it:

| Role | Can |
|---|---|
| `reader` | `GET` everything |
| `editor` | also `POST` and `PUT`: create, rename and correct surveys, sections and records |
| `admin` | also `DELETE`: hard deletes, which cascade (deleting a survey removes its sections and records) |

A key without a role, or with an unknown one, is a `reader`, and startup logs a warning naming it. A request above the
key's role gets `403` and is written to the audit log.

Elsewhere use environment variables (`Api__ApiKeys__0__User`, `Api__ApiKeys__0__KeyHash`, `Api__ApiKeys__0__Role`). Send the key as
`Authorization: Bearer <key>` or `X-Api-Key: <key>`; `RoadOps.Api.http` has an `@apiKey` variable for it.
Requests are rate limited per user: 300 reads and 60 writes a minute by default (`RateLimits` in `appsettings.json`).

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

## MCP server (Alexa+ agent tools)

`src/RoadOps.Mcp` is a Model Context Protocol server over **Streamable HTTP** (official C# SDK
`ModelContextProtocol.AspNetCore` 2.2.0, protocol revisions **2025-11-25** and 2026-07-28, stateless mode). It uses the
same Application services and database as the REST API. The tools are domain-level: they take corridor, year, section
names, km and GPS instead of ids, and return short summaries meant to be spoken aloud.

### Run it

1. Create an API key for each inspector and store only its SHA-256 hash (user secrets, never the repo):

   ```powershell
   $hash = dotnet run --project src/RoadOps.Mcp --no-launch-profile -- hash-key "<a long random key>"
   dotnet user-secrets --project src/RoadOps.Mcp set "Mcp:ApiKeys:0:User" "t.mokoena"
   dotnet user-secrets --project src/RoadOps.Mcp set "Mcp:ApiKeys:0:KeyHash" $hash
   dotnet user-secrets --project src/RoadOps.Mcp set "Mcp:ApiKeys:0:Role" "editor"
   ```

   In other environments use environment variables (`Mcp__ApiKeys__0__User`, `Mcp__ApiKeys__0__KeyHash`,
   `Mcp__ApiKeys__0__Role`). The user name is what gets recorded as `CreatedBy`; tools never accept it as an argument.

   Roles are the same as the REST API's. A `reader` key (e.g. a manager asking about the backlog) gets the read tools
   and photo downloads; the write tools are hidden from it and uploads return `403`. An `editor` (or `admin`) key also
   gets the write tools and uploads, which is what inspectors need. A key without a role is a `reader`, and startup
   logs a warning naming it.

2. Start PostgreSQL (`docker compose up -d`) and the server:

   ```bash
   dotnet run --project src/RoadOps.Mcp --launch-profile http
   ```

   MCP endpoint: `http://localhost:5288/mcp`. Health check: `GET /health` (no auth). Every other route requires
   `Authorization: Bearer <key>` (or `X-Api-Key: <key>`).

3. Connect a client, e.g. MCP Inspector: `npx @modelcontextprotocol/inspector`, transport **Streamable HTTP**,
   URL `http://localhost:5288/mcp`, and add the header `Authorization: Bearer <key>`.

### Tools

| Tool | Kind | Purpose |
|---|---|---|
| `find_surveys` | read | Surveys by corridor/year/name/status, with section and observation counts |
| `list_sections` | read | Sections of a survey with km ranges, counts, average degree, % poor |
| `get_condition_summary` | read | Survey, section or km range: degree distribution, top distresses, rut depth, riding quality, % poor |
| `find_worst_stretches` | read | Ranked km stretches by severity, rutting or riding quality, with dominant distress and recommended action |
| `compare_surveys` | read | Per km band between two surveys of a corridor: deteriorated / stable / rehabilitated |
| `get_location_details` | read | Observations at a km or GPS point (distress, degree, extent, measurements, photos, GPS) |
| `get_repair_backlog` | read | Recommended actions grouped and counted, urgent first, with urgent locations |
| `locate_position` | read | GPS → survey, section and km ("where am I?") |
| `list_distress_types` | read | The accepted TMH9 distress names |
| `get_session_summary` | read | Where you left off: survey, section and km (saved, or from your latest observation if newer), the previous survey's poor defects from there to the section end, your follow-ups |
| `log_observation` | write | Log from GPS (or km) + distress + degree + extent (+ rut depth, measurements, notes). Returns what is missing or a read-back; saves only with `confirm=true` |
| `attach_photo` | write | Attach an uploaded photo to an observation (default: your latest) |
| `update_observation` | write, destructive | Add or correct measurements, notes, degree or extent on your own observation |
| `void_observation` | write, destructive | Soft-void your own observation within 10 minutes of logging it |
| `save_session_summary` | write | Remember where you are (GPS, km or survey) and your follow-ups, for the next voice session. One summary per inspector (`inspector_sessions` table) |

There are no delete tools, and surveys and sections can only be created or renamed through the REST API. Each inspector
only reads and saves their own session summary; follow-up text is quoted as data when read back and logged only as its
length in the audit log.

### Photos

Photos never pass through the agent. The field app uploads the image over plain HTTP and passes the returned id to
`attach_photo`:

```bash
curl -H "Authorization: Bearer <key>" -F "file=@pothole.jpg" http://localhost:5288/uploads/photos
# 201 {"photoId":"3f2c…","contentType":"image/jpeg","sizeBytes":123456,"sha256":"…"}
```

Only JPEG, PNG and WebP are accepted, detected from the file's bytes (the name and declared type are ignored), up to
10 MB. The image structure is validated and metadata (EXIF GPS, device details, XMP, comments) is stripped before
storing; the JPEG orientation is kept. Files are stored under server-generated names in `PhotoStorage:RootPath`
(default `src/RoadOps.Mcp/data/photos`, git-ignored). `GET /photos/{photoId}` returns a photo.

### Security

Per-user rate limits on both servers (`RateLimits` in each `appsettings.json`, `429` + `Retry-After` when exceeded), an audit log of
every write on both servers: MCP write tools, photo uploads, REST POST/PUT/DELETE (log category `RoadOps.Audit`), upload validation and stripping, and stored notes
quoted as data. See [docs/SECURITY.md](docs/SECURITY.md) for the threat model, the OWASP mapping and the known limits.

### Voice agent (simulated Alexa+)

`simulator/AlexaPlusSimulator` runs the RoadOps voice agent: a simulated Alexa+ device
([mcp-voice-simulator](https://github.com/musawenkos/mcp-voice-simulator), our fork) whose Bedrock brain calls these
tools in a loop on Amazon Bedrock, with the RoadOps system prompt and the phone's GPS and photos as context. Writes
always need a confirmation turn, enforced in code. See [its README](simulator/AlexaPlusSimulator/README.md) to run it,
and [docs/friction-log.md](docs/friction-log.md) for problems met along the way.

## API

All routes require an API key (see [Run the API](#2-run-the-api)); without one they return `401`. `createdBy` is set
from the key's user name, and any `createdBy` in a request body is ignored.

Enums are serialised as integers
(`SurfaceType`: 0 Asphalt, 1 SurfaceSeal, 2 Concrete; `WorkspaceStatus`: 0 Active, 1 Suspended, 2 Archive, 3 Unknown).
Chainage is in kilometres.

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/workspaces` | List workspaces (paged) |
| `GET` | `/api/workspaces/{id}` | Get a workspace |
| `POST` | `/api/workspaces` | Create (`name`, `assessmentType`, `corridor`, `surveyYear` required) |
| `PUT` | `/api/workspaces/{id}` | Update name, assessment type, corridor, survey year, status |
| `DELETE` | `/api/workspaces/{id}` | Delete (cascades) |
| `GET` | `/api/road-sections` | List sections (paged) |
| `GET` | `/api/road-sections/{id}` | Get a section |
| `GET` | `/api/road-sections/workspace/{workspaceId}` | Sections in a workspace (paged) |
| `POST` | `/api/road-sections` | Create (`sectionName`, `workspaceId`, `chainageFrom`, `chainageTo` required; workspace must exist) |
| `PUT` | `/api/road-sections/{id}` | Rename and/or change the km range |
| `DELETE` | `/api/road-sections/{id}` | Delete (cascades) |
| `GET` | `/api/paved-road-records` | List records (paged, oldest first) |
| `GET` | `/api/paved-road-records/{id}` | Get a record |
| `GET` | `/api/paved-road-records/workspace/{workspaceId}` | Records in a workspace, by chainage (paged) |
| `GET` | `/api/paved-road-records/section/{sectionId}` | Records in a section, by chainage (paged) |
| `GET` | `/api/paved-road-records/chainage?chainageFrom=&chainageTo=&workspaceId=` | Records lying fully inside a chainage range; `workspaceId` optional (paged) |
| `POST` | `/api/paved-road-records` | Create a record (workspace and section must exist, the section must belong to the workspace, and the record must lie inside the section's km range) |
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

### Validation rules

| Entity | Rule |
|---|---|
| Workspace | `corridor` is 1–12 letters/digits, stored upper-case (`"n1"` → `"N1"`); `surveyYear` is 1990–2100. Surveys of the same corridor can be compared. |
| Road section | `chainageFrom` < `chainageTo` (km, non-negative). Sections of one workspace must not overlap (touching ends are fine). A range change must still contain the section's records. |
| Record | Inside its section's km range. `distressType` required; catalogue names are matched case-insensitively and stored in catalogue spelling. `degree`/`extent` are 1–5, or both 0 when `distressType` is `"None"`. `rutDepthMm` 0–200, valid latitude/longitude. Optional on-site measurements `lengthM` (0–1000), `widthM` (0–50), `depthMm` (0–500). `notes` up to 1000 characters, no control characters. At most 20 image paths. |
| Record | Leave `recommendedAction` empty and it is derived from distress, degree and extent by the shared TMH9 rules (`RecommendedActionRules`, also used by the seeder). A non-empty value is kept as an engineer's override. |

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
`STRESS_BASE_URL` (with `STRESS_API_KEY`), `STRESS_REPORT_PATH`. A running API applies its rate limits, so set
`RateLimits__RequestsPerMinute=0` and `RateLimits__WritesPerMinute=0` on it before load-testing it.

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
back the paged queries. `road_sections (workspace_id, chainage_from)` serves chainage-to-section lookups,
`workspaces (corridor, survey_year)` survey pairing, and `paved_road_records (latitude, longitude)` the bounding-box
prefilter of nearest-observation (GPS) lookups. Condition summaries, worst stretches, survey comparison and the repair
backlog are computed with SQL `GROUP BY` in `ConditionAnalyticsRepository`; records are never paged into memory to be counted.

### Migrations

```bash
dotnet ef migrations add <Name> --project src/RoadOps.Infrastructure --startup-project src/RoadOps.Api --output-dir Migrations
dotnet ef database update      --project src/RoadOps.Infrastructure --startup-project src/RoadOps.Api
```

## Repository layout

```
├── docker-compose.yml          # PostgreSQL for local development
├── docker/postgres/            # Postgres Dockerfile + first-run init scripts
├── docs/                       # ARCHITECTURE.md, SECURITY.md, friction-log.md
├── scripts/run-tests.ps1       # Test runner
├── simulator/AlexaPlusSimulator/ # Voice agent: simulated Alexa+ with a Bedrock brain (Node/TypeScript)
├── src/                        # Domain, Application, Infrastructure, Api, Mcp, Auth (shared keys, rate limits, audit format)
├── tests/                      # Unit, integration (API and MCP), stress
└── tools/RoadOps.DataSeeder/   # Synthetic data generator
```

## License

[MIT](LICENSE)
