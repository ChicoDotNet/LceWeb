# I30 · multilingual release. Preflight only unless -Apply. No Azure Table writes or App Settings changes.
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ArtifactPath,
  [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedPackageSha256,
  [string]$SubscriptionId = "cde3bbad-ead8-41d9-bbed-cbd64987f008",
  [string]$ResourceGroup = "lce-murus-common-rg",
  [string]$WebApp = "lce-glt",
  [string]$ApprovedOrigin = "https://lce-glt.azurewebsites.net",
  [switch]$ApproveTranslations,
  [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$origin = [Uri]$ApprovedOrigin
if (-not $origin.IsAbsoluteUri -or $origin.Scheme -ne "https" -or $origin.AbsolutePath -ne "/" -or $origin.Query -or $origin.Fragment -or $origin.UserInfo -or -not $origin.IsDefaultPort) { throw "ApprovedOrigin must be a bare HTTPS origin." }
$ApprovedOrigin = $origin.GetLeftPart([UriPartial]::Authority)
$defaultOrigin = "https://lce-glt.azurewebsites.net"
$id = "16f5812b-6a27-44f6-b5b4-557a720a6425"
$langs = @("es","en","fr","pt")
$routes = @("index.html","diagnostico.html","privacidad.html","servicios/planeacion.html","servicios/logistica.html","servicios/normas-nom.html","l/costos-importacion.html","l/cumplimiento-nom.html","l/diagnostico-logistico.html","l/manifestacion-de-valor.html","l/proveedores-confiables.html","l/compliance-aduanero.html")
$editorial = @("l/manifestacion-de-valor.html","l/proveedores-confiables.html","l/compliance-aduanero.html")
function Az-Checked {
  param([string[]]$Arguments)
  $result = & az @Arguments --subscription $SubscriptionId --only-show-errors
  if ($LASTEXITCODE -ne 0) { throw "Azure CLI step failed: $($Arguments[0..1] -join ' ')" }
  return $result
}
function Test-Diagnostic {
  $old = Invoke-RestMethod "$ApprovedOrigin/api/diagnostics/${id}?version=1" -TimeoutSec 25
  $current = Invoke-RestMethod "$ApprovedOrigin/api/diagnostics/$id" -TimeoutSec 25
  if ($old.version -ne 1 -or $current.version -ne 2 -or @($current.steps).Count -ne 5 -or @($current.dimensions).Count -ne 0 -or @($current.results).Count -ne 0) { throw "Expected compatible public v1/v2 contract." }
  foreach ($step in $current.steps) { foreach ($question in $step.questions) { foreach ($option in $question.options) {
    if (($option.PSObject.Properties.Name -contains "scores") -and $option.scores -and @($option.scores.PSObject.Properties).Count -gt 0) { throw "Internal score weights exposed." }
  }}}
}
function Get-Route {
  param([string]$Language,[string]$Page)
  $prefix = if ($Language -eq "es") { "" } else { "/$Language" }
  if ($Page -eq "index.html") { return "$prefix/" }
  return "$prefix/$Page"
}
$work = Join-Path ([IO.Path]::GetTempPath()) ("lce-i30-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
  Expand-Archive -LiteralPath $ArtifactPath -DestinationPath (Join-Path $work "outer")
  $zip = Join-Path $work "outer/lceweb.zip"
  if (-not (Test-Path $zip)) { throw "Expected GitHub Actions artifact containing lceweb.zip." }
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
  if ($hash -ne $ExpectedPackageSha256) { throw "Package SHA256 mismatch. Do not deploy." }
  $publish = Join-Path $work "publish"
  Expand-Archive -LiteralPath $zip -DestinationPath $publish
  $webroot = Join-Path $publish "wwwroot"
  foreach ($asset in @("LceWeb.Api.dll","wwwroot/assets/site-locale.js","wwwroot/assets/site-locale.css","wwwroot/assets/clarity-bundle.js","diagnostics/$id.v1.json","diagnostics/$id.v2.json")) {
    if (-not (Test-Path (Join-Path $publish $asset))) { throw "Missing package file: $asset" }
  }
  foreach ($lang in $langs) { foreach ($page in $routes) {
    $relative = if ($lang -eq "es") { $page } else { "$lang/$page" }
    $path = Join-Path $webroot $relative
    if (-not (Test-Path $path)) { throw "Missing locale: $relative" }
    $html = Get-Content -LiteralPath $path -Raw
    $langTag = if ($lang -eq "es") { "es-MX" } else { $lang }
    if (-not $html.Contains(('<html lang="{0}"' -f $langTag)) -or -not $html.Contains('data-lce-locale="pt"')) { throw "Invalid language markup: $relative" }
    if ($lang -ne "es") {
      $marker = if ($page -eq "privacidad.html") { 'data-lce-translation="reference"' } else { 'data-lce-translation="editorial"' }
      if (-not $html.Contains($marker) -or -not $html.Contains('content="noindex,follow"')) { throw "Missing translation release gate: $relative" }
    }
  }}
  $app = Az-Checked @("webapp","show","-g",$ResourceGroup,"-n",$WebApp,"-o","json") | ConvertFrom-Json
  if ($app.serverFarmId -notlike "*/lce-murus-asp" -or -not $app.httpsOnly -or $app.state -ne "Running" -or $origin.Host -notin @($app.hostNames)) { throw "App target/HTTPS/domain preflight failed." }
  $runtime = Az-Checked @("webapp","config","show","-g",$ResourceGroup,"-n",$WebApp,"--query","linuxFxVersion","-o","tsv")
  if ($runtime -ne "DOTNETCORE|10.0") { throw "Unexpected runtime." }
  $health = Invoke-RestMethod "$ApprovedOrigin/health" -TimeoutSec 20
  if ($health.status -ne "ok") { throw "Existing health check failed." }
  Test-Diagnostic
  $before = Invoke-RestMethod "$ApprovedOrigin/api/public-capabilities" -TimeoutSec 20
  # Spanish editorial was approved in I25. Confirm the live pages are already indexable before preserving that state.
  foreach ($page in $editorial) {
    $live = Invoke-WebRequest "$ApprovedOrigin/$page" -TimeoutSec 20
    if ($live.StatusCode -ne 200 -or $live.Content -match '<meta\s+name="robots"\s+content="noindex') { throw "Spanish editorial approval not confirmed on live site: $page" }
  }
  Write-Host "Preflight OK: 48 HTML pages, SHA256 $hash, historical v1 and current v2 intact."
  Write-Host "Lead capture: $($before.leadCaptureEnabled); attachments: $($before.attachmentsEnabled); translated SEO approved: $ApproveTranslations"
  if (-not $Apply) { Write-Host "READ ONLY. No deployment, table writes or settings changes."; return }
  # Preserve I25 Spanish SEO release; foreign-language copies remain noindex until separately approved.
  foreach ($page in $editorial) {
    $path = Join-Path $webroot $page
    $html = Get-Content -LiteralPath $path -Raw
    if (-not $html.Contains('<meta name="robots" content="noindex,follow">')) { throw "Spanish editorial marker missing: $page" }
    $html.Replace('<meta name="robots" content="noindex,follow">','<meta name="robots" content="index,follow">') | Set-Content -LiteralPath $path -Encoding utf8NoBOM
  }
  $sitemapUrls = [System.Collections.Generic.List[string]]::new()
  foreach ($lang in $langs) { foreach ($page in $routes) {
    if ($lang -eq "es") { $sitemapUrls.Add("$ApprovedOrigin$(Get-Route $lang $page)") }
    elseif ($ApproveTranslations -and $page -ne "privacidad.html") {
      $path = Join-Path $webroot "$lang/$page"
      $html = Get-Content -LiteralPath $path -Raw
      $html.Replace('<meta name="robots" content="noindex,follow" data-lce-translation="editorial">','<meta name="robots" content="index,follow" data-lce-translation="editorial">') | Set-Content -LiteralPath $path -Encoding utf8NoBOM
      $sitemapUrls.Add("$ApprovedOrigin$(Get-Route $lang $page)")
    }
  }}
  foreach ($file in Get-ChildItem $webroot -Recurse -File -Filter "*.html") {
    $html = Get-Content -LiteralPath $file.FullName -Raw
    if ($html.Contains($defaultOrigin) -and $ApprovedOrigin -ne $defaultOrigin) { $html.Replace($defaultOrigin,$ApprovedOrigin) | Set-Content -LiteralPath $file.FullName -Encoding utf8NoBOM }
  }
  $sitemap = '<?xml version="1.0" encoding="UTF-8"?>' + "`n<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>`n" + (($sitemapUrls | ForEach-Object { "  <url><loc>$_</loc></url>" }) -join "`n") + "`n</urlset>`n"
  $parsed = [xml]$sitemap
  $expectedUrls = if ($ApproveTranslations) { 45 } else { 12 }
  if (@($parsed.urlset.url).Count -ne $expectedUrls) { throw "Sitemap generation failed." }
  $sitemap | Set-Content -LiteralPath (Join-Path $webroot "sitemap.xml") -Encoding utf8NoBOM
  "User-agent: *`nDisallow: /api/`nDisallow: /admin/`nSitemap: $ApprovedOrigin/sitemap.xml" | Set-Content -LiteralPath (Join-Path $webroot "robots.txt") -Encoding utf8NoBOM
  $release = Join-Path $work "lceweb-i30-release.zip"
  [IO.Compression.ZipFile]::CreateFromDirectory($publish,$release,[IO.Compression.CompressionLevel]::Optimal,$false)
  Write-Host "Release SHA256: $((Get-FileHash $release -Algorithm SHA256).Hash)"
  Az-Checked @("webapp","deploy","-g",$ResourceGroup,"-n",$WebApp,"--src-path",$release,"--type","zip","-o","none") | Out-Null
  $ready = $false
  for ($attempt=1;$attempt -le 12;$attempt++) {
    try {
      $h = Invoke-WebRequest "$ApprovedOrigin/en/" -TimeoutSec 15
      if ($h.StatusCode -eq 200 -and $h.Content.Contains('<html lang="en"')) { $ready = $true; break }
    } catch { Write-Host "Waiting for localized release: $attempt" }
    Start-Sleep -Seconds 5
  }
  if (-not $ready) { throw "Localized binary not verified. Investigate before redeploying." }
  $count = 0
  foreach ($lang in $langs) { foreach ($page in $routes) {
    $route = Get-Route $lang $page
    $response = Invoke-WebRequest "$ApprovedOrigin$route" -TimeoutSec 25
    $langTag = if ($lang -eq "es") { "es-MX" } else { $lang }
    if ($response.StatusCode -ne 200 -or -not $response.Content.Contains(('<html lang="{0}"' -f $langTag))) { throw "Locale smoke test failed: $route" }
    $count++
    Write-Host "200 $route"
  }}
  Test-Diagnostic
  $after = Invoke-RestMethod "$ApprovedOrigin/api/public-capabilities" -TimeoutSec 20
  foreach ($key in @("leadCaptureEnabled","attachmentsEnabled","privacyNoticeVersion")) {
    if ($before.$key -ne $after.$key) { throw "Unexpected public capability change: $key" }
  }
  $liveSitemap = [xml](Invoke-WebRequest "$ApprovedOrigin/sitemap.xml" -TimeoutSec 20).Content
  if (@($liveSitemap.urlset.url).Count -ne $expectedUrls) { throw "Live sitemap count mismatch." }
  Write-Host "I30 RELEASE OK: $count/48 localized routes, v1/v2 intact, capabilities unchanged, $expectedUrls sitemap URLs. No lead created; no table writes or App Settings changes."
} finally {
  if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
