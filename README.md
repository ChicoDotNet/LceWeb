# LceWeb

Backend and vanilla-web foundation for LCE configurable diagnostic experiences.

## Current increment

The current draft PR now implements four slices of the configurable diagnostic pipeline:

- ASP.NET Core on .NET 10 LTS;
- versioned hierarchical diagnostic JSON contract;
- Azure Table Storage diagnostic definitions with Managed Identity support;
- reusable Vanilla JS diagnostic runtime;
- `POST /api/leads` with authoritative server-side validation and scoring;
- local/in-memory and Azure Table `LeadSubmissions` persistence;
- reusable contact capture for name, email and optional telephone with calling code;
- UTM/referrer/page URL capture;
- public-endpoint rate limiting, request-size ceiling and honeypot;
- GitHub Actions build, browser-module tests and runtime smoke tests.

Azure Communication Services Email is the next delivery. The four production HTML pages are not yet present in this repository, so the browser integration is currently demonstrated through the technical `/diagnostic-demo.html` harness rather than reconstructed production designs.

## Requirements

- .NET 10 SDK
- Node.js 22+ for browser-module tests
- PowerShell + Azure CLI for Azure provisioning scripts

## Run locally

```powershell
dotnet restore ./src/LceWeb.Api/LceWeb.Api.csproj
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

The application publishes:

```text
GET  /health
GET  /api/diagnostics/{diagnosticId}
POST /api/leads
GET  /diagnostic-demo.html
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

Development definitions live under:

```text
src/LceWeb.Api/diagnostics/
```

The machine-readable contract is:

```text
contracts/diagnostic-definition.schema.json
```

Definitions are loaded and validated when the API starts. In Azure, the same contract is loaded by GUID/version from `DiagnosticDefinitions` in Table Storage.

Changing the meaning of an existing question or answer should create a new diagnostic version rather than silently changing historical semantics.

## Lead submissions

The public lead contract is:

```text
contracts/lead-submission.schema.json
```

The browser sends contact data plus raw question/answer identifiers. It does **not** send scores/results as authoritative fields. The API reloads the exact definition version, revalidates the visible route and recomputes scores/result bands before persisting the lead.

See [`docs/LEADS.md`](docs/LEADS.md) for the submission flow and `LeadSubmissions` layout.

## Repository structure

```text
.github/workflows/          CI
contracts/                  Public JSON contracts
src/LceWeb.Api/             ASP.NET Core API
src/LceWeb.Api/Diagnostics/ Diagnostic contract/storage
src/LceWeb.Api/Leads/       Lead validation/storage
src/LceWeb.Api/diagnostics/ Local diagnostic definitions
src/LceWeb.Api/wwwroot/     Shared Vanilla JS runtime + technical harness
scripts/azure/              Cloud Shell PowerShell/Azure CLI scripts
tests/js/                   Browser-module unit tests
docs/                       Delivery and operations documentation
```

## Infrastructure direction

Azure infrastructure is provisioned from Azure Portal / Cloud Shell using PowerShell scripts that invoke Azure CLI.

No Bicep or Terraform is planned for this project.

Start with:

- [`docs/AZURE-STORAGE.md`](docs/AZURE-STORAGE.md)
- [`docs/LEADS.md`](docs/LEADS.md)
- [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md)
