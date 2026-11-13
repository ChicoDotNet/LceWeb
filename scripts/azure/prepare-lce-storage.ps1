# I05: Storage preparation for LCE. No runtime or website content changes.
# Default = preview. Use -Apply for Azure modifications.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9]{3,24}$')]
    [string]$StorageAccountName,
    [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
    [string]$ResourceGroupName = "lce-murus-common-rg",
    [string]$WebAppName = "lce-glt",
    [switch]$Apply,
    [switch]$CreateStorageIfMissing,
    [string]$OperatorObjectId,
    [ValidateSet("User", "ServicePrincipal", "Group")]
    [string]$OperatorPrincipalType = "User"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$diagnosticId = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$roleName = "Storage Table Data Contributor"

function Invoke-AzCli {
    param([string[]]$CliArgs)
    & az @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI fallo: az $($CliArgs -join ' ')"
    }
}

function Invoke-AzJson {
    param([string[]]$CliArgs)
    $output = & az @CliArgs --output json --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI fallo: az $($CliArgs -join ' ')"
    }
    return ConvertFrom-Json -InputObject ($output -join [Environment]::NewLine)
}

function Ensure-Role {
    param([string]$PrincipalId, [string]$PrincipalType, [string]$Scope)
    $existing = & az role assignment list --assignee-object-id $PrincipalId --scope $Scope --role $script:roleName --include-inherited --query "[].id" -o tsv --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudieron consultar los roles del principal $PrincipalId."
    }
    if ([string]::IsNullOrWhiteSpace([string]($existing -join ""))) {
        if ($script:Apply) {
            Invoke-AzCli -CliArgs @("role", "assignment", "create", "--assignee-object-id", $PrincipalId, "--assignee-principal-type", $PrincipalType, "--role", $script:roleName, "--scope", $Scope, "-o", "none")
            Write-Host "[OK] RBAC asignado: $PrincipalType"
        }
        else {
            Write-Host "[PENDIENTE] RBAC para $PrincipalType"
        }
    }
    else {
        Write-Host "[OK] RBAC existente: $PrincipalType"
    }
}

Invoke-AzCli -CliArgs @("account", "set", "--subscription", $SubscriptionId)
$group = Invoke-AzJson -CliArgs @("group", "show", "-n", $ResourceGroupName)
$app = Invoke-AzJson -CliArgs @("webapp", "show", "-g", $ResourceGroupName, "-n", $WebAppName)
if ($app.serverFarmId -notlike "*/lce-murus-asp") {
    throw "Plan de App Service inesperado: $($app.serverFarmId)"
}
Write-Host "I05: $WebAppName en $($group.location); modo $(if ($Apply) { 'APPLY' } else { 'READ ONLY' })"

$accounts = Invoke-AzJson -CliArgs @("storage", "account", "list", "-g", $ResourceGroupName)
$storage = @($accounts | Where-Object { $_.name -eq $StorageAccountName }) | Select-Object -First 1

if ($null -eq $storage) {
    if (-not ($Apply -and $CreateStorageIfMissing)) {
        Write-Warning "La cuenta '$StorageAccountName' no existe. Para crearla expresamente: -Apply -CreateStorageIfMissing."
        exit 2
    }
    Write-Host "Creando StorageV2 Standard_LRS '$StorageAccountName'. Puede generar cargos Azure."
    Invoke-AzCli -CliArgs @(
        "storage", "account", "create", "-g", $ResourceGroupName, "-n", $StorageAccountName,
        "--location", [string]$group.location, "--sku", "Standard_LRS", "--kind", "StorageV2",
        "--min-tls-version", "TLS1_2", "--allow-blob-public-access", "false", "-o", "none"
    )
}

$storage = Invoke-AzJson -CliArgs @("storage", "account", "show", "-g", $ResourceGroupName, "-n", $StorageAccountName)
if ($storage.kind -ne "StorageV2") {
    throw "La cuenta debe ser StorageV2. Tipo recibido: $($storage.kind)"
}
$tableEndpoint = [string]$storage.primaryEndpoints.table
if ([string]::IsNullOrWhiteSpace($tableEndpoint)) {
    throw "No existe endpoint Table en la cuenta."
}
Write-Host "[OK] Storage: $StorageAccountName / $tableEndpoint"

