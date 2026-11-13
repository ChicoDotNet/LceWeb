# I07 - Readiness & gated activation of public LCE lead capture.
# Preview (safe): ./scripts/azure/i07-activate-leads.ps1
# Production activation (only after legal publication + approval):
# ./scripts/azure/i07-activate-leads.ps1 -Apply -ConfirmLegalApproval -ApprovedNoticeVersion "YYYY-MM-DD-v1"
[CmdletBinding()]
param(
    [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
    [string]$ResourceGroupName = "lce-murus-common-rg",
    [string]$WebAppName = "lce-glt",
    [string]$StorageAccountName = "lcemuruscommonst",
    [string]$ApprovedNoticeVersion,
    [switch]$Apply,
    [switch]$ConfirmLegalApproval
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-AzOk {
    param([string]$Step)
    if ($LASTEXITCODE -ne 0) { throw "Fallo Azure CLI en: $Step" }
}

& az account set --subscription $SubscriptionId
Assert-AzOk "Seleccionar suscripcion"

$webJson = & az webapp show -g $ResourceGroupName -n $WebAppName --output json --only-show-errors
Assert-AzOk "Consultar App Service"
$web = $webJson | ConvertFrom-Json
if ($web.serverFarmId -notlike "*/lce-murus-asp") {
    throw "Plan de App Service distinto al esperado."
}

$base = "https://$WebAppName.azurewebsites.net"
$diagnosticId = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$capabilities = Invoke-RestMethod "$base/api/public-capabilities" -TimeoutSec 20
$health = Invoke-RestMethod "$base/health" -TimeoutSec 20
$definition = Invoke-RestMethod "$base/api/diagnostics/$diagnosticId" -TimeoutSec 20
if ($health.status -ne "ok" -or [int]$definition.version -lt 1 -or !$definition.isActive) {
    throw "API o diagnóstico no están listos."
}

$settingsJson = & az webapp config appsettings list -g $ResourceGroupName -n $WebAppName -o json --only-show-errors
Assert-AzOk "Consultar configuracion"
$settings = @{}
foreach ($entry in ($settingsJson | ConvertFrom-Json)) {
    $settings[$entry.name] = $entry.value
}

$required = @{
    "Diagnostics__Storage__Provider" = "AzureTable"
    "Diagnostics__Storage__TableName" = "LceDiagnosticDefinitions"
    "Leads__Storage__Provider" = "AzureTable"
    "Leads__Storage__TableName" = "LceLeadSubmissions"
    "Leads__Storage__TableEndpoint" = "https://$StorageAccountName.table.core.windows.net/"
}
foreach ($name in $required.Keys) {
    if ($settings[$name] -ne $required[$name]) {
        throw "Configuracion no coincide: $name."
    }
}

$notice = Invoke-WebRequest "$base/privacidad.html" -TimeoutSec 20
$noticeBody = [string]$notice.Content
$legalPlaceholdersPresent = $noticeBody -match "(?i)pendiente antes de activar el formulario|no sustituye el aviso de privacidad integral|PROPUESTA PARA VISTO BUENO|pendiente del visto bueno|no sustituye al aviso oficial"

Write-Host "I07: estado de lce-glt"
Write-Host "  Health: $($health.status)"
Write-Host "  Diagnostico: version $($definition.version), activo=$($definition.isActive)"
Write-Host "  Azure Tables: configurado"
Write-Host "  Public lead capture: $($capabilities.leadCaptureEnabled)"
Write-Host "  Aviso integral publicado y aprobado: $(if ($legalPlaceholdersPresent) { 'NO' } else { 'REQUIERE REVISION HUMANA' })"
if (-not $Apply) {
    Write-Host "  Modo: PREVIEW - No se hizo ningun cambio."
    Write-Host "  Para habilitar se requiere aviso integral publicado, revisado y versionado."
    exit 0
}

if (-not $ConfirmLegalApproval) {
    throw "No se habilita: falta -ConfirmLegalApproval."
}
if ([string]::IsNullOrWhiteSpace($ApprovedNoticeVersion) -or $ApprovedNoticeVersion -cnotmatch "^[A-Za-z0-9._-]{1,64}$") {
    throw "No se habilita: -ApprovedNoticeVersion debe ser una revision no vacia, sin espacios."
}
if ($legalPlaceholdersPresent) {
    throw "No se habilita: privacidad.html aun contiene textos provisionales. Publica primero el aviso integral revisado."
}

# The approved page must carry an explicit matching revision marker.
$marker = "data-lce-privacy-revision=" + [char]34 + $ApprovedNoticeVersion + [char]34
if (-not $noticeBody.Contains($marker)) {
    throw "No se habilita: falta el marcador $marker en el aviso publicado."
}
if ($noticeBody -notmatch "(?i)aviso de privacidad integral") {
    throw "No se habilita: el documento publicado no se identifica como aviso integral."
}
Write-Warning "El marcador solo prueba coincidencia tecnica. La aprobacion juridica es responsabilidad de LCE."

& az webapp config appsettings set -g $ResourceGroupName -n $WebAppName --settings "Leads__PrivacyNoticeVersion=$ApprovedNoticeVersion" "Leads__PrivacyNoticeApproved=true" "Leads__PublicCaptureEnabled=true" -o none
Assert-AzOk "Activar captacion"

$activated = $false
for ($attempt=1; $attempt -le 12; $attempt++) {
    try {
        $capabilities = Invoke-RestMethod "$base/api/public-capabilities" -TimeoutSec 15
        if ($capabilities.leadCaptureEnabled -eq $true -and $capabilities.privacyNoticeVersion -ceq $ApprovedNoticeVersion) {
            $activated = $true
            break
        }
    }
    catch { Write-Host "Reiniciando aplicacion: intento $attempt" }
    Start-Sleep -Seconds 5
}
if (-not $activated) {
    Write-Warning "No se verifico la captura activa. Aplicando rollback de banderas de captacion."
    & az webapp config appsettings set -g $ResourceGroupName -n $WebAppName --settings "Leads__PublicCaptureEnabled=false" "Leads__PrivacyNoticeApproved=false" -o none
    Assert-AzOk "Rollback de banderas"
    throw "Activacion fallida, captura desactivada."
}

Write-Host "[OK] I07: captacion publica activa con aviso $ApprovedNoticeVersion. Verificar un lead ficticio y su almacenamiento antes de difundir."
