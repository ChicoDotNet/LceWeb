# I05 — LCE Table Storage, minimum privileges. DOES NOT change web app settings.
# Preview: pwsh ./scripts/azure/prepare-lce-storage.ps1
# Apply:   pwsh ./scripts/azure/prepare-lce-storage.ps1 -Apply
# Works in Cloud Shell PowerShell with the repo script uploaded.
[CmdletBinding()]
param(
    [string]$StorageAccountName = "lcemuruscommonst",
    [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
    [string]$ResourceGroupName = "lce-murus-common-rg",
    [string]$WebAppName = "lce-glt",
    [string]$DiagnosticTable = "LceDiagnosticDefinitions",
    [string]$LeadTable = "LceLeadSubmissions",
    [switch]$Apply,
    [string]$OperatorObjectId,
    [ValidateSet("User", "ServicePrincipal", "Group")]
    [string]$OperatorPrincipalType = "User"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$diagnosticId = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$roleName = "Storage Table Data Contributor"

function Invoke-Az {
    param([string[]]$Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo Azure CLI: az $($Arguments -join ' ')"
    }
}

function Get-AzJson {
    param([string[]]$Arguments)
    $lines = & az @Arguments --output json --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo Azure CLI: az $($Arguments -join ' ')"
    }
    if (-not $lines) {
        return $null
    }
    return ConvertFrom-Json -InputObject ($lines -join [Environment]::NewLine)
}

function Ensure-TableRole {
    param([string]$Principal, [string]$Type, [string]$Scope)
    $existing = & az role assignment list --assignee-object-id $Principal --scope $Scope --include-inherited --role $script:roleName --query "[].id" -o tsv --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "No se pudieron consultar asignaciones RBAC." }
    if ([string]::IsNullOrWhiteSpace([string]($existing -join ""))) {
        if (-not $script:Apply) {
            Write-Host "[PENDIENTE] Rol por tabla para $Type"
            return
        }
        Invoke-Az -Arguments @(
            "role","assignment","create","--assignee-object-id",$Principal,
            "--assignee-principal-type",$Type,"--role",$script:roleName,
            "--scope",$Scope,"--output","none"
        )
        Write-Host "[OK] RBAC concedido para $Type a tabla $($Scope.Split('/')[-1])"
    }
    else {
        Write-Host "[OK] RBAC existente para $Type a tabla $($Scope.Split('/')[-1])"
    }
}

Invoke-Az -Arguments @("account","set","--subscription",$SubscriptionId)
$app = Get-AzJson -Arguments @("webapp","show","-g",$ResourceGroupName,"-n",$WebAppName)
if ($app.serverFarmId -notlike "*/lce-murus-asp") {
    throw "El App Service no está en lce-murus-asp."
}
$storage = Get-AzJson -Arguments @("storage","account","show","-g",$ResourceGroupName,"-n",$StorageAccountName)
if ($storage.kind -ne "StorageV2") { throw "La cuenta existente debe ser StorageV2." }
if (-not $storage.primaryEndpoints.table) { throw "La cuenta no expone Azure Table Storage." }
Write-Host "I05 LCE: modo $(if ($Apply) { 'APPLY' } else { 'SOLO LECTURA' })"
Write-Host "Web App: $WebAppName / Plan: lce-murus-asp"
Write-Host "Storage compartido: $StorageAccountName / $($storage.primaryEndpoints.table)"
Write-Host "Tablas exclusivas: $DiagnosticTable; $LeadTable"

$appPrincipal = & az webapp identity show -g $ResourceGroupName -n $WebAppName --query principalId -o tsv --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "No fue posible consultar Managed Identity." }
$appPrincipal = [string]$appPrincipal
if ([string]::IsNullOrWhiteSpace($appPrincipal)) {
    if ($Apply) {
        $identity = Get-AzJson -Arguments @("webapp","identity","assign","-g",$ResourceGroupName,"-n",$WebAppName)
        $appPrincipal = [string]$identity.principalId
        if (-not $appPrincipal) { throw "No se pudo activar Managed Identity." }
        Write-Host "[OK] Managed Identity habilitada en lce-glt."
    }
    else {
        Write-Host "[PENDIENTE] Managed Identity en lce-glt."
    }
}
else {
    Write-Host "[OK] Managed Identity disponible."
}