$identity = Invoke-AzJson -CliArgs @("webapp", "identity", "show", "-g", $ResourceGroupName, "-n", $WebAppName)
$appPrincipal = [string]$identity.principalId
if ([string]::IsNullOrWhiteSpace($appPrincipal)) {
    if ($Apply) {
        $identity = Invoke-AzJson -CliArgs @("webapp", "identity", "assign", "-g", $ResourceGroupName, "-n", $WebAppName)
        $appPrincipal = [string]$identity.principalId
        if ([string]::IsNullOrWhiteSpace($appPrincipal)) {
            throw "No se obtuvo principalId al habilitar Managed Identity."
        }
        Write-Host "[OK] Managed Identity habilitada."
    }
    else {
        Write-Host "[PENDIENTE] Managed Identity del App Service."
    }
}
else {
    Write-Host "[OK] Managed Identity existente."
}

if ([string]::IsNullOrWhiteSpace($OperatorObjectId)) {
    $operator = & az ad signed-in-user show --query id -o tsv --only-show-errors
    if ($LASTEXITCODE -eq 0) {
        $OperatorObjectId = [string]$operator
    }
}
if ([string]::IsNullOrWhiteSpace($OperatorObjectId)) {
    throw "No se pudo identificar al operador. Usa -OperatorObjectId y -OperatorPrincipalType."
}
Ensure-Role -PrincipalId $OperatorObjectId -PrincipalType $OperatorPrincipalType -Scope ([string]$storage.id)
if (-not [string]::IsNullOrWhiteSpace($appPrincipal)) {
    Ensure-Role -PrincipalId $appPrincipal -PrincipalType "ServicePrincipal" -Scope ([string]$storage.id)
}

if (-not $Apply) {
    Write-Host "Preflight completado. No se crearon tablas ni se cambió la aplicación."
    exit 0
}

foreach ($table in @("DiagnosticDefinitions", "LeadSubmissions")) {
    $created = $false
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        & az storage table create --account-name $StorageAccountName --name $table --auth-mode login -o none --only-show-errors
        if ($LASTEXITCODE -eq 0) {
            $created = $true
            break
        }
        if ($attempt -lt 6) {
            Start-Sleep -Seconds 10
        }
    }
    if (-not $created) {
        throw "No se pudo crear/verificar la tabla '$table'. Revisar RBAC o Storage firewall."
    }
    Write-Host "[OK] Tabla: $table"
}

# Public diagnostic definition contains no prospect data.
$definition = Invoke-RestMethod -Uri "https://$WebAppName.azurewebsites.net/api/diagnostics/$diagnosticId" -TimeoutSec 20
if ([string]$definition.id -ne $diagnosticId -or [int]$definition.version -lt 1) {
    throw "El diagnóstico publicado no coincide con el GUID esperado."
}
$definitionJson = ConvertTo-Json -InputObject $definition -Depth 100 -Compress
if ([Text.Encoding]::Unicode.GetByteCount($definitionJson) -gt 61440) {
    throw "El diagnóstico supera el límite de 60 KiB en Azure Tables."
}
$rowKey = "v{0:D10}" -f ([int]$definition.version)
$isActive = if ([bool]$definition.isActive) { "true" } else { "false" }
$entity = @(
    "PartitionKey=$diagnosticId", "RowKey=$rowKey",
    "Version=$([int]$definition.version)", "Version@odata.type=Edm.Int32",
    "IsActive=$isActive", "IsActive@odata.type=Edm.Boolean",
    "Name=$([string]$definition.name)", "DefinitionJson=$definitionJson"
)
Invoke-AzCli -CliArgs (@(
    "storage", "entity", "insert", "--account-name", $StorageAccountName,
    "--table-name", "DiagnosticDefinitions", "--auth-mode", "login",
    "--if-exists", "replace", "--entity"
) + $entity + @("-o", "none"))
Write-Host "[OK] Definición sembrada: $diagnosticId/$rowKey"

Write-Host "I05: cuentas, roles y tablas preparados. NO se alteraron app settings ni se habilitó captación."
Write-Host "Table endpoint: $tableEndpoint"
