# Implementation Plan: Driving Licence Onboarding Workflow

## Overview

This plan implements the Driving Licence Onboarding Workflow System (DloWkf) as a .NET 8+ solution with three projects: `DrivingLicenceWorkflow.Api` (ASP.NET Core Web API), `DrivingLicenceWorkflow.Worker` (background service hosting a MassTransit saga), and `DrivingLicenceWorkflow.Shared` (class library). The workflow uses a MassTransit orchestration saga (Automatonymous) that coordinates step consumers for data validation, photo check, and risk assessment. Communication is via RabbitMQ with MassTransit, persistence to PostgreSQL via EF Core with a single shared `DbContext`, and an outbox pattern for reliable publishing. All workflow transitions are recorded in an append-only audit trail.

The system is scoped for a 6-hour implementation window. Tasks 1–11 are core; tasks 12–14 are stretch goals to be implemented if time permits. Each task includes an "Update README" sub-task to maintain the README as a living document throughout development.

## Tasks

- [ ] 1. Solution scaffold [CORE]
  - [ ] 1.1 Create .NET solution with 3 projects, project references, and NuGet packages
    - Create `DrivingLicenceWorkflow.sln` with three projects:
      - `DrivingLicenceWorkflow.Api` — ASP.NET Core Web API
      - `DrivingLicenceWorkflow.Worker` — background service (console host)
      - `DrivingLicenceWorkflow.Shared` — class library
    - Add project references: Api → Shared, Worker → Shared
    - Add NuGet packages across projects:
      - Shared: MassTransit, MassTransit.EntityFrameworkCore, EF Core Npgsql, FluentValidation
      - Api: MassTransit.RabbitMQ, Serilog (file sink), Swashbuckle.AspNetCore, EF Core Design-time
      - Worker: MassTransit.RabbitMQ, Serilog (file sink), EF Core Design-time
      - Tests: FsCheck, xUnit, MassTransit Test Harness, OpenTelemetry
    - _Requirements: 11.1, 11.4_

  - [ ] 1.2 Create .gitignore and .env.example
    - Create `.gitignore` for .NET (bin, obj, user-specific files) with `.env` listed
    - Create `.env.example` with placeholder secrets:
      ```
      POSTGRES_PASSWORD=changeme
      API_DB_PASSWORD=changeme
      WORKER_DB_PASSWORD=changeme
      RABBITMQ_PASSWORD=guest
      ```
    - _Requirements: 11.1_

  - [ ] 1.3 Create README stub with basic project description
    - Add project name, one-paragraph description, solution structure overview
    - _Requirements: 11.1_

- [ ] 2. Docker Compose [CORE]
  - [ ] 2.1 Create docker-compose.yml with 4 services
    - Services: `api`, `worker`, `database` (PostgreSQL), `queue` (RabbitMQ)
    - `env_file: .env` directive on api and worker services
    - Healthchecks for database (`pg_isready`) and queue (RabbitMQ management plugin)
    - Dependency ordering: database and queue start first (`depends_on` with `condition: service_healthy`), then api and worker
    - Environment variables for connection strings using .env secrets (e.g., `ConnectionStrings__AppDb`, `ConnectionStrings__WorkerDb`)
    - Port mappings: api on 8080, RabbitMQ management on 15672
    - _Requirements: 11.1, 11.2_

  - [ ] 2.2 Create PostgreSQL init script for database roles
    - SQL init script that creates `api_user` and `worker_user` roles with appropriate permissions on the `dl_system` database
    - `api_user`: INSERT/UPDATE/SELECT on Applications, AuditTrailEntry; full access to outbox tables
    - `worker_user`: full access to ApplicationWorkflowState, AuditTrailEntry; UPDATE on Applications.Status; full access to outbox/inbox tables
    - Mount init script via Docker volume in docker-compose.yml
    - _Requirements: 11.1_

  - [ ] 2.3 Create Dockerfiles for api and worker projects
    - Multi-stage Dockerfiles: SDK image for build, runtime image for execution
    - Expose port 8080 on api Dockerfile
    - Set entry points for each project
    - _Requirements: 11.1_

  - [ ] 2.4 Verify docker compose up works
    - Run `docker compose up --build` and confirm all 4 services start and report healthy
    - Verify API responds on port 8080 and RabbitMQ management UI is accessible
    - _Requirements: 11.1, 11.2_

  - [ ] 2.5 Update README with setup instructions
    - Document `docker compose up` workflow, .env setup (copy .env.example), endpoint URLs, Swagger path
    - _Requirements: 11.1_

