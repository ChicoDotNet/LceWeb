# I02 — Despliegue al App Service existente (LCE)

Destino validado por el operador el 13-nov-2026:
- Suscripción: cde3bbad-ead8-41d9-bbed-cbd64987f008
- RG: lce-murus-common-rg
- App: lce-glt, Linux, México Central, en plan lce-murus-asp (B1)
- Runtime anterior: NODE|24-lts. Antes de la publicación se veía Azure Welcome.
- No crear otro App Service ni modificar el plan compartido.

## Artefacto

El workflow .github/workflows/lce-package.yml publica el backend ASP.NET Core 10, la landing comercial y los recursos estáticos al ZIP interno lceweb.zip. Debe terminar exitosamente antes de publicar. Descarga el artefacto lceweb-package desde Actions > LCE package - .NET 10 para la rama features/i02-lce-commercial-home.

El archivo descargado de Actions es un ZIP externo que contiene el ZIP interno lceweb.zip. Usa el botón Upload/Download de Azure Cloud Shell para subirlo a PowerShell y extraerlo:

    Expand-Archive -Path ./lceweb-package.zip -DestinationPath ./lce-package -Force

## Publicación manual desde Azure Cloud Shell PowerShell

ATENCIÓN: estos comandos cambian el stack y contenido de la aplicación lce-glt. Ejecutar sólo después de descargar el artifact y comprobar que el workflow esté verde. No requieren clonar GitHub.

    $sub = "cde3bbad-ead8-41d9-bbed-cbd64987f008"
    $rg = "lce-murus-common-rg"
    $app = "lce-glt"
    $zip = "./lce-package/lceweb.zip"

    az account set --subscription $sub
    if ($LASTEXITCODE -ne 0) { throw "Suscripcion no disponible" }

    $web = az webapp show -g $rg -n $app -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Web App no accesible" }
    if ($web.serverFarmId -notlike "*/lce-murus-asp") { throw "Plan de App Service inesperado" }
    if (-not (Test-Path $zip)) { throw "ZIP no encontrado: $zip" }

    $stackAnterior = az webapp config show -g $rg -n $app --query linuxFxVersion -o tsv
    Write-Host "Runtime anterior: $stackAnterior"

    az webapp config set -g $rg -n $app --linux-fx-version 'DOTNETCORE|10.0' --always-on true -o none
    if ($LASTEXITCODE -ne 0) { throw "Error configurando .NET 10" }

    az webapp update -g $rg -n $app --https-only true -o none
    if ($LASTEXITCODE -ne 0) { throw "Error habilitando HTTPS obligatorio" }

    az webapp deploy -g $rg -n $app --src-path $zip --type zip
    if ($LASTEXITCODE -ne 0) { throw "Error en despliegue ZIP" }

    foreach ($p in @("/", "/health", "/assets/site.css", "/assets/site.js", "/diagnostic-demo.html")) {
      try {
        $r = Invoke-WebRequest "https://$app.azurewebsites.net$p" -TimeoutSec 20 -SkipHttpErrorCheck
        Write-Host "$p -> HTTP $([int]$r.StatusCode)"
      } catch { Write-Warning "$p -> $($_.Exception.Message)" }
    }

## Condiciones de terminado

- / responde 200 y título LCE, no Azure Welcome.
- /health responde 200 y status ok.
- Los recursos CSS/JS/SVG responden 200.
- /diagnostic-demo.html responde 200 como recorrido técnico, no enlazado desde la homepage.
- HTTPS-only está activo. Stack DOTNETCORE|10.0.
- No se alteraron otros App Services ni el plan B1 compartido.

## Precaución comercial

La landing de I02 sólo tiene CTAs a correo y teléfono. No incluye un formulario simulado. La API de leads aún puede estar en modo memoria si Azure Tables no está configurado: no captar datos fiscales/personales reales por el diagnóstico técnico antes de I05–I06, persistencia y aviso de privacidad.
