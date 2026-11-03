# Requirements Document

## Introduction

This document specifies the requirements for the Driving Licence Onboarding Workflow System, a backend system built in .NET 8+ that processes citizen applications for driving licences through a multi-state asynchronous workflow. The system accepts applications via an ASP.NET Core Web API, persists them to PostgreSQL, publishes messages to a RabbitMQ queue, and processes them asynchronously through a background worker that executes data validation, simulated photo checks, and risk assessment. Every workflow transition is recorded in an audit trail.

The system is scoped for implementation within a fixed 6-hour window. Core functionality is prioritized; reliability and bonus features are documented as stretch goals to be implemented if time permits. Key architectural trade-offs are documented as design constraints in this document because the assignment explicitly requires them.

## Glossary

- **DL_System**: The complete Driving Licence Onboarding Workflow System comprising the Application_API, Workflow_Worker, database, and Message_Queue.
- **Application_API**: The ASP.NET Core Web API component that exposes HTTP endpoints for submitting applications, retrieving status, performing manual review, and viewing audit history.
- **Workflow_Worker**: The background service that consumes messages from the Message_Queue and executes workflow processing steps.
- **Workflow_Engine**: The state machine component that defines valid states, manages transitions, and enforces transition rules.
- **Data_Validator**: The component that validates application data including required fields, email format, national ID presence, and applicant age.
- **Photo_Check**: The component that performs simulated photo verification including file type, file size, and a deterministic quality score.
- **Risk_Assessment**: The component that evaluates configurable risk rules and determines whether manual review is required.
- **Audit_Trail**: The persistent, append-only record of all workflow state transitions with timestamps, from/to states, event names, and optional details.
- **Message_Queue**: The RabbitMQ-based queue, accessed via MassTransit, used for asynchronous communication between the Application_API and Workflow_Worker.
- **Watchlist**: A configurable list of national IDs that require manual review during risk assessment.
- **Application**: A driving licence application submitted by a citizen, containing personal details and a face photo.
- **Terminal_State**: A workflow state (Approved, Rejected, or Failed) from which no further transitions are permitted.

## Design Decisions and Trade-offs

The following architectural decisions are documented as constraints on the solution. The assignment explicitly requires these choices and their trade-offs to be recorded.

### Photo Handling: Base64 Payload

THE Application_API SHALL accept the face photo as a base64-encoded string within the JSON request body rather than as a multipart upload or separate endpoint.

- **Rationale**: A single JSON request simplates the endpoint, request validation, and testing. No separate upload state to manage.
- **Trade-off**: Base64 encoding adds ~33% size overhead and increases request body size and memory pressure. For a simplified system with small simulated photos this is acceptable; a production system handling large images would prefer multipart upload or a dedicated upload endpoint with streaming.

### Workflow: MassTransit Orchestration Saga (Automatonymous)

THE Workflow_Engine SHALL implement an orchestration saga using MassTransit with Automatonymous, not a custom state machine.

- **Rationale**: The saga provides step-level fault isolation (each step retries independently), automatic idempotency via state guards (duplicate messages to the wrong state are no-ops), native retry and dead-letter queue support through MassTransit consumer configuration, and a clear extensibility path for future async or external service steps. Each workflow step (data validation, photo check, risk assessment) is a separate consumer that sends completion events back to the saga, which coordinates the orchestration. MassTransit's test harness enables saga testing without a real broker.
- **Trade-off**: More moving parts than a custom state machine (saga state instance, step consumers, 12 message contracts). The saga must persist its state and handle crash recovery. A pure custom state machine would be less code but gives no built-in retry, idempotency, or broker integration, and couples all steps into a single synchronous pipeline that is harder to extend with async or external service calls.

### Queue: RabbitMQ with MassTransit

THE Message_Queue SHALL use RabbitMQ as the broker and MassTransit as the .NET transport abstraction.

- **Rationale**: MassTransit provides consumer registration, retry policies, dead-letter queue support, and message serialization out of the box, reducing boilerplate. RabbitMQ is lightweight, runs well in Docker, and is explicitly suggested by the assignment.
- **Trade-off**: MassTransit adds an abstraction layer with its own configuration model and learning curve. A system needing only raw pub/sub could use the RabbitMQ client directly for less overhead.

### Database: PostgreSQL

THE DL_System SHALL use PostgreSQL for application, audit trail, and outbox persistence.

- **Rationale**: PostgreSQL is robust, well-supported by EF Core and Npgsql, and runs reliably in Docker Compose. It is the assignment's primary suggestion.
- **Trade-off**: Heavier to set up than an embedded database like SQLite, but Docker Compose handles the lifecycle. A simpler prototype could use SQLite to reduce infrastructure, at the cost of concurrency and transactional guarantees.

## Requirements

### Requirement 1: Submit Driving Licence Application

**User Story:** As a citizen, I want to submit a driving licence application with my personal details and face photo, so that my application can be processed through the onboarding workflow.

#### Acceptance Criteria

