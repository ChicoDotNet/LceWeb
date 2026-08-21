# LceWeb

Backend and vanilla-web foundation for LCE configurable diagnostic experiences.

## Current increment

The current draft PR implements the first diagnostic vertical slice foundation:

- ASP.NET Core on .NET 10 LTS;
- versioned hierarchical diagnostic JSON contract;
- stable GUID/question/answer identifiers;
- single-choice, multiple-choice and text questions;
- conditional visibility;
- optional multidimensional scoring metadata;
- semantic validation at application startup;
- repository abstraction with an in-memory implementation;
- `GET /api/diagnostics/{diagnosticId}`;
- JSON definitions loaded from disk without changing C#;
- GitHub Actions build/smoke-test workflow.

Azure Table Storage replaces the in-memory repository in the next delivery without changing the public endpoint contract.

## Requirements

- .NET 10 SDK

## Run locally

```powershell
dotnet restore ./src/LceWeb.Api/LceWeb.Api.csproj
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

The application publishes a health endpoint:

```text
GET /health
```

and the diagnostic endpoint:

```text
GET /api/diagnostics/{diagnosticId}
```

The repository currently contains this active sample diagnostic:

```text
16f5812b-6a27-44f6-b5b4-557a720a6425
```

For example, using the URL printed by `dotnet run`:

```powershell
Invoke-RestMethod "$baseUrl/api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425"
```

## Diagnostic definitions

Development definitions currently live under:

```text
src/LceWeb.Api/diagnostics/
```

The machine-readable contract is:

```text
contracts/diagnostic-definition.schema.json
```

Definitions are loaded and validated when the API starts. Changing the diagnostic JSON changes the API response without recompiling the application; restart the running process to reload the local in-memory set.

Changing the meaning of an existing question or answer should create a new diagnostic version rather than silently changing historical semantics.

## Repository structure

```text
.github/workflows/        CI
contracts/                Public JSON contracts
src/LceWeb.Api/           ASP.NET Core API
src/LceWeb.Api/diagnostics/ Local diagnostic definitions
docs/                     Delivery and operations documentation
```

## Infrastructure direction

Azure infrastructure will be provisioned from Azure Portal / Cloud Shell using PowerShell scripts that invoke Azure CLI.

No Bicep or Terraform is planned for this project.

See [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md) for the delivery sequence.
