# QA I15 — publicacion de Home, landings, diagnostico y privacidad.
# Azure Cloud Shell PowerShell 7
# El ZIP externo descargado de GitHub Actions contiene lceweb.zip.
# NO cambia configuracion de prospectos, Cloud Analytics ni recursos de Azure.
[CmdletBinding()]
param(
  [string]$ArtifactPath = "$HOME/lce-i15-qa-package.zip"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$subscription = "cde3bbad-ead8-41d9-bbed-cbd64987f008"
$group = "lce-murus-common-rg"
$app = "lce-glt"
$diagnosticId = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$revision = "2026-11-13-lce-diagnostico-v1"
$base = "https://$app.azurewebsites.net"

function Assert-Azure {
  param([string]$Step)
  if($LASTEXITCODE -ne 0) { throw "Azure CLI fallo en: $Step" }
}

if(-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
  Get-ChildItem -Path $HOME -Filter "*.zip" -File | Select-Object Name,Length,FullName | Format-Table
  throw "No encuentro el paquete: $ArtifactPath. Subelo primero mediante Manage files > Upload."
}
$work = Join-Path $HOME ("lce-i15-"+[guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Expand-Archive -LiteralPath $ArtifactPath -DestinationPath $work
$zip = Join-Path $work "lceweb.zip"
if(-not (Test-Path $zip)) { throw "El artefacto no contiene lceweb.zip" }
$inspect = Join-Path $work "inspeccion"
Expand-Archive -LiteralPath $zip -DestinationPath $inspect

$required = @(
  "LceWeb.Api.dll",
  "wwwroot/index.html",
  "wwwroot/diagnostico.html",
  "wwwroot/privacidad.html",
  "wwwroot/assets/site.css",
  "wwwroot/assets/control-tower.css",
  "wwwroot/assets/diagnostico.js",
  "wwwroot/assets/operational-subpages.css",
  "wwwroot/assets/landing-control-tower.css",
  "wwwroot/assets/landing-experiments.js",
  "wwwroot/assets/analytics-consent.js",
  "wwwroot/assets/clarity-bundle.js",
  "wwwroot/servicios/planeacion.html",
  "wwwroot/servicios/logistica.html",
  "wwwroot/servicios/normas-nom.html",
  "wwwroot/l/costos-importacion.html",
  "wwwroot/l/cumplimiento-nom.html",
  "wwwroot/l/diagnostico-logistico.html"
)
foreach($file in $required) {
  if(-not (Test-Path (Join-Path $inspect $file))) { throw "Paquete incompleto: $file" }
}
$diag = Get-Content (Join-Path $inspect "wwwroot/diagnostico.html") -Raw
$privacy = Get-Content (Join-Path $inspect "wwwroot/privacidad.html") -Raw
$landing = Get-Content (Join-Path $inspect "wwwroot/l/diagnostico-logistico.html") -Raw
if(-not $diag.Contains("Tu operaci`u{00F3}n merece")) { throw "ZIP incorrecto: falta diseno actualizado de diagnostico" }
if(-not $privacy.Contains("data-lce-privacy-revision=`"$revision`"")) { throw "El aviso no coincide con la version aprobada" }
if(-not $privacy.Contains("/assets/operational-subpages.css")) { throw "ZIP incorrecto: falta estilo del aviso" }
if($landing -match "El formulario s.lo aparece cuando la API|API real|CTA / FORM") { throw "Este ZIP conserva textos internos: usa el paquete I15" }
Write-Host "[OK] ZIP I15 validado: HTML, estilos, Clarity y 3 landings."

az account set --subscription $subscription
Assert-Azure "Seleccionar suscripcion"
$webJson = az webapp show -g $group -n $app -o json
Assert-Azure "Consultar Web App"
$web = $webJson | ConvertFrom-Json
if($web.serverFarmId -notlike "*/lce-murus-asp"){throw "El destino no usa el plan esperado"}
$runtime = az webapp config show -g $group -n $app --query linuxFxVersion -o tsv
Assert-Azure "Consultar runtime"
if($runtime -ne "DOTNETCORE|10.0"){throw "Runtime inesperado: $runtime"}

Write-Host "`n=== DESPLEGANDO QA I15 ==="
az webapp deploy -g $group -n $app --src-path $zip --type zip
Assert-Azure "Desplegar ZIP"

Write-Host "`n=== VALIDANDO RUTAS PUBLICAS ==="
$paths = @(
 "/", "/health", "/diagnostico.html", "/privacidad.html",
 "/assets/control-tower.css", "/assets/operational-subpages.css",
 "/assets/analytics-consent.js", "/assets/clarity-bundle.js",
 "/servicios/planeacion.html", "/servicios/logistica.html",
 "/servicios/normas-nom.html", "/l/costos-importacion.html",
 "/l/cumplimiento-nom.html", "/l/diagnostico-logistico.html"
)
$verified = $false
for($attempt=1; $attempt -le 12; $attempt++){
  $errors = @()
  foreach($path in $paths){
    try {
      $response=Invoke-WebRequest -Uri "$base$path" -SkipHttpErrorCheck -TimeoutSec 20 -Headers @{"Cache-Control"="no-cache"}
      if([int]$response.StatusCode -ne 200){$errors+=("$path HTTP "+[int]$response.StatusCode)}
    }catch{$errors+=("$path : "+$_.Exception.Message)}
  }
  if($errors.Count -eq 0){
    try{
      $publishedDiag=(Invoke-WebRequest "$base/diagnostico.html?qa=i15" -TimeoutSec 20 -Headers @{"Cache-Control"="no-cache"}).Content
      $publishedPrivacy=(Invoke-WebRequest "$base/privacidad.html?qa=i15" -TimeoutSec 20 -Headers @{"Cache-Control"="no-cache"}).Content
      $publishedLanding=(Invoke-WebRequest "$base/l/diagnostico-logistico.html?qa=i15" -TimeoutSec 20 -Headers @{"Cache-Control"="no-cache"}).Content
      if($publishedDiag -notmatch "Tu operaci.n merece"){ $errors+= "El diagnostico no muestra el nuevo diseno" }
      if($publishedPrivacy -notmatch "privacy-aside"){ $errors+= "El aviso no muestra el nuevo diseno" }
      if($publishedLanding -match "El formulario s.lo aparece cuando la API"){ $errors+= "La landing sigue con copy tecnico" }
    }catch{ $errors+= "QA HTML: $($_.Exception.Message)"}
  }
  if($errors.Count -eq 0){$verified=$true;break}
  Write-Host "Verificacion $attempt : $($errors -join '; ')"
  if($attempt -lt 12){Start-Sleep -Seconds 5}
}
if(-not $verified){throw "El despliegue se ejecuto, pero QA HTTP no se verifico. No se modificaron los App Settings."}
Write-Host "[OK] Rutas y contenido QA I15 verificados"

Write-Host "`n=== ESTADO DE DIAGNOSTICO Y REGISTRO ==="
$definition=Invoke-RestMethod "$base/api/diagnostics/$diagnosticId" -TimeoutSec 20
$cap=Invoke-RestMethod "$base/api/public-capabilities" -TimeoutSec 20
Write-Host "Definicion de diagnostico: v$($definition.version)"
Write-Host "Registro de prospectos: $($cap.leadCaptureEnabled)"
if($cap.leadCaptureEnabled -ne $true){
 Write-Warning "La captacion esta desactivada en Azure. El despliegue I15 NO cambio sus banderas."
}
Write-Host "`n=== I15 PUBLICADA ==="
Write-Host "Home: $base/"
Write-Host "Diagnostico: $base/diagnostico.html"
Write-Host "Privacidad: $base/privacidad.html"
Write-Host "Landings: $base/l/"
Write-Host "Sin cambios a App Settings, consentimientos almacenados ni Azure Tables."