- [ ] 3. Domain models + DbContext + migrations [CORE]
  - [ ] 3.1 Create domain models and entities
    - `Application` entity with all properties from design doc: Id (Guid), FirstName, LastName, DateOfBirth, NationalId, Email, PhoneNumber, Address, LicenceCategory, PhotoBase64, Status (ApplicationStatus enum), RejectionReason (string?), PhotoQualityScore (int?), CreatedAt, UpdatedAt
    - `AuditTrailEntry` entity: Id (Guid), ApplicationId (Guid), Timestamp (DateTime), FromStatus (ApplicationStatus?), ToStatus (ApplicationStatus), Event (string), Details (string?)
    - `ApplicationWorkflowState` entity implementing `SagaStateMachineInstance`: CorrelationId (Guid), CurrentState (string), ApplicationId (Guid), PhotoQualityScore (int?), FailureReason (string?), RejectionReason (string?)
    - `ApplicationStatus` enum: Submitted, ValidatingData, CheckingPhoto, RiskAssessment, PendingManualReview, Approved, Rejected, Failed
    - Configure EF Core relationships in model (Application → AuditTrailEntry one-to-many, Application → ApplicationWorkflowState one-to-one)
    - _Requirements: 5.1, 4.2, 5.3_

  - [ ] 3.2 Create DrivingLicenceDbContext
    - Single `DbContext` class mapping all entities: Application, AuditTrailEntry, ApplicationWorkflowState
    - Add MassTransit outbox/inbox tables: `AddEntityFrameworkOutbox` integration (OutboxMessage, OutboxState, InboxState)
    - Configure table names, constraints, indexes (e.g., index on AuditTrailEntry.ApplicationId for history queries)
    - Both API and Worker will use this same DbContext class with different connection strings
    - _Requirements: 5.3, 9.3, 10.2_

  - [ ] 3.3 Create EF Core migration
    - Generate initial migration from the DbContext
    - Ensure migration creates all entity tables and MassTransit outbox/inbox tables
    - _Requirements: 5.3_

  - [ ] 3.4 Add in-place migration on startup in Program.cs
    - Add `db.Database.MigrateAsync()` call in API Program.cs (and optionally Worker) on startup
    - Include comment block explaining trade-offs: multi-node concurrent DDL conflict risk, production uses dedicated migration job/init container, migrations must be backward compatible (add columns nullable first, never destructive in same deployment)
    - _Requirements: 11.1_

  - [ ] 3.5 Update README with data model notes
    - Document entity schema, DbContext approach (single shared context, different connection strings per project), migration strategy and trade-offs
    - _Requirements: 5.1, 5.3_

- [ ] 4. Message contracts [CORE]
  - [ ] 4.1 Create all 12 message types in Shared
    - Events (9): `ApplicationSubmitted`, `ApplicationValidated`, `ApplicationValidationFailed`, `PhotoChecked`, `PhotoCheckFailed`, `RiskApproved`, `RiskManualReviewRequired`, `ManualReviewApproved`, `ManualReviewRejected`
    - Commands (3): `ValidateApplication`, `CheckPhoto`, `AssessRisk`
    - Each message is a C# `record` with `ApplicationId` (Guid) and relevant fields (e.g., `QualityScore` on PhotoChecked, `Reason` on failure events, `RejectionReason` on ManualReviewRejected)
    - All messages implement MassTransit `CorrelatedBy<Guid>` or use `ApplicationId` property for saga correlation
    - _Requirements: 9.1, 9.4_

  - [ ] 4.2 Update README with message contract overview
    - Document the 12 message types, their kind (event vs command), publisher, and consumer
    - _Requirements: 9.1, 9.4_

