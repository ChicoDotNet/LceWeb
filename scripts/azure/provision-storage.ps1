[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$StorageAccountName,

    [string]$Location,

    [string]$TableName = "DiagnosticDefinitions",

    [string]$LeadTableName = "LeadSubmissions",

    [string]$AppServicePrincipalId,

    [string]$OperatorObjectId,

    [ValidateSet("User", "ServicePrincipal", "Group")]
    [string]$OperatorPrincipalType = "User",

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

function Ensure-TableDataRole {
    param(
        [Parameter(Mandatory = $true)][string]$PrincipalId,
        [Parameter(Mandatory = $true)][string]$PrincipalType,
        [Parameter(Mandatory = $true)][string]$Scope
    )

    $roleName = "Storage Table Data Contributor"
    $existingRole = & az role assignment list `
        --assignee-object-id $PrincipalId `
        --scope $Scope `
        --role $roleName `
        --fill-principal-name false `
        --query "[0].id" `
        --output tsv

    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect role assignments for principal '$PrincipalId'."
    }

    if (-not $existingRole) {
        Invoke-AzCli role assignment create `
            --assignee-object-id $PrincipalId `
            --assignee-principal-type $PrincipalType `
            --role $roleName `
            --scope $Scope `
            --output none
    }
}

function Ensure-Table {
    param(
        [Parameter(Mandatory = $true)][string]$AccountName,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$HasOperatorAccess
    )

    $attempts = 0
    while ($attempts -lt 6) {
        $attempts++
        & az storage table create `
            --account-name $AccountName `
            --name $Name `
            --auth-mode login `
            --output none

        if ($LASTEXITCODE -eq 0) {
            return
        }

        if (-not $HasOperatorAccess) {
            throw "Table creation requires data-plane access. Rerun with -OperatorObjectId after granting 'Storage Table Data Contributor'."
        }

        if ($attempts -lt 6) {
            Write-Host "Waiting for Table Storage RBAC propagation for '$Name' (attempt $attempts/6)..."
            Start-Sleep -Seconds 10
        }
    }

    throw "Could not create or verify table '$Name' after waiting for RBAC propagation."
}

if ($SubscriptionId) {
    Invoke-AzCli account set --subscription $SubscriptionId
}

$groupExists = (& az group exists --name $ResourceGroupName) -eq "true"
if (-not $groupExists) {
    if (-not $Location) {
        throw "Resource group '$ResourceGroupName' does not exist. Provide -Location so the script can create it."
    }

    Invoke-AzCli group create --name $ResourceGroupName --location $Location --output none
}

if (-not $Location) {
    $Location = & az group show --name $ResourceGroupName --query location --output tsv
    if ($LASTEXITCODE -ne 0 -or -not $Location) {
        throw "Could not resolve the resource-group location."
    }
}

$storageExists = & az storage account show `
    --resource-group $ResourceGroupName `
    --name $StorageAccountName `
    --query name `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $storageExists) {
    Invoke-AzCli storage account create `
        --resource-group $ResourceGroupName `
        --name $StorageAccountName `
        --location $Location `
        --sku Standard_LRS `
        --kind StorageV2 `
        --min-tls-version TLS1_2 `
        --allow-blob-public-access false `
        --output none
}

$storageId = & az storage account show `
    --resource-group $ResourceGroupName `
    --name $StorageAccountName `
    --query id `
    --output tsv
if ($LASTEXITCODE -ne 0 -or -not $storageId) {
    throw "Could not resolve the storage-account resource id."
}

if (-not $OperatorObjectId) {
    $accountType = & az account show --query user.type --output tsv
    if ($LASTEXITCODE -eq 0 -and $accountType -eq "user") {
        $OperatorObjectId = & az ad signed-in-user show --query id --output tsv
        $OperatorPrincipalType = "User"
    }
}

if ($OperatorObjectId) {
    Ensure-TableDataRole `
        -PrincipalId $OperatorObjectId `
        -PrincipalType $OperatorPrincipalType `
        -Scope $storageId
}

if ($AppServicePrincipalId) {
    Ensure-TableDataRole `
        -PrincipalId $AppServicePrincipalId `
        -PrincipalType "ServicePrincipal" `
        -Scope $storageId
}

Ensure-Table `
    -AccountName $StorageAccountName `
    -Name $TableName `
    -HasOperatorAccess ([bool]$OperatorObjectId)

Ensure-Table `
    -AccountName $StorageAccountName `
    -Name $LeadTableName `
    -HasOperatorAccess ([bool]$OperatorObjectId)

$tableEndpoint = & az storage account show `
    --resource-group $ResourceGroupName `
    --name $StorageAccountName `
    --query primaryEndpoints.table `
    --output tsv

Write-Host "Storage ready."
Write-Host "  Account:           $StorageAccountName"
Write-Host "  Diagnostic table:  $TableName"
Write-Host "  Lead table:        $LeadTableName"
Write-Host "  Table endpoint:    $tableEndpoint"
Write-Host ""
Write-Host "App Service settings:"
Write-Host "  Diagnostics__Storage__Provider=AzureTable"
Write-Host "  Diagnostics__Storage__TableName=$TableName"
Write-Host "  Diagnostics__Storage__TableEndpoint=$tableEndpoint"
Write-Host "  Leads__Storage__Provider=AzureTable"
Write-Host "  Leads__Storage__TableName=$LeadTableName"
Write-Host "  Leads__Storage__TableEndpoint=$tableEndpoint"
