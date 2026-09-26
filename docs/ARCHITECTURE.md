# RoadOps Architecture Documentation

## 1. Dependency Graph

### Project Dependencies

```
RoadOps.Api
    ↓ depends on
RoadOps.Application
    ↓ depends on
RoadOps.Domain

RoadOps.Infrastructure
    ↓ depends on
RoadOps.Application
    ↓ depends on
RoadOps.Domain
```

### Layer Responsibilities

**RoadOps.Domain**
- Contains domain entities (Workspace, RoadSection, PavedRoadRecord)
- Contains domain enums (SurfaceType, WorkspaceStatus)
- No external dependencies
- Pure business logic and data structures

**RoadOps.Application**
- Contains repository interfaces (IWorkspaceRepository, IRoadSectionRepository, IPavedRoadRecordRepository)
- Contains DTOs (data transfer objects for API)
- Contains application services (WorkspaceService, RoadSectionService, PavedRoadRecordService)
- Business logic and validation
- Depends on Domain for entities
- Does NOT depend on Infrastructure, EF Core, or PostgreSQL

**RoadOps.Infrastructure**
- Contains EF Core DbContext (RoadOpsDbContext)
- Contains EF Core entity configurations
- Contains repository implementations (WorkspaceRepository, RoadSectionRepository, PavedRoadRecordRepository)
- Database-specific code
- Depends on Application for repository interfaces
- Depends on Domain for entities

**RoadOps.Api**
- Contains API controllers (WorkspacesController, RoadSectionsController, PavedRoadRecordsController)
- HTTP request/response handling
- Dependency injection registration in Program.cs
- Depends on Application for services and DTOs
- Depends on Infrastructure for DbContext

## 2. CRUD Map

### Workspace CRUD Operations

| Operation | Controller | Service | Repository Interface | Repository Implementation | DbContext |
|-----------|------------|---------|----------------------|---------------------------|------------|
| Create | WorkspacesController.Create | WorkspaceService.CreateAsync | IWorkspaceRepository.AddAsync | WorkspaceRepository.AddAsync | Workspaces.AddAsync |
| Get by ID | WorkspacesController.GetById | WorkspaceService.GetByIdAsync | IWorkspaceRepository.GetByIdAsync | WorkspaceRepository.GetByIdAsync | Workspaces.FindAsync |
| Get All (paged) | WorkspacesController.GetAll | WorkspaceService.GetPageAsync | IWorkspaceRepository.GetPageAsync | WorkspaceRepository.GetPageAsync | Workspaces.OrderBy(...).Skip/Take + CountAsync |
| Update | WorkspacesController.Update | WorkspaceService.UpdateAsync | IWorkspaceRepository.UpdateAsync | WorkspaceRepository.UpdateAsync | Workspaces.Update |
| Delete | WorkspacesController.Delete | WorkspaceService.DeleteAsync | IWorkspaceRepository.DeleteAsync | WorkspaceRepository.DeleteAsync | Workspaces.Remove |

### RoadSection CRUD Operations

| Operation | Controller | Service | Repository Interface | Repository Implementation | DbContext |
|-----------|------------|---------|----------------------|---------------------------|------------|
| Create | RoadSectionsController.Create | RoadSectionService.CreateAsync | IRoadSectionRepository.AddAsync | RoadSectionRepository.AddAsync | RoadSections.AddAsync |
| Get by ID | RoadSectionsController.GetById | RoadSectionService.GetByIdAsync | IRoadSectionRepository.GetByIdAsync | RoadSectionRepository.GetByIdAsync | RoadSections.FindAsync |
| Get All (paged) | RoadSectionsController.GetAll | RoadSectionService.GetPageAsync | IRoadSectionRepository.GetPageAsync | RoadSectionRepository.GetPageAsync | RoadSections.OrderBy(...).Skip/Take + CountAsync |
| Get by Workspace (paged) | RoadSectionsController.GetByWorkspaceId | RoadSectionService.GetByWorkspaceIdAsync | IRoadSectionRepository.GetByWorkspaceIdAsync | RoadSectionRepository.GetByWorkspaceIdAsync | RoadSections.Where(...).Skip/Take + CountAsync |
| Update | RoadSectionsController.Update | RoadSectionService.UpdateAsync | IRoadSectionRepository.UpdateAsync | RoadSectionRepository.UpdateAsync | RoadSections.Update |
| Delete | RoadSectionsController.Delete | RoadSectionService.DeleteAsync | IRoadSectionRepository.DeleteAsync | RoadSectionRepository.DeleteAsync | RoadSections.Remove |

### PavedRoadRecord CRUD Operations