- [ ] 5. Business rule services [CORE]
  - [ ] 5.1 Implement IDataValidationService + DataValidationService
    - Interface and `ApplicationData` / `ValidationResult` records in Shared
    - Validates: all required fields non-empty (firstName, lastName, dateOfBirth, nationalId, email, phoneNumber, address, licenceCategory, photoBase64), email format valid, nationalId present, applicant age >= 18 (boundary: exactly 18 passes)
    - Returns `ValidationResult` with `IsValid` bool and list of error strings
    - _Requirements: 6.2, 6.3, 1.4, 1.5, 1.6, 1.7_

  - [ ] 5.2 Implement IPhotoCheckService + PhotoCheckService
    - Interface and `PhotoCheckResult` record in Shared
    - Validates: photo present, supported file type (decode base64 header for JPEG/PNG), file size >= minimum (from RiskAssessmentOptions.MinPhotoFileSizeBytes)
    - Produces deterministic quality score derived from photo content hash (e.g., `SHA256(photoBytes).FirstByte % 100` or similar) so test outcomes are reproducible
    - Returns `PhotoCheckResult` with `IsAcceptable`, `QualityScore`, `FailureReason`
    - _Requirements: 7.1, 7.3, 7.4_

  - [ ] 5.3 Implement IRiskAssessmentService + RiskAssessmentService + RiskAssessmentOptions
    - `RiskAssessmentOptions` configuration class: Watchlist (List\<string\>), PhotoQualityThreshold (int), MinPhotoFileSizeBytes (int), MinApplicantAge (int), RiskAgeThreshold (int)
    - Interface and `RiskAssessmentInput` / `RiskAssessmentResult` records in Shared
    - Evaluates: applicant below RiskAgeThreshold (21), licence category not "B", nationalId in Watchlist, photo quality score below PhotoQualityThreshold
    - Returns `RiskAssessmentResult` with `RequiresManualReview` bool and `Reason` string (triggering condition)
    - Reads Watchlist and thresholds from `IOptions<RiskAssessmentOptions>` (application configuration)
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [ ] 5.4 Register all business rule services in DI
    - Register `IDataValidationService`, `IPhotoCheckService`, `IRiskAssessmentService` with their implementations in both API and Worker DI containers
    - Bind `RiskAssessmentOptions` from configuration (`builder.Services.Configure<RiskAssessmentOptions>(builder.Configuration.GetSection("RiskAssessment"))`)
    - _Requirements: 8.4_

  - [ ] 5.5 Update README with business rules documentation
    - Document each service's validation rules, configurable thresholds, and how to override via appsettings.json
    - _Requirements: 6.2, 7.4, 8.4_

- [ ] 6. Saga state machine [CORE]
  - [ ] 6.1 Create IAuditService interface in Shared
    - Interface with `RecordTransitionAsync(Guid applicationId, ApplicationStatus? fromStatus, ApplicationStatus toStatus, string eventName, string? details, CancellationToken)` method
    - _Requirements: 4.2, 5.3_

  - [ ] 6.2 Implement ApplicationWorkflowStateMachine
    - Inherits `MassTransitStateMachine<ApplicationWorkflowState>`
    - Define 8 states: Submitted, ValidatingData, CheckingPhoto, RiskAssessment, PendingManualReview, Approved, Rejected, Failed
    - Define 9 transitions with event correlation (`CorrelateById` for each event using `ApplicationId`):
      - Initially/Submitted handler: ApplicationSubmitted → transition to ValidatingData → send ValidateApplication command
      - During(ValidatingData): ApplicationValidated → transition to CheckingPhoto → send CheckPhoto; ApplicationValidationFailed → transition to Failed (store failure reason)
      - During(CheckingPhoto): PhotoChecked → store PhotoQualityScore on saga → transition to RiskAssessment → send AssessRisk; PhotoCheckFailed → transition to Failed
      - During(RiskAssessment): RiskApproved → transition to Approved; RiskManualReviewRequired → transition to PendingManualReview (store reason)
      - During(PendingManualReview): ManualReviewApproved → transition to Approved; ManualReviewRejected → transition to Rejected (store rejection reason)
    - `.Then()` blocks contain BOTH: (a) workflow state updates on saga instance, and (b) audit calls to `IAuditService.RecordTransitionAsync`
    - `.Then()` blocks also update `ApplicationWorkflowState.CurrentState` and `Application.Status` for status sync
    - `SetCompletedWhenFinalized` for terminal states (Approved, Rejected, Failed)
    - Inject `IAuditService` into the saga (resolved from DI container used by saga activities)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3, 7.2, 7.3, 8.2, 8.3, 10.1, 10.5_

  - [ ] 6.3 Update README with state machine overview
    - Document 8 states, 9 transitions, state diagram reference, idempotency via state guards
    - _Requirements: 5.1, 5.2, 10.1_