if (-not $OperatorObjectId) {
    $signedIn = & az ad signed-in-user show --query id -o tsv --only-show-errors
    if ($LASTEXITCODE -eq 0) { $OperatorObjectId = [string]$signedIn }
}
if (-not $OperatorObjectId) {
    throw "No se pudo identificar al operador. Indica -OperatorObjectId y -OperatorPrincipalType."
}

foreach ($tableName in @($DiagnosticTable,$LeadTable)) {
    if ($tableName -cnotmatch "^[A-Za-z][A-Za-z0-9]{2,62}$") {
        throw "Nombre inválido de tabla: $tableName"
    }
    $scope = "$($storage.id)/tableServices/default/tables/$tableName"
    & az resource show --ids $scope --api-version 2023-05-01 --output none --only-show-errors 2>$null
    $exists = ($LASTEXITCODE -eq 0)
    if (-not $exists) {
        if ($Apply) {
            # ARM management plane creates only the table resource, no account keys required.
            Invoke-Az -Arguments @(
                "resource","create","--id",$scope,"--api-version","2023-05-01",
                "--properties","{}","--output","none"
            )
            Write-Host "[OK] Tabla creada: $tableName"
        }
        else {
            Write-Host "[PENDIENTE] Tabla por crear: $tableName"
        }
    }
    else {
        Write-Host "[OK] Tabla existente: $tableName"
    }
    Ensure-TableRole -Principal $OperatorObjectId -Type $OperatorPrincipalType -Scope $scope
    if ($appPrincipal) {
        Ensure-TableRole -Principal $appPrincipal -Type "ServicePrincipal" -Scope $scope
    }
}

if (-not $Apply) {
    Write-Host "Inspección I05 lista; nada fue modificado."
    exit 0
}

# Seed only a versioned, public questionnaire. No actual customer data is processed.
$definition = Invoke-RestMethod -Uri "https://$WebAppName.azurewebsites.net/api/diagnostics/$diagnosticId" -TimeoutSec 20
if ([string]$definition.id -ne $diagnosticId -or [int]$definition.version -ne 1) {
    throw "La definición pública no coincide con la versión 1 de LCE."
}
$json = ConvertTo-Json -InputObject $definition -Depth 100 -Compress
if ([Text.Encoding]::Unicode.GetByteCount($json) -gt 61440) {
    throw "El JSON del diagnóstico excede 60 KiB."
}
$entity = @(
    "PartitionKey=$diagnosticId", "RowKey=v0000000001",
    "Version=1", "Version@odata.type=Edm.Int32",
    "IsActive=true", "IsActive@odata.type=Edm.Boolean",
    "Name=$([string]$definition.name)", "DefinitionJson=$json"
)
# Idempotent: preserve any previously seeded v1 instead of replacing it.
& az storage entity show --account-name $StorageAccountName --table-name $DiagnosticTable --auth-mode login --partition-key $diagnosticId --row-key "v0000000001" --select "Version" -o none --only-show-errors 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] Diagnóstico v1 ya existe; no será reemplazado."
    Write-Host "[OK] I05 preparada sin activar la captura pública."
    exit 0
}
$seeded = $false
for ($attempt=1; $attempt -le 8; $attempt++) {
    # Insert only: if an entity already exists we must not overwrite data.
    & az storage entity insert --account-name $StorageAccountName --table-name $DiagnosticTable --auth-mode login --if-exists fail --entity @entity --output none --only-show-errors
    if ($LASTEXITCODE -eq 0) {
        $seeded = $true
        Write-Host "[OK] Diagnóstico v1 insertado: $diagnosticId"
        break
    }
    if ($attempt -lt 8) {
        Write-Host "Reintento $attempt/8; verificando propagación RBAC antes del seed..."
        Start-Sleep -Seconds 10
    }
}
if (-not $seeded) {
    Write-Warning "No se completó seed (puede existir v1 o faltar propagación RBAC). Revisar antes de I06."
    exit 2
}
Write-Host "[OK] I05: tablas y roles preparados. La Web NO fue reconfigurada; captura pública sigue desactivada."
Write-Host "Config I06: Diagnostics__Storage__TableName=$DiagnosticTable"
Write-Host "Config I06: Leads__Storage__TableName=$LeadTable"
