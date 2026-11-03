# Driving Licence Onboarding Workflow System

A .NET 8 backend for submitting and processing driving licence applications through an asynchronous onboarding workflow. The system is split into an HTTP API, a background worker, and a shared library, with PostgreSQL persistence and RabbitMQ messaging planned as the workflow infrastructure.

## Solution structure

- `DrivingLicenceWorkflow.Api` — ASP.NET Core Web API
- `DrivingLicenceWorkflow.Worker` — background service host
- `DrivingLicenceWorkflow.Shared` — shared workflow, domain, and persistence components