- [ ] 7. Step consumers [CORE]
  - [ ] 7.1 Implement ValidateApplicationConsumer
    - Consumes `ValidateApplication` command
    - Loads Application from DrivingLicenceDbContext (WorkerDb connection)
    - Calls `IDataValidationService.Validate` with application data
    - Publishes `ApplicationValidated` (valid) or `ApplicationValidationFailed` with reason (invalid)
    - _Requirements: 6.1, 6.2, 6.3_

  - [ ] 7.2 Implement CheckPhotoConsumer
    - Consumes `CheckPhoto` command
    - Loads Application from DrivingLicenceDbContext
    - Calls `IPhotoCheckService.Check` with photo base64
    - Publishes `PhotoChecked` with quality score (acceptable) or `PhotoCheckFailed` with reason (failed)
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ] 7.3 Implement RiskAssessmentConsumer
    - Consumes `AssessRisk` command
    - Loads Application and stored PhotoQualityScore from DrivingLicenceDbContext
    - Calls `IRiskAssessmentService.Assess` with risk input
    - Publishes `RiskApproved` (no manual review needed) or `RiskManualReviewRequired` with reason (manual review needed)
    - _Requirements: 8.1, 8.2, 8.3_

  - [ ] 7.4 Register all consumers in Worker DI
    - Register `ValidateApplicationConsumer`, `CheckPhotoConsumer`, `RiskAssessmentConsumer` in Worker Program.cs DI
    - _Requirements: 9.2_

  - [ ] 7.5 Update README with consumer overview
    - Document each step consumer's responsibility, input command, and output events
    - _Requirements: 6.1, 7.1, 8.1_

- [ ] 8. Audit service [CORE]
  - [ ] 8.1 Implement AuditService in Worker
    - Implements `IAuditService.RecordTransitionAsync`
    - Writes a new `AuditTrailEntry` row (Id, ApplicationId, Timestamp UTC, FromStatus, ToStatus, Event, Details)
    - Updates `Application.Status` to the new status and `Application.UpdatedAt` to current UTC
    - Both operations in the same DbContext transaction (saga transaction)
    - _Requirements: 4.2, 5.3, 9.3_

  - [ ] 8.2 Register AuditService in Worker DI
    - Register `IAuditService` → `AuditService` in Worker Program.cs DI
    - _Requirements: 4.2, 5.3_

  - [ ] 8.3 Update README with audit approach
    - Document the audit trail design: append-only entries, IAuditService called from saga .Then() blocks, status sync within same transaction
    - _Requirements: 4.2, 5.3_

