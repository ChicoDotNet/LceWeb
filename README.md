# LceWeb

Backend and vanilla-web foundation for LCE configurable diagnostic experiences.

## Current state

The current draft PR implements the first two backend deliveries:

- ASP.NET Core on .NET 10 LTS;
- versioned hierarchical diagnostic JSON contract;
- stable GUID/question/answer identifiers;
- single-choice, multiple-choice and text questions;
- conditional visibility;
- optional multidimensional scoring metadata;
- semantic validation;
- `GET /api/diagnostics/{diagnosticId}`;
- repository abstraction shared by local and Azure persistence;
- local JSON/in-memory repository for development and CI;
- Azure Table Storage repository using `Azure.Data.Tables`;
- `DefaultAzureCredential` / Managed Identity authentication for App Service;
- PowerShell + Azure CLI scripts to provision Table Storage and seed versioned definitions;
- GitHub Actions build, runtime smoke test and PowerShell syntax validation.

The next vertical slice is the reusable vanilla JavaScript diagnostic runtime for one landing page.

## Requirements

- .NET 10 SDK
- Azure CLI + PowerShell 7 for Azure provisioning/operations

## Run locally

No Azure resources are required for normal development. With no storage configuration the API reads definitions from disk into memory.

```powershell
dotnet restore ./src/LceWeb.Api/LceWeb.Api.csproj
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

Endpoints:

```text
GET /health
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

Development definitions live under:

```text
src/LceWeb.Api/diagnostics/
```

The machine-readable contract is:

```text
contracts/diagnostic-definition.schema.json
```

Changing the semantic meaning of an existing question, answer, branch or score should create a new diagnostic version rather than silently changing historical meaning.

## Azure Table Storage

Set these App Service settings to use Azure Tables instead of the local repository:

```text
Diagnostics__Storage__Provider=AzureTable
Diagnostics__Storage__TableName=DiagnosticDefinitions
Diagnostics__Storage__TableEndpoint=https://<storage-account>.table.core.windows.net/
```

Production authentication uses the App Service Managed Identity through `DefaultAzureCredential`; no Storage Account key is required in application settings.

Provision the Storage Account/table from Azure Cloud Shell:

```powershell
./scripts/azure/provision-storage.ps1 `
  -ResourceGroupName '<resource-group>' `
  -StorageAccountName '<globally-unique-storage-name>' `
  -Location 'centralus'
```

Seed a diagnostic definition:

```powershell
./scripts/azure/seed-diagnostics.ps1 `
  -StorageAccountName '<storage-account>' `
  -DefinitionPath './src/LceWeb.Api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425.v1.json'
```

See [`docs/AZURE-STORAGE.md`](docs/AZURE-STORAGE.md) for the table layout, Managed Identity/RBAC setup, App Service settings and Cloud Shell flow.

## Repository structure

```text
.github/workflows/          CI
contracts/                  Public JSON contracts
docs/                       Delivery and operations documentation
scripts/azure/              PowerShell + Azure CLI operations
src/LceWeb.Api/             ASP.NET Core API
src/LceWeb.Api/diagnostics/ Local diagnostic definitions
```

## Infrastructure direction

Azure infrastructure is provisioned from Azure Portal / Cloud Shell using PowerShell scripts that invoke Azure CLI.

No Bicep or Terraform is used in this project.

See [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md) for the complete delivery sequence.
