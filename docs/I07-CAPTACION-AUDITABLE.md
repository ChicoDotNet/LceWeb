# I07 — Registro comercial auditable y activación segura

**Entorno:** lce-glt, .NET 10, mismo App Service B1 lce-murus-asp, grupo lce-murus-common-rg, almacenamiento lcemuruscommonst / Azure Tables. El diagnóstico v1 ya está sembrado y publicado. Microsoft Clarity usa npm y consentimiento opcional separado de la autorización comercial.

## Estado de partida verificado

- GET /health: 200.
- GET /api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425: versión 1 activa, leída desde LceDiagnosticDefinitions.
- GET /api/public-capabilities: leadCaptureEnabled=false.
- La tabla LceLeadSubmissions existe y la identidad administrada de lce-glt tiene Storage Table Data Contributor sobre las dos tablas LCE.
- GET /privacidad.html es informativo: **no es todavía un aviso de privacidad integral validado**.

## Código I07

1. El POST /api/leads exige privacyAccepted=true y privacyNoticeVersion idéntica a la revisión aprobada configurada por el operador.
2. El servidor genera PrivacyAcceptedUtc; nunca confía en una fecha enviada por el navegador.
3. La entidad persistida incluye PrivacyNoticeVersion y PrivacyAcceptedUtc en columnas consultables y en SubmissionJson.
4. El frontend lee privacyNoticeVersion de GET /api/public-capabilities y sólo ofrece el formulario si el backend también informa leadCaptureEnabled=true.
5. La casilla de autorización expresa identifica el aviso vigente. **No solicita autorización para marketing ni publicidad; solo seguimiento del contacto.**
6. Clarity permanece independiente: se carga únicamente tras consentimiento analítico; los campos del diagnóstico y contacto usan clarity-mask.
7. El API en producción sigue **cerrado por defecto**. Para activar requiere todo: AzureTable + Leads__PublicCaptureEnabled=true + Leads__PrivacyNoticeApproved=true + Leads__PrivacyNoticeVersion no vacío.
8. El desarrollo/CI usa privacyNoticeVersion=development-test y prueba consentimiento rechazado, revisión obsoleta y petición válida.

## Pendiente legal imprescindible antes de activar

El área legal de LCE debe aprobar y publicar un aviso integral con información real:

- Nombre o razón social exacta de la entidad responsable y domicilio;
- Identidad y contacto del área para privacidad, procedimiento ARCO;
- Categorías precisas de datos recogidos y finalidades primarias/ secundarias;
- Destinatarios o transferencias y proveedores que participen en el tratamiento (incluidas consideraciones sobre Clarity, si corresponden);
- Mecanismos de limitación, revocación, medios de ejercicio de derechos;
- Plazos y criterios de conservación/eliminación, cambios al aviso;
- Versión documental y fecha de vigencia visibles;
- Confirmación de protección adecuada de grabaciones de sesión, datos de contacto y preguntas.

El borrador se encuentra en docs/I07-AVISO-PRIVACIDAD-BORRADOR.md; **no es texto publicable** sin personalizar ni validar. El sitio no debe afirmar que el aviso está aprobado si lo anterior no está documentado.

Para permitir activación, la futura página /privacidad.html aprobada debe contener el título 'Aviso de privacidad integral' y un atributo HTML data-lce-privacy-revision="YYYY-MM-DD-v1", donde el valor coincida EXACTAMENTE con Leads__PrivacyNoticeVersion. No usar el marcador hasta la aprobación. Este marcador es una verificación técnica, no una certificación legal.

## Preflight Azure Cloud Shell (SOLO lectura)

Antes o después de desplegar I07:

    $base="https://lce-glt.azurewebsites.net"
    Invoke-RestMethod "$base/health"
    Invoke-RestMethod "$base/api/public-capabilities"
    Invoke-RestMethod "$base/api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425" | Select-Object name,version,isActive
    az storage entity show --account-name lcemuruscommonst --table-name LceDiagnosticDefinitions --partition-key 16f5812b-6a27-44f6-b5b4-557a720a6425 --row-key v0000000001 --auth-mode login --query "{Version:Version,Activo:IsActive}" -o json

Public-capabilities esperado mientras no exista aviso integral aprobado: `{"leadCaptureEnabled":false}`. Según la configuración del serializador, `privacyNoticeVersion` puede omitirse cuando es nulo.

Puedes comprobar que POST /api/leads sigue cerrado, usando exclusivamente datos ficticios y sin guardar leads:

    $payload = '{"diagnosticId":"16f5812b-6a27-44f6-b5b4-557a720a6425","definitionVersion":1,"privacyAccepted":false,"privacyNoticeVersion":"bad-version","contact":{"name":"Prueba LCE","email":"prueba@example.com"},"answers":[]}'
    $r = Invoke-WebRequest -Method POST -Uri "$base/api/leads" -ContentType "application/json" -Body $payload -SkipHttpErrorCheck
    [int]$r.StatusCode

Resultado esperado: **503**. Si el endpoint devuelve 201 en producción antes de la aprobación, desactivar PublicCaptureEnabled y PrivacyNoticeApproved inmediatamente.

## Activación futura (NO ejecutar hasta publicar el aviso integral)

El script de esta iteración valida que el aviso publicado NO contiene el texto provisional, contiene un marcador de versión exacto y está identificado como aviso integral. Requiere confirmación del operador:

    ./scripts/azure/i07-activate-leads.ps1

Modo previsualización por defecto: sin cambios. Solo después de validación jurídica y nueva publicación del aviso:

    ./scripts/azure/i07-activate-leads.ps1 -Apply -ConfirmLegalApproval -ApprovedNoticeVersion "YYYY-MM-DD-v1"

La activación comprueba el endpoint y revierte PublicCaptureEnabled/PrivacyNoticeApproved si la prueba falla. No intenta almacenar correos electrónicos de prueba ni imprimir credenciales.

## Aceptación I07

- CI .NET 10 y JavaScript verde.
- Paquete ZIP de I07 generado desde su propio commit y publicado en el mismo lce-glt.
- Las rutas homepage, diagnóstico, privacidad y Clarity mantienen HTTP 200.
- GET /api/public-capabilities devuelve false/null y POST /api/leads devuelve 503.
- La definición sigue en Azure Tables.
- Contenido legal pendiente identificado; la futura activación queda controlada y reversible.

## Posibles mejoras posteriores

- Validación y publicación del aviso integral, aprobación explícita, activación y prueba sintética real de punta a punta.
- ACS Email y confirmación de alertas; no se debe afirmar entrega de email si Email__Provider=None.
- Captación con consentimiento separado si LCE desea publicidad directa o comunicaciones promocionales.
