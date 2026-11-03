# AGENTS.md

Guidance for AI coding agents working on this repository.

## Project

Driving Licence Onboarding Workflow System — a .NET 8+ backend that processes citizen driving licence applications through a multi-state asynchronous workflow. Built as a technical assignment with a 6-hour implementation budget.

## Spec Documents

Read these before implementing anything:

- `.kiro/specs/driving-licence-workflow/requirements.md` — functional requirements and acceptance criteria
- `.kiro/specs/driving-licence-workflow/design.md` — technical design, interfaces, message contracts, saga definition, trade-offs
- `.kiro/specs/driving-licence-workflow/tasks.md` — 14-task implementation plan with dependencies

## Solution Structure

```
DrivingLicenceWorkflow.sln
├── DrivingLicenceWorkflow.Shared    # Domain models, messages, DbContext, services, saga, interfaces
├── DrivingLicenceWorkflow.Api        # ASP.NET Core Web API (controllers, FluentValidation, Swagger, publisher)
└── DrivingLicenceWorkflow.Worker     # Background service (saga host, step consumers, MassTransit consumers)
```

Both Api and Worker depend on Shared

## Build and Run

```bash
# Copy secrets
cp .env.example .env  # then fill in real values

# Run everything
docker compose up --build

# API available at http://localhost:8080
# Swagger at http://localhost:8080/swagger
# RabbitMQ management at http://localhost:15672
```

## Testing

```bash
dotnet test
```

- Unit tests for business rule services (DataValidationService, PhotoCheckService, RiskAssessmentService)
- Unit tests for API controllers (mock DbContext, mock IPublishEndpoint)
- Saga tests via MassTransit InMemoryTestHarness (no RabbitMQ needed)
- FsCheck property-based tests (11 properties, 100+ iterations each)
- Testcontainers integration tests documented as enhancement

## Conventions

- Each task in tasks.md includes a README update step — keep the README current as you implement
- EF Core migrations run in-place on startup (acceptable for single-instance; production would use a dedicated migration job)
- Migrations must be backward compatible
- All timestamps in UTC
- Application IDs are GUIDs (Guid.NewGuid())

## Dependencies

| Package | Purpose |
|---------|---------|
| MassTransit.RabbitMQ | Message transport |
| MassTransit.EntityFrameworkCore | Saga persistence + outbox |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL provider |
| FluentValidation | Request validation |
| Serilog.Sinks.File | Structured logging to files |
| Swashbuckle.AspNetCore | Swagger/OpenAPI |
| FsCheck | Property-based testing |
| xUnit | Test framework |
