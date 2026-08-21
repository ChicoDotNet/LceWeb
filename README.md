# LceWeb

Backend and vanilla-web foundation for LCE configurable diagnostic experiences.

## Current increment

The current draft PR now includes:

- ASP.NET Core on .NET 10 LTS;
- versioned hierarchical diagnostic JSON contract;
- stable GUID/question/answer identifiers;
- single-choice, multiple-choice and text questions;
- conditional visibility;
- optional multidimensional scoring metadata;
- semantic validation;
- local JSON/in-memory storage;
- Azure Table Storage with Managed Identity support;
- PowerShell + Azure CLI provisioning/seed scripts;
- `GET /api/diagnostics/{diagnosticId}`;
- reusable vanilla-JavaScript diagnostic runtime core;
- GitHub Actions build/smoke-test workflow.

The four production HTML pages are not yet present in this repository, so the current web increment provides a reusable renderer and a technical demo harness rather than inventing one of the production page designs.

## Requirements

- .NET 10 SDK
- Node.js 20+ for diagnostic-runtime tests

## Run locally

```powershell
dotnet restore ./src/LceWeb.Api/LceWeb.Api.csproj
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

The application publishes:

```text
GET /health
GET /api/diagnostics/{diagnosticId}
```

The repository contains this active sample diagnostic:

```text
16f5812b-6a27-44f6-b5b4-557a720a6425
```

For example:

```powershell
Invoke-RestMethod "$baseUrl/api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425"
```

## Diagnostic definitions

Development definitions live under:

```text
src/LceWeb.Api/diagnostics/
```

The machine-readable contract is:

```text
contracts/diagnostic-definition.schema.json
```

Changing the meaning of an existing question or answer should create a new diagnostic version rather than silently changing historical semantics.

## Diagnostic runtime

The browser runtime is intentionally framework-free and split into:

```text
src/LceWeb.Api/wwwroot/js/diagnostics/diagnostic-core.js
src/LceWeb.Api/wwwroot/js/diagnostics/diagnostic-runtime.js
```

The core owns conditional visibility, answer validation, hidden-branch pruning, scoring and result-band resolution. The renderer owns API loading and DOM interaction.

A production landing will only need an element such as:

```html
<div
  data-diagnostic-id="16f5812b-6a27-44f6-b5b4-557a720a6425"
  data-diagnostic-api-base="">
</div>
<script type="module" src="/js/diagnostics/diagnostic-runtime.js"></script>
```

The runtime dispatches custom browser events including `diagnostic:loaded`, `diagnostic:answer` and `diagnostic:completed`. The future lead form will consume the completion payload rather than duplicating diagnostic logic.

Run the pure runtime tests with:

```powershell
npm run test:diagnostics
```

## Repository structure

```text
.github/workflows/          CI
contracts/                  Public JSON contracts
docs/                       Delivery / Azure operations docs
scripts/azure/              PowerShell + Azure CLI scripts
src/LceWeb.Api/             ASP.NET Core API + static runtime assets
src/LceWeb.Api/diagnostics/ Local diagnostic definitions
tests/js/                   Framework-free runtime tests
```

## Infrastructure direction

Azure infrastructure is provisioned from Azure Portal / Cloud Shell using PowerShell scripts that invoke Azure CLI.

No Bicep or Terraform is planned for this project.

See:

- [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md)
- [`docs/AZURE-STORAGE.md`](docs/AZURE-STORAGE.md)
