# I01 — Azure preflight de LCE. Solo lectura: no crea, modifica ni elimina recursos.
# Uso (PowerShell 7, Azure CLI y sesión autenticada):
#   pwsh ./scripts/azure/check-lce-preflight.ps1
[CmdletBinding()]
param(
    [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
    [string]$ResourceGroupName = "lce-murus-common-rg",
    [string]$AppServicePlanName = "lcp-murus-asp",
    [string]$WebAppName = "lce-glt"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "No se encontro Azure CLI ('az'). Instala Azure CLI y ejecuta 'az login'."
}

function Invoke-AzJson {
    param([Parameter(Mandatory = $true)][string[]]$CliArgs)

    $output = & az @CliArgs --subscription $SubscriptionId --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI fallo: az $($CliArgs -join ' ') (exit $LASTEXITCODE)"
    }

    $json = ($output -join [Environment]::NewLine)
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "Azure CLI devolvio una respuesta vacia: az $($CliArgs -join ' ')"
    }
    return ConvertFrom-Json -InputObject $json
}

$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Preflight {
    param([string]$Name, [bool]$Condition, [string]$Detail)
    if ($Condition) {
        Write-Host "[OK] $Name — $Detail" -ForegroundColor Green
    } else {
        Write-Host "[FALTA] $Name — $Detail" -ForegroundColor Yellow
        $script:failures.Add($Name)
    }
}

Write-Host "I01 | Preflight LCE | Solo lectura" -ForegroundColor Cyan
Write-Host "Suscripcion: $SubscriptionId"
Write-Host "Grupo:       $ResourceGroupName"
Write-Host "Plan:        $AppServicePlanName"
Write-Host "App:         $WebAppName"
Write-Host ""

$account = Invoke-AzJson -CliArgs @("account", "show")
Assert-Preflight -Name "Suscripcion accesible" -Condition ($account.id -eq $SubscriptionId) -Detail ([string]$account.name)

$group = Invoke-AzJson -CliArgs @("group", "show", "--name", $ResourceGroupName)
Assert-Preflight -Name "Grupo de recursos" -Condition ($group.name -eq $ResourceGroupName) -Detail ([string]$group.location)

$app = Invoke-AzJson -CliArgs @("webapp", "show", "--name", $WebAppName, "--resource-group", $ResourceGroupName)
Assert-Preflight -Name "Web App" -Condition ($app.name -eq $WebAppName) -Detail ("Estado: " + $app.state)
Assert-Preflight -Name "Host esperado" -Condition ($app.defaultHostName -eq "$WebAppName.azurewebsites.net") -Detail ([string]$app.defaultHostName)
Assert-Preflight -Name "HTTPS obligatorio" -Condition ([bool]$app.httpsOnly) -Detail ("httpsOnly: " + $app.httpsOnly)
Assert-Preflight -Name "App iniciada" -Condition ($app.state -eq "Running") -Detail ([string]$app.state)

Write-Host ""
Write-Host "Plan real vinculado al sitio (independiente del nombre esperado):"
Write-Host "  Resource ID: $($app.serverFarmId)"
$actualPlanName = ([string]$app.serverFarmId -split "/")[-1]
Write-Host "  Nombre real: $actualPlanName"

$plan = $null
try {
    $plan = Invoke-AzJson -CliArgs @("appservice", "plan", "show", "--name", $AppServicePlanName, "--resource-group", $ResourceGroupName)
    Assert-Preflight -Name "ASP esperado existe" -Condition ($plan.name -eq $AppServicePlanName) -Detail ("SKU: " + $plan.sku.name + "; region: " + $plan.location)
} catch {
    Assert-Preflight -Name "ASP esperado existe" -Condition $false -Detail ("No se encontro '$AppServicePlanName' en '$ResourceGroupName'.")
}

$correctBinding = $null -ne $plan -and ([string]$app.serverFarmId -eq [string]$plan.id)
Assert-Preflight -Name "Vinculo con ASP esperado" -Condition $correctBinding -Detail ("ASP real: $actualPlanName; esperado: $AppServicePlanName")

try {
    $actualPlan = Invoke-AzJson -CliArgs @("resource", "show", "--ids", [string]$app.serverFarmId)
    Write-Host "  Region real: $($actualPlan.location)"
    if ($null -ne $actualPlan.sku) {
        Write-Host "  SKU real: $($actualPlan.sku.name)"
    }
} catch {
    Write-Warning "No fue posible leer los detalles del plan real: $($_.Exception.Message)"
}

$config = Invoke-AzJson -CliArgs @("webapp", "config", "show", "--name", $WebAppName, "--resource-group", $ResourceGroupName)
Write-Host ""
Write-Host "Configuracion de App Service (sin secretos):"
Write-Host "  Runtime Linux: $($config.linuxFxVersion)"
Write-Host "  Always On:     $($config.alwaysOn)"
Write-Host "  TLS minimo:    $($config.minTlsVersion)"
Write-Host "  FTPS:          $($config.ftpsState)"

try {
    $identity = Invoke-AzJson -CliArgs @("webapp", "identity", "show", "--name", $WebAppName, "--resource-group", $ResourceGroupName)
    Write-Host "  Managed Identity: $($identity.type)"
    Write-Host "  Principal ID presente: $(-not [string]::IsNullOrWhiteSpace([string]$identity.principalId))"
} catch {
    Write-Warning "No se pudo inspeccionar Managed Identity: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Prueba HTTP publica (informativa; un timeout no demuestra caida):"
foreach ($path in @("/", "/health", "/diagnostic-demo.html")) {
    $uri = "https://$WebAppName.azurewebsites.net$path"
    try {
        $response = Invoke-WebRequest -Uri $uri -TimeoutSec 12 -SkipHttpErrorCheck
        Write-Host "  $path : HTTP $([int]$response.StatusCode)"
    } catch {
        Write-Host "  $path : sin respuesta verificable ($($_.Exception.Message))" -ForegroundColor Yellow
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "PRECHECK INCOMPLETO: $($failures.Count) verificacion(es) principales fallaron." -ForegroundColor Yellow
    exit 1
}
Write-Host "PRECHECK AZURE OK: recursos y vinculacion basica verificados; no equivale a despliegue aprobado." -ForegroundColor Green
