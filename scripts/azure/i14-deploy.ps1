# LCE I09–I14 deployment for Azure Cloud Shell PowerShell.
# Download the 'lceweb-package' artifact from the successful I14 package run.
# Default behavior: deploy only, preserve all existing app settings.
# Optional: -EnableLeads -TestLead to switch on capture and verify a synthetic submission.
[CmdletBinding()]
param(
  [string]$ArtifactPath = "$HOME/lceweb-package.zip",
  [switch]$EnableLeads,
  [switch]$TestLead
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$sub="cde3bbad-ead8-41d9-bbed-cbd64987f008"
$rg="lce-murus-common-rg"
$app="lce-glt"
$st="lcemuruscommonst"
$id="16f5812b-6a27-44f6-b5b4-557a720a6425"
$rev="2026-11-13-lce-diagnostico-v1"
$base="https://$app.azurewebsites.net"
$work=Join-Path $HOME ("lce-release-"+[guid]::NewGuid().ToString("N"))
if (-not (Test-Path $ArtifactPath)) {
  throw "Descarga el artefacto I14 y subelo a Cloud Shell: $ArtifactPath"
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Expand-Archive -LiteralPath $ArtifactPath -DestinationPath $work
$zip=Join-Path $work "lceweb.zip"
if (-not (Test-Path $zip)) { throw "El artefacto no incluye lceweb.zip" }
$unpack=Join-Path $work "inspect"
Expand-Archive -LiteralPath $zip -DestinationPath $unpack
$required=@(
  "LceWeb.Api.dll",
  "wwwroot/index.html",
  "wwwroot/privacidad.html",
  "wwwroot/assets/control-tower.css",
  "wwwroot/assets/service-pages.css",
  "wwwroot/assets/landing-control-tower.css",
  "wwwroot/assets/landing-experiments.js",
  "wwwroot/assets/clarity-bundle.js",
  "wwwroot/servicios/planeacion.html",
  "wwwroot/servicios/logistica.html",
  "wwwroot/servicios/normas-nom.html",
  "wwwroot/l/costos-importacion.html",
  "wwwroot/l/cumplimiento-nom.html",
  "wwwroot/l/diagnostico-logistico.html"
)
foreach($p in $required){if(-not(Test-Path (Join-Path $unpack $p))){throw "Archivo ausente en el paquete: $p"}}
$notice=Get-Content (Join-Path $unpack "wwwroot/privacidad.html") -Raw
if(-not $notice.Contains("data-lce-privacy-revision=`"$rev`"")){throw "Revision de aviso incorrecta"}
Write-Host "[OK] El paquete contiene las 3 landing pages, 3 servicios, Home y Clarity."

az account set --subscription $sub
if($LASTEXITCODE -ne 0){throw "Error seleccionando suscripcion"}
$web=(az webapp show -g $rg -n $app -o json | ConvertFrom-Json)
if($LASTEXITCODE -ne 0 -or $web.serverFarmId -notlike "*/lce-murus-asp"){throw "Destino de Azure inesperado"}
$runtime=az webapp config show -g $rg -n $app --query linuxFxVersion -o tsv
if($runtime -ne "DOTNETCORE|10.0"){throw "Runtime inesperado: $runtime"}

Write-Host "`n=== DESPLEGANDO I09–I14 ==="
az webapp deploy -g $rg -n $app --src-path $zip --type zip
if($LASTEXITCODE -ne 0){throw "Error de despliegue"}

$paths=@(
  "/","/health","/privacidad.html","/diagnostico.html",
  "/assets/analytics-consent.js","/assets/clarity-bundle.js",
  "/assets/control-tower.css","/assets/landing-control-tower.css",
  "/servicios/planeacion.html","/servicios/logistica.html","/servicios/normas-nom.html",
  "/l/costos-importacion.html","/l/cumplimiento-nom.html","/l/diagnostico-logistico.html"
)
$ready=$false
for($i=1;$i -le 12;$i++){
  try{
    $failures=@()
    foreach($p in $paths){
      $r=Invoke-WebRequest "$base$p" -SkipHttpErrorCheck -TimeoutSec 20 -Headers @{"Cache-Control"="no-cache"}
      if([int]$r.StatusCode -ne 200){$failures+=("$p : "+[int]$r.StatusCode)}
    }
    if($failures.Count -eq 0){$ready=$true;break}
    Write-Host ("Intento {0}: {1}" -f $i,($failures -join "; "))
  }catch{Write-Host "Intento $i : $($_.Exception.Message)"}
  Start-Sleep -Seconds 5
}
if(-not $ready){throw "Algunas rutas no responden HTTP 200. La captacion existente NO fue modificada."}
Write-Host "[OK] Las 14 rutas responden HTTP 200."

# Optional user-directed production activation; never silently undo existing settings.
if($EnableLeads){
  Write-Host "`n=== HABILITANDO LEADS (SIN ROLLBACK AUTOMATICO) ==="
  az webapp config appsettings set -g $rg -n $app --settings `
    "Leads__Storage__Provider=AzureTable" `
    "Leads__Storage__TableName=LceLeadSubmissions" `
    "Leads__Storage__TableEndpoint=https://$st.table.core.windows.net/" `
    "Leads__PrivacyNoticeVersion=$rev" `
    "Leads__PrivacyNoticeApproved=true" `
    "Leads__PublicCaptureEnabled=true" -o none
  if($LASTEXITCODE -ne 0){throw "No se pudieron configurar las banderas de captacion"}
  az webapp restart -g $rg -n $app -o none
  if($LASTEXITCODE -ne 0){throw "Fallo reinicio"}
}
$cap=$null
for($i=1;$i -le 30;$i++){
  try{
    $cap=Invoke-RestMethod "$base/api/public-capabilities?t=$i" -TimeoutSec 12 -Headers @{"Cache-Control"="no-cache"}
    $capVersion = if($cap.PSObject.Properties.Name -contains "privacyNoticeVersion") { [string]$cap.privacyNoticeVersion } else { "" }
    if(-not $EnableLeads -or ($cap.leadCaptureEnabled -eq $true -and $capVersion -eq $rev)){break}
  }catch{Write-Host "Esperando API: $i"}
  Start-Sleep -Seconds 5
}
if($null -eq $cap){throw "API sin respuesta; la configuracion anterior no fue revertida"}
$capVersion = if($cap.PSObject.Properties.Name -contains "privacyNoticeVersion") { [string]$cap.privacyNoticeVersion } else { "(no publicada)" }
Write-Host ("Captacion={0}; Revision={1}" -f $cap.leadCaptureEnabled,$capVersion)
if($EnableLeads -and $cap.leadCaptureEnabled -ne $true){
  Write-Warning "La API no reconoce la activacion. Los App Settings no se revirtieron; inspecciona el entorno y la configuracion antes de recibir clientes."
}
if($TestLead){
  if($cap.leadCaptureEnabled -ne $true){throw "Prueba de lead cancelada: la API no confirma captacion activa"}
  $lead=@{
    diagnosticId=$id;definitionVersion=1;privacyAccepted=$true;privacyNoticeVersion=$rev
    contact=@{name="PRUEBA TECNICA I14";email="lce-synthetic@example.com"}
    answers=@(
      @{questionId="stage";answerIds=@("planning")},
      @{questionId="priorities";answerIds=@("cost")}
    )
    acquisition=@{utmSource="i14-synthetic";pageUrl="$base/l/diagnostico-logistico.html"}
    website=""
  } | ConvertTo-Json -Depth 12 -Compress
  $r=Invoke-WebRequest "$base/api/leads" -Method Post -ContentType "application/json" -Body $lead -SkipHttpErrorCheck -TimeoutSec 30
  if([int]$r.StatusCode -ne 201){throw "POST /api/leads devolvio HTTP $([int]$r.StatusCode): $($r.Content)"}
  $saved=$r.Content | ConvertFrom-Json
  $key=[string]$saved.submissionId
  if(-not $key){throw "No se recibio submissionId"}
  az storage entity show --account-name $st --table-name LceLeadSubmissions --partition-key $id --row-key $key --auth-mode login --query "{Registro:RowKey,Version:PrivacyNoticeVersion}" -o json
  if($LASTEXITCODE -ne 0){throw "La insercion no se pudo comprobar en Azure Tables"}
  Write-Host "[OK] Prospecto sintetico persistido: $key"
}
Write-Host "`n[I14] Despliegue verificado. La configuracion de leads existente no se desactivo."