1. WHEN a valid application request containing personal details and a face photo is received, THE Application_API SHALL persist the Application, set the initial workflow state to Submitted, publish an ApplicationSubmitted message to the Message_Queue, and return the application ID and status Submitted.
2. THE Application_API SHALL accept the face photo as a base64-encoded string within the JSON request body.
3. THE Application_API SHALL generate a unique identifier for each submitted Application.
4. IF any required field (firstName, lastName, dateOfBirth, nationalId, email, phoneNumber, address, licenceCategory) is missing, THEN THE Application_API SHALL return a 400 response with a descriptive validation error listing the missing fields.
5. IF the email format is invalid, THEN THE Application_API SHALL return a 400 response with a descriptive error.
6. IF the applicant is under 18 years of age, THEN THE Application_API SHALL return a 400 response with a descriptive error.
7. IF no face photo is provided, THEN THE Application_API SHALL return a 400 response with a descriptive error.

### Requirement 2: Retrieve Application Status

**User Story:** As a citizen or reviewer, I want to retrieve the current status of an Application, so that I can track its progress through the workflow.

#### Acceptance Criteria

1. WHEN a GET request for an existing application ID is received, THE Application_API SHALL return the application ID, current status, creation timestamp, and last update timestamp.
2. IF the application ID does not exist, THEN THE Application_API SHALL return a 404 response.

### Requirement 3: Manual Review Actions

**User Story:** As a reviewer, I want to approve or reject Applications that require manual review, so that I can make a final decision on flagged Applications.

#### Acceptance Criteria

1. WHEN an approve request is received for an Application in PendingManualReview state, THE Application_API SHALL transition the Application to Approved and record the transition in the Audit_Trail.
2. WHEN a reject request containing a reason is received for an Application in PendingManualReview state, THE Application_API SHALL transition the Application to Rejected, store the rejection reason, and record the transition in the Audit_Trail.
3. IF an approve or reject request is received for an Application not in PendingManualReview state, THEN THE Application_API SHALL return a 409 Conflict response indicating an invalid state transition.
4. IF the application ID does not exist, THEN THE Application_API SHALL return a 404 response.

### Requirement 4: Audit Trail Retrieval

**User Story:** As a reviewer or auditor, I want to view the complete history of workflow transitions for an Application, so that I can understand how an Application reached its current state.

#### Acceptance Criteria

1. WHEN a GET request for the history of an existing Application is received, THE Application_API SHALL return an ordered list of all workflow transitions including timestamp, fromStatus, toStatus, event name, and optional details.
2. THE Audit_Trail SHALL record every workflow state transition with a timestamp and the triggering event name.
3. IF the application ID does not exist, THEN THE Application_API SHALL return a 404 response.

### Requirement 5: Workflow State Machine

**User Story:** As a system operator, I want the Application to move through a defined workflow, so that processing follows a consistent, auditable, and verifiable sequence.

#### Acceptance Criteria

1. THE Workflow_Engine SHALL define the following states: Submitted, ValidatingData, CheckingPhoto, RiskAssessment, PendingManualReview, Approved, Rejected, and Failed.
2. THE Workflow_Engine SHALL only permit transitions defined in the workflow definition and SHALL reject all other transitions.
3. WHEN the Workflow_Engine executes a valid transition, THE Workflow_Engine SHALL update the Application status, record the transition in the Audit_Trail, and persist the new state.
4. WHILE an Application is in a Terminal_State, THE Workflow_Engine SHALL reject all further transition requests.
5. THE Workflow_Engine SHALL guarantee that any state transition that is not in the set of defined valid transitions is rejected, regardless of the source and target states (state machine invariant).

### Requirement 6: Data Validation Step

**User Story:** As a system operator, I want submitted Applications to be validated as a workflow step, so that incomplete or invalid data is caught during processing.

#### Acceptance Criteria

1. WHEN the Workflow_Worker consumes an ApplicationSubmitted message, THE Workflow_Worker SHALL transition the Application from Submitted to ValidatingData.
2. WHEN the Data_Validator confirms the Application data is valid, THE Workflow_Engine SHALL transition the Application from ValidatingData to CheckingPhoto.
3. IF the Data_Validator detects invalid data (missing national ID, invalid email, or applicant under 18), THEN THE Workflow_Engine SHALL transition the Application from ValidatingData to Failed and record the failure reason in the Audit_Trail.

### Requirement 7: Photo Check Step

**User Story:** As a system operator, I want the submitted face photo to be checked, so that Applications with missing or low-quality photos are caught during processing.

#### Acceptance Criteria

1. WHEN the Application transitions to CheckingPhoto, THE Photo_Check SHALL evaluate the provided face photo.
2. WHEN the Photo_Check confirms the photo is acceptable, THE Workflow_Engine SHALL transition the Application from CheckingPhoto to RiskAssessment.
3. IF the Photo_Check fails due to unsupported file type, file size too small, simulated quality score below threshold, or no photo provided, THEN THE Workflow_Engine SHALL transition the Application from CheckingPhoto to Failed and record the failure reason in the Audit_Trail.
4. THE Photo_Check SHALL produce a deterministic simulated quality score derived from the photo content so that test outcomes are reproducible.

