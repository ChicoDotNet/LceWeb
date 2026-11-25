# Single release candidate for LCE I16–I25. Read-only preflight by default.
# Requires PowerShell 7, Azure CLI and authenticated identity with explicit deploy and table data access.
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ArtifactPath,
  [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedPackageSha256,
  [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
  [string]$ResourceGroup = "lce-murus-common-rg",
  [string]$WebApp = "lce-glt",
  [string]$StorageAccount = "lcemuruscommonst",
  [string]$ApprovedOrigin = "https://lce-glt.azurewebsites.net",
  [switch]$ApproveEditorial,
  [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$diagnosticId = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$defaultOrigin = "https://lce-glt.azurewebsites.net"
$origin = [Uri]$ApprovedOrigin
if (-not $origin.IsAbsoluteUri -or $origin.Scheme -ne "https" -or $origin.AbsolutePath -ne "/" -or $origin.Query -or $origin.Fragment -or $origin.UserInfo) {
  throw "ApprovedOrigin must be a bare HTTPS origin."
}
$ApprovedOrigin = $origin.GetLeftPart([UriPartial]::Authority)
if (-not $origin.IsDefaultPort) { throw "Use a standard HTTPS origin." }
function Az-Checked {
  param([string[]]$Arguments)
  $result = & az @Arguments --subscription $SubscriptionId --only-show-errors
  if ($LASTEXITCODE -ne 0) { throw "Azure CLI step failed: $($Arguments[0..1] -join ' ')" }
  return $result
}
$work = Join-Path ([IO.Path]::GetTempPath()) ("lce-release-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
  $outer = Join-Path $work "outer"
  Expand-Archive -LiteralPath $ArtifactPath -DestinationPath $outer
  $zip = Join-Path $outer "lceweb.zip"
  if (-not (Test-Path $zip)) { throw "Use the GitHub Actions artifact containing lceweb.zip." }
  $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
  if ($actual -ne $ExpectedPackageSha256) { throw "Package SHA256 mismatch." }
  $publish = Join-Path $work "publish"
  Expand-Archive -LiteralPath $zip -DestinationPath $publish
  $webroot = Join-Path $publish "wwwroot"
  $definition = Join-Path $publish "diagnostics/$diagnosticId.v2.json"
  $slugs = @("manifestacion-de-valor","proveedores-confiables","compliance-aduanero")
  $paths = @("/","/health","/diagnostico.html","/privacidad.html","/robots.txt","/sitemap.xml","/l/costos-importacion.html","/l/cumplimiento-nom.html","/l/diagnostico-logistico.html","/js/diagnostics/commercial-i18n.js")
  foreach ($slug in $slugs) { $paths += "/l/$slug.html" }
  foreach ($path in @("LceWeb.Api.dll","diagnostics/$diagnosticId.v1.json","diagnostics/$diagnosticId.v2.json","wwwroot/index.html","wwwroot/diagnostico.html","wwwroot/assets/clarity-bundle.js")) {
    if (-not (Test-Path (Join-Path $publish $path))) { throw "Missing package file: $path" }
  }
  foreach ($path in $paths | Where-Object { $_ -notin @("/","/health") }) {
    if (-not (Test-Path (Join-Path $webroot $path.TrimStart('/')))) { throw "Missing public file: $path" }
  }
  $v2 = Get-Content $definition -Raw | ConvertFrom-Json -Depth 100
  if ($v2.id -ne $diagnosticId -or $v2.version -ne 2 -or $v2.isActive -ne $true -or @($v2.steps).Count -ne 5) { throw "Incorrect diagnostic v2." }
  $app = Az-Checked @("webapp","show","-g",$ResourceGroup,"-n",$WebApp,"-o","json") | ConvertFrom-Json
  if ($app.serverFarmId -notlike "*/lce-murus-asp" -or -not $app.httpsOnly -or $app.state -ne "Running") { throw "App target or HTTPS preflight failed." }
  $runtime = Az-Checked @("webapp","config","show","-g",$ResourceGroup,"-n",$WebApp,"--query","linuxFxVersion","-o","tsv")
  if ($runtime -ne "DOTNETCORE|10.0") { throw "Unexpected runtime." }
  if ($origin.Host -notin @($app.hostNames)) { throw "Approved domain is not bound to this app. Configure and validate DNS/TLS first; this script does not change DNS." }
  $base = "https://$WebApp.azurewebsites.net"
  $domainHealth = Invoke-RestMethod "$ApprovedOrigin/health" -TimeoutSec 20
  if ($domainHealth.status -ne "ok") { throw "Approved origin is not serving LCE health." }
  $settings = Az-Checked @("webapp","config","appsettings","list","-g",$ResourceGroup,"-n",$WebApp,"--query","[?name=='Diagnostics__Storage__Provider' || name=='Diagnostics__Storage__TableEndpoint' || name=='Diagnostics__Storage__TableName' || name=='Admin__Enabled' || name=='Attachments__PublicUploadEnabled'].{name:name,value:value}","-o","json") | ConvertFrom-Json
  $settingsMap = @{}
  foreach ($setting in $settings) { $settingsMap[[string]$setting.name] = [string]$setting.value }
  if ($settingsMap["Diagnostics__Storage__Provider"] -ne "AzureTable") { throw "Expected AzureTable diagnostic storage." }
  if ($settingsMap["Diagnostics__Storage__TableEndpoint"] -ne "https://$StorageAccount.table.core.windows.net/") {
    if ($settingsMap["Diagnostics__Storage__TableEndpoint"] -ne "https://$StorageAccount.table.core.windows.net") { throw "Unexpected diagnostic table endpoint." }
  }
  $table = $settingsMap["Diagnostics__Storage__TableName"]
  if (-not $table) { $table = "DiagnosticDefinitions" }
  if ($settingsMap["Admin__Enabled"] -eq "true" -or $settingsMap["Attachments__PublicUploadEnabled"] -eq "true") {
    throw "This first-release script requires admin and attachment upload disabled until real identity/scan/privacy tests pass."
  }
  $prior = Az-Checked @("storage","entity","show","--account-name",$StorageAccount,"--table-name",$table,"--partition-key",$diagnosticId,"--row-key","v0000000001","--auth-mode","login","--query","Version","-o","tsv")
  if ($prior -ne "1") { throw "Historical definition v1 not verified." }
  $cap = Invoke-RestMethod "$base/api/public-capabilities" -TimeoutSec 20
  Write-Host "Preflight OK. Existing lead capture: $($cap.leadCaptureEnabled). No settings changed."
  Write-Host "Artifact SHA256: $actual"
  Write-Host "Target: $ApprovedOrigin; editorial approval: $ApproveEditorial"
  if (-not $Apply) { Write-Host "READ ONLY. No deployment or table writes."; return }
  if (-not $ApproveEditorial) { throw "Obtain LCE editorial approval before deploying the new public pages; recheck DOF and service scope." }
  # Final editorial transform: exact markers only; verify output and retain the resulting hash.
  foreach ($slug in $slugs) {
    $path = Join-Path $webroot "l/$slug.html"
    $html = Get-Content -LiteralPath $path -Raw
    if (-not $html.Contains('<meta name="robots" content="noindex,follow">')) { throw "Editorial marker missing: $slug" }
    $html.Replace('<meta name="robots" content="noindex,follow">','<meta name="robots" content="index,follow">') | Set-Content -LiteralPath $path -Encoding utf8NoBOM
  }
  $sitemap = Join-Path $webroot "sitemap.xml"
  $xml = Get-Content $sitemap -Raw
  $extra = ($slugs | ForEach-Object { "  <url><loc>$defaultOrigin/l/$_.html</loc></url>" }) -join "`n"
  $xml.Replace("</urlset>", "$extra`n</urlset>") | Set-Content $sitemap -Encoding utf8NoBOM
  foreach ($file in Get-ChildItem $webroot -Recurse -File | Where-Object { $_.Extension -in @(".html",".xml",".txt") }) {
    $content = Get-Content $file.FullName -Raw
    if ($content.Contains($defaultOrigin)) { $content.Replace($defaultOrigin,$ApprovedOrigin) | Set-Content $file.FullName -Encoding utf8NoBOM }
  }
  $parsedSitemap = [xml](Get-Content $sitemap -Raw)
  if (@($parsedSitemap.urlset.url).Count -ne 12) { throw "Sitemap transformation failed." }
  $release = Join-Path $work "lceweb-release.zip"
  [IO.Compression.ZipFile]::CreateFromDirectory($publish,$release,[IO.Compression.CompressionLevel]::Optimal,$false)
  Write-Host "Final release SHA256: $((Get-FileHash $release -Algorithm SHA256).Hash)"
  # First deploy code compatible with both definitions. Never expose v2 weights through I15.
  Az-Checked @("webapp","deploy","-g",$ResourceGroup,"-n",$WebApp,"--src-path",$release,"--type","zip","-o","none") | Out-Null
  $ready = $false
  for ($attempt=1;$attempt -le 12;$attempt++) {
    try {
      $current = Invoke-RestMethod "$base/api/public-capabilities" -TimeoutSec 20
      $old = Invoke-RestMethod "$base/api/diagnostics/${diagnosticId}?version=1" -TimeoutSec 20
      $module = Invoke-WebRequest "$base/js/diagnostics/commercial-i18n.js" -TimeoutSec 20
      if ($current.PSObject.Properties.Name -contains "attachmentsEnabled" -and $old.version -eq 1 -and $module.StatusCode -eq 200) { $ready = $true; break }
    } catch { Write-Host "Waiting for new compatible code: attempt $attempt" }
    Start-Sleep -Seconds 5
  }
  if (-not $ready) { throw "New binary not verified. No diagnostic v2 seed performed." }
  # Insert v2 only; never replace or delete historical rows. Existing v2 must match.
  $canonicalJson = $v2 | ConvertTo-Json -Depth 100 -Compress
  if ([Text.Encoding]::Unicode.GetByteCount($canonicalJson) -gt 61440) { throw "Definition exceeds Table string budget." }
  $existing = Az-Checked @("storage","entity","query","--account-name",$StorageAccount,"--table-name",$table,"--filter","PartitionKey eq '$diagnosticId' and RowKey eq 'v0000000002'","--auth-mode","login","-o","json") | ConvertFrom-Json
  if (@($existing.items).Count -eq 0) {
    Az-Checked @("storage","entity","insert","--account-name",$StorageAccount,"--table-name",$table,"--auth-mode","login","--if-exists","fail","--entity","PartitionKey=$diagnosticId","RowKey=v0000000002","Version=2","Version@odata.type=Edm.Int32","IsActive=true","IsActive@odata.type=Edm.Boolean","Name=$($v2.name)","DefinitionJson=$canonicalJson","-o","none") | Out-Null
  } else {
    $previous = $existing.items[0].DefinitionJson | ConvertFrom-Json -Depth 100 | ConvertTo-Json -Depth 100 -Compress
    if ($previous -ne $canonicalJson) { throw "Existing v2 differs; do not overwrite. Investigate." }
  }
  foreach ($path in $paths) {
    $response = Invoke-WebRequest "$ApprovedOrigin$path" -TimeoutSec 25
    if ([int]$response.StatusCode -ne 200) { throw "Public smoke failed: $path" }
  }
  $public = Invoke-RestMethod "$ApprovedOrigin/api/diagnostics/$diagnosticId" -TimeoutSec 25
  if ($public.version -ne 2 -or @($public.steps).Count -ne 5 -or @($public.dimensions).Count -ne 0 -or @($public.results).Count -ne 0) { throw "Public v2 projection failed." }
  foreach ($step in $public.steps) { foreach ($question in $step.questions) { foreach ($option in $question.options) {
    if (($option.PSObject.Properties.Name -contains "scores") -and $option.scores -and @($option.scores.PSObject.Properties).Count -gt 0) { throw "Internal weights exposed." }
  }}}
  $finalCap = Invoke-RestMethod "$ApprovedOrigin/api/public-capabilities" -TimeoutSec 20
  if ($finalCap.attachmentsEnabled -ne $false -or $finalCap.leadCaptureEnabled -ne $cap.leadCaptureEnabled) { throw "Unexpected capabilities after release." }
  Write-Host "Release HTTP and v2 QA passed. No public test lead created. Admin/upload remain disabled. Perform authorized browser and email test before declaring production acceptance."
} finally {
  if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