| Operation | Controller | Service | Repository Interface | Repository Implementation | DbContext |
|-----------|------------|---------|----------------------|---------------------------|------------|
| Create | PavedRoadRecordsController.Create | PavedRoadRecordService.CreateAsync | IPavedRoadRecordRepository.AddAsync | PavedRoadRecordRepository.AddAsync | PavedRoadRecords.AddAsync |
| Get by ID | PavedRoadRecordsController.GetById | PavedRoadRecordService.GetByIdAsync | IPavedRoadRecordRepository.GetByIdAsync | PavedRoadRecordRepository.GetByIdAsync | PavedRoadRecords.FindAsync |
| Get All (paged) | PavedRoadRecordsController.GetAll | PavedRoadRecordService.GetPageAsync | IPavedRoadRecordRepository.GetPageAsync | PavedRoadRecordRepository.GetPageAsync | PavedRoadRecords.OrderBy(...).Skip/Take + CountAsync |
| Get by Workspace (paged) | PavedRoadRecordsController.GetByWorkspaceId | PavedRoadRecordService.GetByWorkspaceIdAsync | IPavedRoadRecordRepository.GetByWorkspaceIdAsync | PavedRoadRecordRepository.GetByWorkspaceIdAsync | PavedRoadRecords.Where(...).Skip/Take + CountAsync |
| Get by Section (paged) | PavedRoadRecordsController.GetBySectionId | PavedRoadRecordService.GetBySectionIdAsync | IPavedRoadRecordRepository.GetBySectionIdAsync | PavedRoadRecordRepository.GetBySectionIdAsync | PavedRoadRecords.Where(...).Skip/Take + CountAsync |
| Find by Chainage Range (paged) | PavedRoadRecordsController.FindByChainageRange | PavedRoadRecordService.FindByChainageRangeAsync | IPavedRoadRecordRepository.FindByChainageRangeAsync | PavedRoadRecordRepository.FindByChainageRangeAsync | PavedRoadRecords.Where(...).Skip/Take + CountAsync |
| Update | PavedRoadRecordsController.Update | PavedRoadRecordService.UpdateAsync | IPavedRoadRecordRepository.UpdateAsync | PavedRoadRecordRepository.UpdateAsync | PavedRoadRecords.Update |
| Delete | PavedRoadRecordsController.Delete | PavedRoadRecordService.DeleteAsync | IPavedRoadRecordRepository.DeleteAsync | PavedRoadRecordRepository.DeleteAsync | PavedRoadRecords.Remove |

## 3. Complete Request Trace: POST /api/paved-road-records

### Step-by-Step Flow

**1. HTTP Request**
```
POST /api/paved-road-records
Content-Type: application/json

{
  "workspaceId": "workspace-123",
  "sectionId": "section-456",
  "chainageFrom": 10.0,
  "chainageTo": 11.0,
  "surfaceType": 0,
  "distressType": "Pothole",
  "degree": 3,
  "extent": 2,
  "rutDepthMm": 15.5,
  "ridingQuality": "Poor",
  "skidResistance": "Low",
  "stdRef": "REF-001",
  "recommendedAction": "Repair",
  "latitude": -25.7461,
  "longitude": 28.1881,
  "imagePaths": [],
  "createdBy": "user-123"
}
```

**2. PavedRoadRecordsController.Create** (`src/RoadOps.Api/Controllers/PavedRoadRecordsController.cs`)
- **Responsibility**: HTTP request handling, input validation, response formatting
- **Action**: Receives `CreatePavedRoadRecordDto`, calls service, returns HTTP 201 Created
- **Dependencies**: `PavedRoadRecordService`

**3. PavedRoadRecordService.CreateAsync** (`src/RoadOps.Application/Services/PavedRoadRecordService.cs`)
- **Responsibility**: Business logic, validation, entity mapping
- **Action**: 
  - Validates input (non-null IDs, non-negative chainage, chainageFrom <= chainageTo)
  - Checks the workspace exists, the section exists, and the section belongs to that workspace (400 otherwise)
  - Creates new `PavedRoadRecord` entity with generated GUID
  - Sets timestamps (CreatedAt, UpdatedAt)
  - Calls repository to persist
  - Maps entity back to DTO
- **Dependencies**: `IPavedRoadRecordRepository`, `IWorkspaceRepository`, `IRoadSectionRepository`, `PavedRoadRecord` (domain entity)

**4. IPavedRoadRecordRepository.AddAsync** (`src/RoadOps.Application/Repositories/IPavedRoadRecordRepository.cs`)
- **Responsibility**: Repository interface definition
- **Action**: Contract for adding records
- **Dependencies**: `PavedRoadRecord` (domain entity)