- [ ] 9. API layer [CORE]
  - [ ] 9.1 Create request/response DTOs (records)
    - `SubmitApplicationRequest`: FirstName, LastName, DateOfBirth, NationalId, Email, PhoneNumber, Address, LicenceCategory, PhotoBase64
    - `SubmitApplicationResponse`: ApplicationId (Guid), Status (ApplicationStatus)
    - `ApplicationStatusResponse`: Id, Status, CreatedAt, UpdatedAt
    - `AuditTrailResponse`: list of entries with Timestamp, FromStatus, ToStatus, Event, Details
    - `ManualReviewActionRequest`: Reason (string, for reject)
    - _Requirements: 1.1, 2.1, 3.2, 4.1_

  - [ ] 9.2 Implement SubmitApplicationRequestValidator (FluentValidation)
    - Rules: FirstName, LastName, DateOfBirth, NationalId, Email, PhoneNumber, Address, LicenceCategory not empty
    - Email valid format
    - Applicant age >= 18 (from DateOfBirth)
    - PhotoBase64 present and non-empty
    - On failure, returns 400 with descriptive error listing failing fields
    - _Requirements: 1.4, 1.5, 1.6, 1.7_

  - [ ] 9.3 Implement ApplicationsController
    - `POST /api/applications`: validate via FluentValidation, persist Application with Status=Submitted + initial AuditTrailEntry, publish `ApplicationSubmitted` via IPublishEndpoint (EF Core outbox), return 200 with applicationId + status Submitted
    - `GET /api/applications/{id}`: return Id, Status, CreatedAt, UpdatedAt; 404 if not found
    - `GET /api/applications/{id}/history`: return ordered audit trail (by Timestamp); 404 if not found
    - `POST /api/applications/{id}/approve`: check precondition (status == PendingManualReview, else 409), publish `ManualReviewApproved`, return 200; 404 if not found
    - `POST /api/applications/{id}/reject`: check precondition (status == PendingManualReview, else 409), validate reason (non-empty, <= 1000 chars, else 400), publish `ManualReviewRejected` with reason, return 200; 404 if not found
    - Depends on `DrivingLicenceDbContext` for persistence/reads and `IPublishEndpoint` for publishing
    - _Requirements: 1.1, 1.3, 1.4, 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 4.1, 4.3_

  - [ ] 9.4 Register DrivingLicenceDbContext with AppDb + IPublishEndpoint in API Program.cs
    - Register `DrivingLicenceDbContext` with `AppDb` connection string (api_user credentials)
    - Configure `IPublishEndpoint` with EF Core outbox (`UseEntityFrameworkOutbox<DrivingLicenceDbContext>`)
    - _Requirements: 1.1, 10.2_

  - [ ] 9.5 Update README with API endpoints documentation
    - Document all 5 endpoints with HTTP method, path, request/response shapes, status codes
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 11.3_

- [ ] 10. MassTransit configuration [CORE]
  - [ ] 10.1 Configure MassTransit in API Program.cs
    - Register MassTransit with RabbitMQ transport (read host/credentials from environment variables)
    - `UseEntityFrameworkOutbox<DrivingLicenceDbContext>()` for transactional publishing
    - Configure publish endpoints for ApplicationSubmitted, ManualReviewApproved, ManualReviewRejected
    - API is a publisher only (no consumers)
    - _Requirements: 9.1, 9.4, 10.2_

  - [ ] 10.2 Configure MassTransit in Worker Program.cs
    - Register MassTransit with RabbitMQ transport (read host/credentials from environment variables)
    - `UseEntityFrameworkOutbox<DrivingLicenceDbContext>()` for transactional command publishing from saga
    - `AddSagaStateMachine<ApplicationWorkflowStateMachine, ApplicationWorkflowState>()` with EF Core saga repository (backed by DrivingLicenceDbContext / WorkerDb)
    - Add step consumers: `ValidateApplicationConsumer`, `CheckPhotoConsumer`, `RiskAssessmentConsumer`
    - Configure receive endpoints with retry (exponential backoff: 5 attempts, 1s min, 30s max) and error queue (DLQ):
      ```
      e.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
      e.ConfigureError(x => x.BindTo("workflow-saga-error"));
      ```
    - Configure saga receive endpoint and consumer receive endpoints
    - _Requirements: 9.2, 9.4, 10.1, 10.4, 10.6_

  - [ ] 10.3 Update README with messaging configuration
    - Document RabbitMQ + MassTransit setup, outbox pattern, retry/DLQ configuration, saga persistence
    - _Requirements: 9.4, 10.1, 10.4_

- [ ] 11. Serilog + Swagger [CORE]
  - [ ] 11.1 Configure Serilog in API and Worker Program.cs
    - Serilog as logging provider in both projects, file sink only (no console)
    - Structured JSON format (Serilog.Sinks.File with JSON formatter)
    - Rolling file with daily rotation and retention
    - Log file path configurable via environment variable (default to `logs/app-.log`)
    - Structured logging throughout: log ApplicationId, state transitions, events, errors with property destructuring
    - _Requirements: 11.4_

  - [ ] 11.2 Configure Swagger in API
    - `AddSwaggerGen` with XML comments from controller assembly
    - `UseSwagger` and `UseSwaggerUI` at `/swagger` endpoint
    - Available in all environments including local Docker Compose
    - _Requirements: 11.3_

  - [ ] 11.3 Update README with logging and Swagger documentation
    - Document Serilog configuration, log file location/format, Swagger UI access path
    - _Requirements: 11.3, 11.4_

