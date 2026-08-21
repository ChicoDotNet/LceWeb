# Azure Table Storage for diagnostic definitions

This delivery keeps the public diagnostic endpoint unchanged while allowing the repository implementation to switch from local JSON/in-memory storage to Azure Table Storage.

## Runtime configuration

The API uses local JSON definitions by default:

```text
Diagnostics__Storage__Provider=Memory
```

For App Service / Azure Table Storage configure:

```text
Diagnostics__Storage__Provider=AzureTable
Diagnostics__Storage__TableName=DiagnosticDefinitions
Diagnostics__Storage__TableEndpoint=https://<storage-account>.table.core.windows.net/
```

Production authentication uses `DefaultAzureCredential`. In App Service this is intended to resolve to the application's Managed Identity. No Storage Account key or connection string is required in production settings.

`Diagnostics__Storage__ConnectionString` exists only as a local-development escape hatch (for example, Azurite). Do not commit connection strings or production storage keys.

## Table layout

Each diagnostic version is one entity:

```text
PartitionKey   = diagnostic GUID in canonical D format
RowKey         = v + zero-padded 10-digit version
Version        = Int32
IsActive       = Boolean
Name           = diagnostic display name
DefinitionJson = canonical hierarchical JSON
```

Example:

```text
PartitionKey = 16f5812b-6a27-44f6-b5b4-557a720a6425
RowKey       = v0000000001
```

The API queries only the diagnostic partition and returns the highest version. The existing endpoint then returns `410 Gone` when that latest definition is inactive.

Historical versions remain in the same partition, which means a future lead submission can retain the exact definition version used at capture time.

## Definition size

Azure Table string properties are UTF-16 and are limited to 64 KiB per property. `seed-diagnostics.ps1` enforces a conservative 60 KiB limit for `DefinitionJson`.

If definitions eventually exceed that size, change the persistence design (for example, chunked properties or Blob Storage plus table metadata). Do not manually truncate diagnostic JSON.

## Provision from Azure Cloud Shell

Open Azure Portal → Cloud Shell → PowerShell and clone the repository/branch.

Run:

```powershell
./scripts/azure/provision-storage.ps1 `
  -ResourceGroupName '<resource-group>' `
  -StorageAccountName '<globally-unique-storage-name>' `
  -Location 'centralus'
```

The script is idempotent where practical. It:

1. creates the resource group when needed;
2. creates a `StorageV2` account using `Standard_LRS`;
3. grants the Cloud Shell operator `Storage Table Data Contributor` when the signed-in principal is a user;
4. optionally grants the App Service Managed Identity the same data-plane role;
5. creates `DiagnosticDefinitions` using Azure AD login authentication;
6. prints the App Service settings required by the API.

If the App Service already exists, pass its Managed Identity principal id:

```powershell
$appPrincipalId = az webapp identity show `
  --resource-group '<resource-group>' `
  --name '<app-service-name>' `
  --query principalId `
  --output tsv

./scripts/azure/provision-storage.ps1 `
  -ResourceGroupName '<resource-group>' `
  -StorageAccountName '<storage-account>' `
  -AppServicePrincipalId $appPrincipalId
```

If Cloud Shell is running under a service principal rather than a user, pass `-OperatorObjectId` and, when necessary, `-OperatorPrincipalType ServicePrincipal`.

Role assignment propagation can take a short period. The script retries table creation before failing with an explicit error.

## Seed a diagnostic

Seed the sample definition:

```powershell
./scripts/azure/seed-diagnostics.ps1 `
  -StorageAccountName '<storage-account>' `
  -DefinitionPath './src/LceWeb.Api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425.v1.json'
```

Seed several files in one execution:

```powershell
./scripts/azure/seed-diagnostics.ps1 `
  -StorageAccountName '<storage-account>' `
  -DefinitionPath @(
    './diagnostics/page-a.v1.json',
    './diagnostics/page-b.v1.json'
  )
```

Seeding is an upsert by `(PartitionKey, RowKey)`. Re-seeding the same GUID/version replaces that entity. If a change alters the semantic meaning of questions, answers, branching, or scoring, create a **new version** instead of replacing a version that may already have captured leads.

## Configure App Service

Once the Storage Account and Managed Identity exist, configure the app without storing credentials:

```powershell
az webapp config appsettings set `
  --resource-group '<resource-group>' `
  --name '<app-service-name>' `
  --settings `
    Diagnostics__Storage__Provider=AzureTable `
    Diagnostics__Storage__TableName=DiagnosticDefinitions `
    Diagnostics__Storage__TableEndpoint='https://<storage-account>.table.core.windows.net/'
```

The full App Service bootstrap will be consolidated later into the top-level Azure provisioning scripts. This delivery intentionally isolates the Storage/diagnostic slice first.

## Local development

No Azure resource is required for normal development:

```powershell
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

With no storage settings, the API loads JSON from `src/LceWeb.Api/diagnostics/` into the in-memory repository and exposes the same HTTP contract used in Azure.