### Requirement 8: Risk Assessment Step

**User Story:** As a system operator, I want Applications to be assessed for risk, so that higher-risk Applications are routed to manual review and lower-risk Applications can be automatically approved.

#### Acceptance Criteria

1. WHEN the Application transitions to RiskAssessment, THE Risk_Assessment SHALL evaluate the Application against configured risk rules.
2. IF the applicant is below 21, OR the licence category is not B, OR the national ID exists in the Watchlist, OR the photo quality score is below a configurable threshold, THEN THE Workflow_Engine SHALL transition the Application from RiskAssessment to PendingManualReview and record the triggering reason in the Audit_Trail.
3. WHEN none of the risk conditions in criterion 2 are met, THE Workflow_Engine SHALL transition the Application from RiskAssessment to Approved.
4. WHERE the Watchlist and photo quality threshold are configurable, THE Risk_Assessment SHALL read these values from application configuration.

### Requirement 9: Asynchronous Processing

**User Story:** As a system operator, I want Application processing to happen asynchronously outside the HTTP request, so that the API remains responsive and processing is decoupled from request handling.

#### Acceptance Criteria

1. WHEN the Application_API successfully stores a new Application, THE Application_API SHALL publish an ApplicationSubmitted message to the Message_Queue before returning the response.
2. WHEN the Workflow_Worker consumes an ApplicationSubmitted message, THE Workflow_Worker SHALL execute the workflow steps: data validation, photo check, and risk assessment in sequence.
3. THE Workflow_Worker SHALL update the Application status and record workflow history in the Audit_Trail after each step completes.
4. THE Message_Queue SHALL use RabbitMQ with MassTransit as the transport.

### Requirement 10: Reliability and Failure Handling

**User Story:** As a system operator, I want the system to handle common failure modes gracefully, so that processing is reliable and failures are observable.

#### Acceptance Criteria

1. WHEN the same ApplicationSubmitted message is consumed more than once, THE Workflow_Worker SHALL detect the duplicate and SHALL NOT re-execute workflow steps for an Application that has already progressed past the Submitted state (idempotency).
2. IF the Application_API stores the Application but fails to publish the message, THEN THE Application_API SHALL use the outbox pattern to ensure the message is eventually published (stretch).
3. WHEN the Workflow_Worker crashes during processing, THE Workflow_Worker SHALL resume processing on restart without corrupting the Application state.
4. WHEN a workflow step fails after exhausting retry attempts, THE Workflow_Worker SHALL move the message to a dead-letter queue and record the failure (stretch).
5. THE Workflow_Engine SHALL prevent all invalid state transitions and SHALL return a descriptive error when a transition is attempted from an invalid source state.
6. WHEN a workflow step encounters an unexpected error, THE DL_System SHALL record the failure reason in the Audit_Trail or application record.

### Requirement 11: Infrastructure and Deployment

**User Story:** As a developer, I want the system to run locally with a single command, so that I can develop and test without manual infrastructure setup.

#### Acceptance Criteria

1. THE DL_System SHALL run locally using a single `docker compose up` command.
2. THE Docker Compose setup SHALL start four services: api, worker, database (PostgreSQL), and queue (RabbitMQ).
3. THE Application_API SHALL expose Swagger/OpenAPI documentation at the /swagger endpoint.
4. THE DL_System SHALL use structured logging across both the Application_API and the Workflow_Worker.
5. WHERE health checks are implemented, THE Application_API and Workflow_Worker SHALL expose health check endpoints (stretch).

### Requirement 12: Testing

**User Story:** As a developer, I want automated tests covering important business logic, so that I can verify correctness and prevent regressions.

#### Acceptance Criteria

1. THE DL_System SHALL include automated tests for the Workflow_Engine covering both valid transitions and rejection of invalid transitions.
2. THE DL_System SHALL include automated tests for the simulated business rules: data validation, photo check, and risk assessment.
3. THE DL_System SHALL include automated tests for the manual review endpoints and the Audit_Trail retrieval.
4. WHERE integration tests are added, THE DL_System SHALL use Testcontainers for PostgreSQL and RabbitMQ (stretch).

### Requirement 13: Optional and Stretch Features

**User Story:** As a system operator, I want optional reliability and observability features, so that the system can be hardened toward production quality if time permits.

#### Acceptance Criteria

1. WHERE an idempotency key is provided in the submission request, THE Application_API SHALL prevent duplicate Application submissions for the same key (stretch).
2. THE Application_API SHALL support simple authentication for the manual review endpoints (stretch).
3. WHERE the workflow definition is loaded from a JSON configuration file, THE Workflow_Engine SHALL parse and apply the configured states and transitions (stretch).
4. THE DL_System SHALL include OpenTelemetry tracing across the Application_API, Workflow_Worker, and Message_Queue (stretch).
5. THE DL_System SHALL implement the outbox pattern for reliable message publishing from the Application_API (stretch).
6. THE Workflow_Worker SHALL apply a retry policy with exponential backoff for transient failures (stretch).