- [ ] 12. Metrics + tracing [STRETCH]
  - [ ] 12.1 Add metrics to API and expose /metrics endpoint
    - Add prometheus-net or OpenTelemetry metrics to API
    - Expose `/metrics` endpoint ready for Prometheus scraping
    - Infrastructure metrics: HTTP request count, request duration (histogram)
    - Business metrics: applications submitted count (counter), applications per state (gauge), manual review pending count (gauge)
    - _Requirements: 11.5, 13.4_

  - [ ] 12.2 Add OpenTelemetry tracing
    - Console exporter for local dev (logs spans to structured log file)
    - MassTransit built-in instrumentation (MassTransit.Diagnostics) for trace propagation across RabbitMQ
    - ASP.NET Core and EF Core instrumentation for HTTP and database spans
    - Trace spans across: API request → outbox publish → saga processing → step consumer → database
    - Document Jaeger as staging alternative (add Jaeger container to Docker Compose for prod)
    - _Requirements: 13.4_

  - [ ] 12.3 Update README with observability documentation
    - Document business metrics that should be tracked, /metrics endpoint, tracing setup, Jaeger staging alternative
    - _Requirements: 11.5, 13.4_

- [ ] 13. Tests [CORE]
  - [ ] 13.1 Create test project structure and test framework setup
    - Create `DrivingLicenceWorkflow.Tests` project referencing all three source projects
    - Directories: `BusinessRules/`, `StateMachine/`, `Api/`, `Saga/`
    - Add FsCheck + xUnit + MassTransit Test Harness NuGet packages
    - Configure FsCheck to run minimum 100 iterations per property test
    - _Requirements: 12.1, 12.2_

  - [ ]* 13.2 Write unit tests for business rule services
    - DataValidationService: applicant exactly 18 (passes), one day under 18 (fails), missing each field, invalid email
    - PhotoCheckService: supported/unsupported file type headers, file size exactly at minimum, quality score reproducibility
    - RiskAssessmentService: each risk condition independently triggers manual review, no conditions → auto approve, configurable thresholds
    - _Requirements: 12.2_

  - [ ]* 13.3 Write unit tests for API controllers
    - Submit valid application → 200 with ID and status
    - Submit with missing fields → 400
    - GET existing application → 200 with all fields
    - GET unknown application → 404
    - Mock DbContext and IPublishEndpoint
    - _Requirements: 12.3, 1.1, 1.4, 2.1, 2.2_

  - [ ]* 13.4 Write unit tests for manual review endpoints
    - Approve/reject on unknown ID → 404
    - Approve/reject on non-PendingManualReview → 409
    - Reject with missing reason → 400
    - Reject with reason > 1000 chars → 400
    - _Requirements: 12.3, 3.3, 3.4_

  - [ ]* 13.5 Write saga tests with MassTransit test harness
    - Use `AddMassTransitTestHarness` with in-memory transport and saga repository
    - Full approval flow: ApplicationSubmitted → ValidatingData → CheckingPhoto → RiskAssessment → Approved
    - Failure flow: ApplicationSubmitted → ValidatingData → Failed (validation fails)
    - Manual review flow: ApplicationSubmitted → ... → PendingManualReview → Approved (or Rejected)
    - Assert state transitions, published commands, and consumed events
    - _Requirements: 12.1_

  - [ ]* 13.6 Write property test: Application ID uniqueness
    - **Property 1: Application ID uniqueness**
    - For any sequence of application submissions, every generated application ID is unique within that sequence
    - **Validates: Requirements 1.1, 1.3**
    - Tag: `// Feature: driving-licence-workflow, Property 1: Application ID uniqueness`
    - _Requirements: 1.1, 1.3_

  - [ ]* 13.7 Write property test: Data validation completeness
    - **Property 2: Data validation completeness**
    - For any application data, validation succeeds if and only if all required fields are non-empty, email is valid, and age >= 18. When validation fails, errors list exactly the failing fields. Boundary (age exactly 18) must pass.
    - **Validates: Requirements 1.4, 1.5, 1.6, 1.7**
    - Tag: `// Feature: driving-licence-workflow, Property 2: Data validation completeness`
    - _Requirements: 1.4, 1.5, 1.6, 1.7_

  - [ ]* 13.8 Write property test: Photo quality score determinism
    - **Property 3: Photo quality score determinism**
    - For any base64-encoded photo string, calling the photo check twice yields the same quality score, and the same photo content always produces the same score across separate runs.
    - **Validates: Requirements 7.4**
    - Tag: `// Feature: driving-licence-workflow, Property 3: Photo quality score determinism`
    - _Requirements: 7.4_

  - [ ]* 13.9 Write property test: Photo check acceptance conditions
    - **Property 4: Photo check acceptance conditions**
    - For any base64-encoded photo, the photo check accepts it if and only if the photo is present, file type is supported, file size is above minimum, and quality score is above threshold. Each failure mode independently causes rejection.
    - **Validates: Requirements 7.1, 7.3**
    - Tag: `// Feature: driving-licence-workflow, Property 4: Photo check acceptance conditions`
    - _Requirements: 7.1, 7.3_

  - [ ]* 13.10 Write property test: Risk assessment decision
    - **Property 5: Risk assessment decision**
    - For any risk assessment input, manual review is required if and only if at least one condition holds: age < 21, category != B, nationalId in watchlist, or photo quality < threshold. When none hold, result is auto-approval. Reason corresponds to satisfied condition(s).
    - **Validates: Requirements 8.1, 8.2, 8.3**
    - Tag: `// Feature: driving-licence-workflow, Property 5: Risk assessment decision`
    - _Requirements: 8.1, 8.2, 8.3_

  - [ ]* 13.11 Write property test: State machine transition invariant
    - **Property 6: State machine transition invariant**
    - For any current state and any event, the state machine accepts the transition if and only if the (state, event) pair is in the set of nine defined valid transitions. Terminal states reject all events.
    - **Validates: Requirements 5.2, 5.4, 5.5, 10.5**
    - Tag: `// Feature: driving-licence-workflow, Property 6: State machine transition invariant`
    - _Requirements: 5.2, 5.4, 5.5, 10.5_

  - [ ]* 13.12 Write property test: Audit trail count invariant
    - **Property 7: Audit trail count invariant**
    - For any sequence of N valid transitions applied to an application, the audit trail contains exactly N entries, each with timestamp and event name, and the application status equals the target state of the last transition.
    - **Validates: Requirements 4.2, 5.3, 9.3**
    - Tag: `// Feature: driving-licence-workflow, Property 7: Audit trail count invariant`
    - _Requirements: 4.2, 5.3, 9.3_

  - [ ]* 13.13 Write property test: Audit trail ordering
    - **Property 8: Audit trail ordering**
    - For any sequence of transitions, the retrieved audit history is ordered chronologically by timestamp, and every entry contains timestamp, fromStatus, toStatus, event name, and optional details.
    - **Validates: Requirements 4.1**
    - Tag: `// Feature: driving-licence-workflow, Property 8: Audit trail ordering`
    - _Requirements: 4.1_

  - [ ]* 13.14 Write property test: Idempotent duplicate message handling
    - **Property 9: Idempotent duplicate message handling**
    - For any message delivered more than once to the saga after it has progressed past the state that message targets, the duplicate delivery is a no-op: the saga does not re-transition, does not re-send commands, and the audit trail gains no additional entries.
    - **Validates: Requirements 10.1**
    - Tag: `// Feature: driving-licence-workflow, Property 9: Idempotent duplicate message handling`
    - _Requirements: 10.1_

  - [ ]* 13.15 Write property test: Manual review precondition
    - **Property 10: Manual review precondition**
    - For any application whose status is not PendingManualReview, the approve and reject endpoints return a 409 Conflict response without publishing a manual review event or recording a transition.
    - **Validates: Requirements 3.3**
    - Tag: `// Feature: driving-licence-workflow, Property 10: Manual review precondition`
    - _Requirements: 3.3_

  - [ ]* 13.16 Write property test: Step failure recording
    - **Property 11: Step failure recording**
    - For any step consumer that throws an unexpected error, the failure reason is recorded in the audit trail or application record, and the saga does not silently advance to the next state.
    - **Validates: Requirements 10.6**
    - Tag: `// Feature: driving-licence-workflow, Property 11: Step failure recording`
    - _Requirements: 10.6_

  - [ ]* 13.17 Write integration tests with Testcontainers
    - Spin up real PostgreSQL and RabbitMQ containers using Testcontainers
    - Exercise full API + worker stack: submit application, poll status, verify audit trail
    - Verify end-to-end workflow: Submitted → Approved (happy path)
    - _Requirements: 12.4_

  - [ ] 13.18 Update README with testing instructions
    - Document how to run tests (`dotnet test`), test project structure, what each test category covers, FsCheck iteration count
    - _Requirements: 12.1, 12.2, 12.3_

