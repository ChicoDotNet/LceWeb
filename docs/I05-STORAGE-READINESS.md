# I05 — Preparación de Azure Tables para lce-glt

## Estado heredado (13-nov-2026)

El sitio https://lce-glt.azurewebsites.net/ sirve la landing y el diagnóstico público en el mismo App Service Linux .NET 10, plan lce-murus-asp B1, grupo lce-murus-common-rg, suscripción cde3bbad-ead8-41d9-bbed-cbd64987f008.

La aplicación expone GET /api/public-capabilities -> {"leadCaptureEnabled":false} y POST /api/leads rechaza captación pública mientras no se configure Azure Tables y el operador active el flag explícitamente.

El propósito de I05 es dejar la infraestructura lista SIN alterar la persistencia activa, el runtime, el código publicado ni activar captación pública. I06 cambia la configuración y habilita la captación después de validar aviso de privacidad y pruebas.

## 1. Inventario Cloud Shell PowerShell (SOLO lectura)

    $sub = "cde3bbad-ead8-41d9-bbed-cbd64987f008"
    $rg = "lce-murus-common-rg"
    $app = "lce-glt"
    az account set --subscription $sub
    az storage account list -g $rg --query "[].{Nombre:name,Tipo:kind,SKU:sku.name,Region:location}" -o table
    az webapp identity show -g $rg -n $app --query "{Tipo:type,PrincipalId:principalId}" -o json
    az webapp config appsettings list -g $rg -n $app --query "[].name" -o tsv

**No mostrar valores de App Settings, claves de Storage, cadenas de conexión, JWT ni e.firma.**

## 2. Selección de cuenta

**Inventario confirmado:** existe `lcemuruscommonst` (`StorageV2`, `Standard_LRS`, `mexicocentral`). Reutilizarla sin crear nuevas cuentas. Se crearán únicamente las tablas `LceDiagnosticDefinitions` y `LceLeadSubmissions` para evitar colisiones con otras aplicaciones.

**Seguridad:** asignar `Storage Table Data Contributor` exclusivamente a `LceDiagnosticDefinitions` y `LceLeadSubmissions`, no al nivel de la cuenta. Los permisos por tabla se aplican a Managed Identity y al operador que ejecuta el seed. Entra ID sustituye el uso de claves de cuenta.

## 3. Script reproducible en repositorio

En una terminal con checkout de features/i05-storage-readiness:

Vista previa de cuenta existente:

    pwsh ./scripts/azure/prepare-lce-storage.ps1

Aplicar para cuenta existente:

    pwsh ./scripts/azure/prepare-lce-storage.ps1 -Apply

No se crearán nuevas cuentas Storage; la opción -CreateStorageIfMissing se retiró para este destino compartido.

El script con -Apply:
- verifica que lce-glt esté en lce-murus-asp;
- conserva la cuenta StorageV2 Standard_LRS existente, sin modificar sus propiedades;
- habilita identidad administrada si aún no existe;
- otorga Storage Table Data Contributor sólo al operador y a la identidad de lce-glt en el scope de cada tabla LCE;
- crea LceDiagnosticDefinitions y LceLeadSubmissions por ARM, sin claves de Storage;
- copia la definición de diagnóstico v1 pública y la siembra;
- deja app settings sin modificar: el sitio sigue con la experiencia de I03 y leadCaptureEnabled=false.

Se requiere permiso para asignar roles (p. ej. User Access Administrator/Owner) y para administrar Storage. Si el operador no tiene permiso sobre la cuenta o el firewal lo impide, el script se detiene con error; no sustituir con claves privadas en el chat.

Para ejecutar desde Cloud Shell sin clonar el repo privado, el operador puede subir únicamente prepare-lce-storage.ps1 desde GitHub o compartir resultados del inventario y recibir comandos específicos de Azure CLI.

## 4. Validaciones previas a I06

- Tabla LceDiagnosticDefinitions existe y tiene entidad PartitionKey 16f5812b-6a27-44f6-b5b4-557a720a6425, RowKey v0000000001.
- Tabla LceLeadSubmissions existe (sin leads reales de pruebas).
- Managed Identity tiene permiso Storage Table Data Contributor sólo sobre las dos tablas LCE.
- Operador puede consultar la definición con --auth-mode login.
- /health y /diagnostico.html siguen HTTP 200.
- /api/public-capabilities sigue leadCaptureEnabled=false.
- El nombre de la cuenta y endpoint de Table quedan anotados; no se imprimen claves.
- No hay cambios aún en app settings, así evitamos que un fallo de autorización o seed derribe la Web.

## 5. Siguiente fase (I06)

Con permisos y seed confirmados, asignar Diagnostics__Storage__Provider, Leads__Storage__Provider y TableEndpoint a AzureTable. Verificar el funcionamiento desde el App Service administrado, publicar el formulario de contacto aprobado y establecer un aviso de privacidad revisado. Solo después habilitar Leads__PublicCaptureEnabled=true y validar una única alta sintética con confirmación de persistencia. Email ACS puede permanecer deshabilitado si aún no está aprovisionado.
