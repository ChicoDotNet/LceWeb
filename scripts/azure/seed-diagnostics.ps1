[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StorageAccountName,

    [Parameter(Mandatory = $true)]
    [string[]]$DefinitionPath,

    [string]$TableName = "DiagnosticDefinitions",

    [string]$SubscriptionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-AzCli {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

if ($SubscriptionId) {
    Invoke-AzCli account set --subscription $SubscriptionId
}

Invoke-AzCli storage table create `
    --account-name $StorageAccountName `
    --name $TableName `
    --auth-mode login `
    --output none

foreach ($path in $DefinitionPath) {
    $resolvedPath = Resolve-Path $path
    $rawJson = Get-Content -LiteralPath $resolvedPath -Raw -Encoding UTF8

    try {
        $definition = $rawJson | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Definition '$resolvedPath' is not valid JSON: $($_.Exception.Message)"
    }

    if (-not $definition.id) {
        throw "Definition '$resolvedPath' is missing id."
    }

    $diagnosticGuid = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$definition.id, [ref]$diagnosticGuid) -or $diagnosticGuid -eq [Guid]::Empty) {
        throw "Definition '$resolvedPath' has an invalid id."
    }

    $version = [int]$definition.version
    if ($version -lt 1) {
        throw "Definition '$resolvedPath' must have version >= 1."
    }

    if ($null -eq $definition.isActive) {
        throw "Definition '$resolvedPath' is missing isActive."
    }

    if (-not $definition.name) {
        throw "Definition '$resolvedPath' is missing name."
    }

    $canonicalJson = $definition | ConvertTo-Json -Depth 100 -Compress
    $definitionBytes = [Text.Encoding]::Unicode.GetByteCount($canonicalJson)
    if ($definitionBytes -gt 61440) {
        throw "Definition '$resolvedPath' is $definitionBytes UTF-16 bytes. DefinitionJson must stay below 60 KiB so it fits safely in one Azure Table string property."
    }

    $partitionKey = $diagnosticGuid.ToString("D")
    $rowKey = "v{0:D10}" -f $version
    $isActive = if ([bool]$definition.isActive) { "true" } else { "false" }

    $entity = @(
        "PartitionKey=$partitionKey",
        "RowKey=$rowKey",
        "Version=$version",
        "Version@odata.type=Edm.Int32",
        "IsActive=$isActive",
        "IsActive@odata.type=Edm.Boolean",
        "Name=$([string]$definition.name)",
        "DefinitionJson=$canonicalJson"
    )

    Invoke-AzCli storage entity insert `
        --account-name $StorageAccountName `
        --table-name $TableName `
        --auth-mode login `
        --if-exists replace `
        --entity $entity `
        --output none

    Write-Host "Seeded diagnostic $partitionKey version $version from $resolvedPath"
}
