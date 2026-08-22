# Azure Table Storage for diagnostics and leads

The application can switch from local/in-memory persistence to Azure Table Storage without changing its public HTTP contracts.

## Runtime configuration

Local development defaults to memory-backed repositories.

For App Service / Azure Table Storage configure:

```text
Diagnostics__Storage__Provider=AzureTable
Diagnostics__Storage__TableName=DiagnosticDefinitions
Diagnostics__Storage__TableEndpoint=https://<storage-account>.table.core.windows.net/

Leads__Storage__Provider=AzureTable
Leads__Storage__TableName=LeadSubmissions
Leads__Storage__TableEndpoint=https://<storage-account>.table.core.windows.net/
```

When `Leads:Storage` is not configured explicitly, provider/endpoint/connection-string values fall back to `Diagnostics:Storage`; the lead table name still defaults to `LeadSubmissions`.

Production authentication uses `DefaultAzureCredential`. In App Service this is intended to resolve to the application's Managed Identity. No Storage Account key or connection string is required in production settings.

Connection-string settings exist only as local-development escape hatches (for example, Azurite). Do not commit production storage keys.

## DiagnosticDefinitions layout

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

The GET endpoint resolves the highest version. Lead submission resolves the exact `(diagnostic GUID, version)` used by the browser so historical answers are never revalidated against a newer definition.

## LeadSubmissions layout

Each accepted lead is one immutable submission entity:

```text
PartitionKey      = diagnostic GUID
RowKey            = submission GUID
CreatedUtc        = DateTimeOffset
DefinitionVersion = Int32
Name              = searchable contact name
Email             = searchable contact email
CallingCode       = optional
PhoneNumber       = optional
UtmSource         = optional
UtmMedium         = optional
UtmCampaign       = optional
PageUrl           = optional
EmailStatus       = Pending
SubmissionJson    = canonical complete lead submission
```

`EmailStatus=Pending` is reserved for the Azure Communication Services notification delivery increment.

## JSON property size

Azure Table string properties are UTF-16 and are limited to 64 KiB per property. The project enforces a conservative 60 KiB limit for both `DefinitionJson` and `SubmissionJson`.

If either document eventually exceeds that size, change the persistence design (for example, Blob Storage plus table metadata). Do not manually truncate canonical JSON.

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
5. creates both `DiagnosticDefinitions` and `LeadSubmissions` using Azure AD login authentication;
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

Seeding is an upsert by `(PartitionKey, RowKey)`. If a change alters semantic meaning, create a **new version** instead of replacing a version that may already have captured leads.

## Configure App Service

Once the Storage Account and Managed Identity exist:

```powershell
az webapp config appsettings set `
  --resource-group '<resource-group>' `
  --name '<app-service-name>' `
  --settings `
    Diagnostics__Storage__Provider=AzureTable `
    Diagnostics__Storage__TableName=DiagnosticDefinitions `
    Diagnostics__Storage__TableEndpoint='https://<storage-account>.table.core.windows.net/' `
    Leads__Storage__Provider=AzureTable `
    Leads__Storage__TableName=LeadSubmissions `
    Leads__Storage__TableEndpoint='https://<storage-account>.table.core.windows.net/'
```

The full App Service bootstrap is consolidated in later provisioning deliveries; this document covers the storage slice.

## Local development

No Azure resource is required for normal development:

```powershell
dotnet run --project ./src/LceWeb.Api/LceWeb.Api.csproj
```

With no storage settings, diagnostic definitions load from `src/LceWeb.Api/diagnostics/` and accepted leads are stored in memory for the process lifetime.