- [ ] 14. README final pass [STRETCH]
  - [ ] 14.1 Final README pass
    - Architecture explanation with diagram references (or links to design doc)
    - Setup instructions (docker compose up, .env setup, .env.example)
    - Design decisions and trade-offs (saga vs custom state machine, single DbContext, outbox, status sync, audit via .Then() blocks, Serilog file sink)
    - Reliability notes (idempotency via state guards, crash recovery, retry/DLQ, outbox)
    - Enhancement documentation: X-Idempotency-Key, pipeline observer audit, Testcontainers, auth, JSON workflow config
    - Production improvements section (migration strategy, logging aggregation, secrets manager)
    - AI usage notes
    - Testing instructions
    - Verify README was updated incrementally during each task (check completeness)
    - _Requirements: All_

> **Checkpoint (after task 5):** Ensure all core tests pass, ask the user if questions arise. Verify business rule services compile and basic unit tests pass.

> **Checkpoint (after task 10):** Ensure all core tests pass, ask the user if questions arise. Verify full end-to-end flow works via docker compose up and Swagger.

> **Checkpoint (after task 13):** Ensure all core tests pass, ask the user if questions arise. Verify all unit tests, saga tests, and property tests pass.

## Notes

- Tasks marked **[CORE]** (1–11, 13) are prioritized for the 6-hour implementation window. Tasks marked **[STRETCH]** (12, 14) are implemented if time permits.
- Sub-tasks marked with `*` are test-related and optional — the implementing agent may skip them. They are included for completeness and can be executed after core implementation.
- Each task includes an "Update README" sub-task per the design document's incremental README convention — the README is a living document updated as each task lands.
- Property tests (13.6–13.16) each correspond to a correctness property from the design document, annotated with the property number, validating requirements, and FsCheck tag comment.
- The design document uses C#/.NET (not pseudocode), so no language selection step was needed.
- The design overrides the requirements document's "custom state machine" approach with a MassTransit orchestration saga (Automatonymous). This is documented in the design's Design Decisions section.
- A single shared `DrivingLicenceDbContext` is used by both API and Worker with different connection strings (api_user vs worker_user) for least-privilege enforcement at the PostgreSQL role level.
- The outbox pattern is implemented via MassTransit's `UseEntityFrameworkOutbox<TDbContext>()` — no custom dispatcher code needed.
- In-place startup migration is used for the simplified single-instance setup; the comment block in Program.cs documents the multi-node production alternative.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "2.2", "3.1", "4.1", "5.1", "5.2", "5.3", "6.1", "9.1", "13.1"] },
    { "id": 2, "tasks": ["2.1", "2.3", "3.2", "5.4", "6.2", "9.2"] },
    { "id": 3, "tasks": ["2.4", "3.3", "4.2", "5.5", "6.3", "7.1", "7.2", "7.3", "8.1", "9.3"] },
    { "id": 4, "tasks": ["3.4", "13.2", "13.3", "13.4", "13.5", "13.6", "13.7", "13.8", "13.9", "13.10", "13.11", "13.12", "13.13", "13.14", "13.15", "13.16", "13.17"] },
    { "id": 5, "tasks": ["2.5", "3.5", "7.4", "9.4", "13.18"] },
    { "id": 6, "tasks": ["8.2", "9.5", "10.1"] },
    { "id": 7, "tasks": ["7.5", "8.3", "10.2", "11.2"] },
    { "id": 8, "tasks": ["10.3", "11.1"] },
    { "id": 9, "tasks": ["11.3", "12.1"] },
    { "id": 10, "tasks": ["12.2"] },
    { "id": 11, "tasks": ["12.3"] },
    { "id": 12, "tasks": ["14.1"] }
  ]
}
```