**5. PavedRoadRecordRepository.AddAsync** (`src/RoadOps.Infrastructure/Repositories/PavedRoadRecordRepository.cs`)
- **Responsibility**: Database persistence using EF Core
- **Action**: 
  - Calls `_context.PavedRoadRecords.AddAsync(record)`
  - Calls `_context.SaveChangesAsync()`
- **Dependencies**: `RoadOpsDbContext`, `PavedRoadRecord` (domain entity)

**6. RoadOpsDbContext.PavedRoadRecords** (`src/RoadOps.Infrastructure/Data/RoadOpsDbContext.cs`)
- **Responsibility**: EF Core DbContext with DbSet declarations
- **Action**: Provides `DbSet<PavedRoadRecord>` for entity tracking
- **Dependencies**: `PavedRoadRecord` (domain entity), entity configurations

**7. PavedRoadRecordConfiguration** (`src/RoadOps.Infrastructure/Configurations/PavedRoadRecordConfiguration.cs`)
- **Responsibility**: EF Core entity mapping to database schema
- **Action**: Configures table name, column types, constraints, indexes, foreign keys
- **Dependencies**: `PavedRoadRecord` (domain entity)

**8. EF Core + PostgreSQL**
- **Responsibility**: ORM translation and database execution
- **Action**: 
  - Translates LINQ query to SQL INSERT statement
  - Opens connection to PostgreSQL
  - Executes INSERT with parameters
  - Returns generated identity (if any)
- **SQL Executed**:
```sql
INSERT INTO paved_road_records (
    id, workspace_id, section_id, chainage_from, chainage_to,
    surface_type, distress_type, degree, extent, rut_depth_mm,
    riding_quality, skid_resistance, std_ref, recommended_action,
    latitude, longitude, image_paths, created_by, created_at, updated_at
) VALUES (
    @id, @workspaceId, @sectionId, @chainageFrom, @chainageTo,
    @surfaceType, @distressType, @degree, @extent, @rutDepthMm,
    @ridingQuality, @skidResistance, @stdRef, @recommendedAction,
    @latitude, @longitude, @imagePaths, @createdBy, @createdAt, @updatedAt
)
```

**9. HTTP Response**
```
HTTP 201 Created
Location: /api/paved-road-records/{generated-id}
Content-Type: application/json

{
  "id": "generated-guid",
  "workspaceId": "workspace-123",
  "sectionId": "section-456",
  "chainageFrom": 10.0,
  "chainageTo": 11.0,
  "surfaceType": 0,
  "distressType": "Pothole",
  "degree": 3,
  "extent": 2,
  "rutDepthMm": 15.5,
  "ridingQuality": "Poor",
  "skidResistance": "Low",
  "stdRef": "REF-001",
  "recommendedAction": "Repair",
  "latitude": -25.7461,
  "longitude": 28.1881,
  "imagePaths": [],
  "createdBy": "user-123",
  "createdAt": "2026-09-18T11:20:00Z",
  "updatedAt": "2026-09-18T11:20:00Z"
}
```

### Layer Responsibilities Summary

| Layer | Responsibility | Key Concerns |
|-------|----------------|--------------|
| **Controller** | HTTP protocol, request/response, status codes | Input validation, error handling, HTTP semantics |
| **Service** | Business logic, validation, orchestration | Domain rules, data integrity, use case flow |
| **Repository Interface** | Contract definition | Abstraction, testability |
| **Repository Implementation** | Data access, persistence | EF Core usage, query optimization |
| **DbContext** | Entity tracking, change tracking | Entity lifecycle, transaction management |
| **Entity Configuration** | Schema mapping | Column types, constraints, indexes |
| **EF Core** | ORM translation | SQL generation, parameterization |
| **PostgreSQL** | Data storage | ACID properties, data integrity |

## 4. Review Checklist

### Architecture & Design
- [ ] **Why is this a foreign key?** - `RoadSection.WorkspaceId` and `PavedRoadRecord.WorkspaceId`/`SectionId` are foreign keys because they establish the hierarchical relationship: Workspace → RoadSection → PavedRoadRecord. This ensures referential integrity and enables cascade deletes.
- [ ] **Why does this repository exist?** - Each repository encapsulates data access logic for a specific entity, following the Repository pattern. This separates data access from business logic and makes the application testable.
- [ ] **Why is this interface in Application?** - Repository interfaces are in Application because they represent application-level contracts. Infrastructure implements these contracts, keeping Application independent of EF Core/PostgreSQL.
- [ ] **Why is DbContext in Infrastructure?** - DbContext is infrastructure-specific (EF Core, PostgreSQL). Placing it in Infrastructure keeps the Domain and Application layers free from database dependencies.
- [ ] **Why is this dependency injected?** - Services and repositories are injected to enable loose coupling, testability, and adherence to the Dependency Inversion Principle.
- [ ] **Why is this query in the repository?** - Queries are in repositories because they represent data access logic. Services orchestrate business logic but delegate data access to repositories.
- [ ] **Why is this validation in the use case?** - Validation in services ensures business rules are enforced regardless of how the data is accessed (API, background job, etc.).
- [ ] **What SQL will EF Core ultimately execute?** - Review the entity configurations and repository methods to understand the generated SQL. Use EF Core logging or profiling tools to verify.

### Database Design
- [ ] **Why are these indexes created?** - Composite indexes `(workspace_id, chainage_from)` and `(section_id, chainage_from)` serve the paged "records by parent, ordered by chainage" queries and double as foreign-key indexes. `chainage_from`, `created_at` and `distress_type` serve range queries, default ordering and distress filtering. Columns are snake_case via `UseSnakeCaseNamingConvention()`.
- [ ] **Why is DeleteBehavior.Cascade?** - Cascade delete ensures that when a Workspace is deleted, all its RoadSections and PavedRoadRecords are automatically deleted. This maintains data integrity.
- [ ] **Why are string lengths limited?** - Column length limits (e.g., 255 for names) prevent excessive storage usage and improve query performance.
- [ ] **Why is ImagePaths an array?** - Arrays in PostgreSQL can store multiple image paths efficiently. EF Core maps this to a JSON or array column.

### Code Quality
- [ ] **Are all async methods using CancellationToken?** - Yes, all async methods accept and propagate `CancellationToken` for proper cancellation support.
- [ ] **Are null checks appropriate?** - Services validate required fields (non-null, non-empty strings) to prevent invalid data from reaching the database.
- [ ] **Are DTOs separate from entities?** - Yes, DTOs are used for API input/output to decouple the API layer from domain entities and control what data is exposed.
- [ ] **Is error handling consistent?** - Controllers catch `ArgumentException` and return 400 Bad Request. Services throw exceptions for validation failures, including references to workspaces/sections that don't exist. `DatabaseExceptionHandler` maps any remaining PostgreSQL foreign-key violation to 400 and unique violations to 409.
- [ ] **Are list endpoints bounded?** - Yes. Every list endpoint is paged (`page`, `pageSize` ≤ 500) with a stable order (ties broken by `Id`) and returns `totalCount`.

### Configuration
- [ ] **Why is connection string in appsettings.json?** - appsettings.json provides a default configuration. In production, use environment variables or Azure Key Vault for secrets.
- [ ] **Why are repositories Scoped?** - Scoped lifetime ensures each HTTP request gets its own repository instance, which is appropriate for DbContext (also Scoped).
- [ ] **Why are services Scoped?** - Services are Scoped to match the repository lifetime and ensure consistent behavior per request.

### Testing Considerations
- [ ] **Can I test services without a database?** - Yes, by mocking `IWorkspaceRepository`, `IRoadSectionRepository`, and `IPavedRoadRecordRepository`.
- [ ] **Can I test controllers without services?** - Yes, by mocking the service classes.
- [ ] **Can I test repositories without PostgreSQL?** - Yes, by using an in-memory database or SQLite for integration tests.

### Performance
- [ ] **Why use AsNoTracking?** - `AsNoTracking()` disables change tracking for read-only queries, improving performance when entities won't be modified.
- [ ] **Why order results?** - Ordering by `CreatedAt` or `ChainageFrom` provides predictable, consistent results for API consumers.
- [ ] **Are N+1 queries avoided?** - Current implementation doesn't include eager loading (Include) because relationships are simple. If you need to load related entities, add `.Include()`.

### Security
- [ ] **Is SQL injection prevented?** - Yes, EF Core parameterizes all queries, preventing SQL injection.
- [ ] **Are secrets properly managed?** - The connection string password should be moved to user secrets or environment variables in production.
- [ ] **Is CORS configured appropriately?** - Current CORS policy allows all origins. Restrict this in production to specific domains.

### Migration & Deployment
- [ ] **How to create database migrations?** - Run `dotnet ef migrations add InitialCreate` in the API project directory.
- [ ] **How to apply migrations?** - Run `dotnet ef database update` or configure automatic migration in Program.cs.
- [ ] **What happens if the database doesn't exist?** - EF Core will create the database if it doesn't exist (ensure the connection string has CREATE DATABASE permissions).
