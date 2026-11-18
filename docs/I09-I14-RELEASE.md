# LCE — Entrega I09–I14: paridad comercial, torre de control y tres landings

## Decisión de arquitectura

El archivo entregado por LCE `lce-web.zip` contiene una Home "Current Song" y tres prototipos con mismo tema visual "Executive Control Tower" pero distintas canciones: Hidden Leverage, Proof First y Diagnostic First.

Este incremento conserva la Home como **centro institucional** (quiénes somos, servicios, experiencia, metodología, prueba social, contacto), y utiliza las tres canciones como **landings independientes para campañas de micronicho**. Las landings enlazan a Home y al diagnóstico compartido, nunca entre sí; Home no promueve ni enlaza las landings experimentales.

| Entrega | Tipo | Ruta | Hipótesis |
|---|---|---|---|
| I09 | prueba social | `/#clientes` | Recuperar argumentos publicados de confianza: Dometic, AM Denmark, RICOM |
| I10 | servicios | `/servicios/planeacion.html`, `/servicios/logistica.html`, `/servicios/normas-nom.html` | Navegar y comprender las actividades, no salir por mailto |
| I11 | Home institucional | `/` | Mantener canción corporativa, recuperar estilo Executive Control Tower, compromisos y especialidades |
| I12 | Landing A | `/l/costos-importacion.html` | Hidden Leverage: costo total y planeación |
| I13 | Landing B | `/l/cumplimiento-nom.html` | Proof First: NOM, vigencias y certificación |
| I14 | Landing C | `/l/diagnostico-logistico.html` | Diagnostic First: orientación y coordinación operativa |

**Diferencia deliberada respecto al ZIP:** las tres canciones originales compartían buyer persona y comunicaban principalmente la operación completa. Por instrucción comercial posterior de LCE, ahora cada una tiene micronicho explícito, manteniendo el orden narrativo de su canción y el mismo look & feel.

## Rutas de CTA / 12 condiciones

Cada landing soporta los cuatro tratamientos usando `?contact=`:

| Variante | URL de ejemplo | Destino |
|---|---|---|
| 1 — Call | `/l/costos-importacion.html?contact=call` | `tel:+525590476846` |
| 2 — WhatsApp | `/l/costos-importacion.html?contact=whatsapp` | `wa.me/525590476846` |
| 3 — Email | `/l/costos-importacion.html?contact=email` | `mailto:asanchez@lce.com.mx` |
| 4 — Form | `/l/costos-importacion.html?contact=form` | `/diagnostico.html` con UTM |

Cambiar la ruta de landing por `cumplimiento-nom.html` (B) o `diagnostico-logistico.html` (C) genera las doce condiciones A1–C4. El contacto por WhatsApp usa el teléfono corporativo publicado; **LCE debe comprobar que ese número tiene WhatsApp antes de invertir en campañas B2/A2/C2**. Las variantes son experimentos de CTAs, no doce páginas distintas ni doce anuncios reales.

En C4, el formulario ocurre en la landing junto al diagnóstico real. En A4/B4, lleva al diagnóstico compartido con UTM de campaña. No se inventó un endpoint paralelo; los leads siguen `POST /api/leads`.

## Prueba social y activos gráficos

- Dometic MX — Patrik Mattson.
- AM Denmark — Aldo Morante.
- RICOM — Itzell Escorcia.

Las frases en la nueva Web son **síntesis atribuidas** de testimonios publicados en `https://lce.com.mx/`, no citas literales; se usan logotipos publicados en WordPress por LCE mediante URLs absolutas a `https://lce.com.mx/wp-content/uploads/2022/06/`. **Pendiente no bloqueante:** obtener y almacenar copias locales de los cuatro activos con autorización de LCE para eliminar la dependencia de hotlinking del sitio legado. Se conservaron alt y dimensiones explícitas.

## Referencia visual y contenido

El estilo se adaptó de los archivos `home/theme.json`, `landing-{1,2,3}/theme.json` y las maquetas incluidas en `lce-web.zip`: navy ejecutivo, azul operacional, verde de cumplimiento, líneas de control, etiquetas monoespaciadas y movimiento mínimo.

La Home conserva la promesa vigente y recupera el mapa de 8 compromisos y 3 áreas de especialidad; las tres páginas de servicios recuperan actividades publicadas en `https://lce.com.mx/index.php/servicios-es/`.

## Seguridad, medición y privacidad

- Las cuatro páginas principales y las landings enlazan al aviso de privacidad aprobado `/privacidad.html`.
- Microsoft Clarity se carga a través del paquete npm ya instalado **solo después del consentimiento de analíticas**. El navegador no envía datos personales a eventos adicionales. Los formularios/diagnósticos están marcados para enmascaramiento.
- El diagnóstico de C usa el GUID existente `16f5812b-6a27-44f6-b5b4-557a720a6425` versión v1. Las cuatro dimensiones del mock original eran **conceptuales**; el backend actual calcula solo las dimensiones definidas en v1 y NO promete un mapa regulatorio certificado.
- El formulario solo aparece si `GET /api/public-capabilities` indica `leadCaptureEnabled=true` junto con versión de privacidad.
- I09–I14 **no cambian App Settings** ni reactivan/desactivan leads. El estado de captación en Azure debe comprobarse directamente; el CI local no sustituye una prueba de producción.
- Los eventos opcionales de Clarity registran campaña + variante de CTA, sin nombre, correo ni respuesta.

## Aceptación y operación

**Automatizado en CI:** Node tests de paridad comercial y aislamiento, pruebas .NET, compilación y empaquetado. No equivale a QA visual en navegadores.

**Pendiente del operador para cierre en Azure:** inspección de diseño móvil/escritorio, revisión comercial de copy y testimonios, confirmación del canal WhatsApp, despliegue y prueba de leads sintéticos según script.

Despliegue: `scripts/azure/i14-deploy.ps1` (Azure Cloud Shell, PowerShell).

1. Descargar del workflow **LCE package - .NET 10**, rama `features/i14-diagnostic-funnel-qa`, artefacto `lceweb-package`.
2. Subir `lceweb-package.zip` a Cloud Shell (directorio HOME).
3. Ejecutar `./scripts/azure/i14-deploy.ps1` desde el directorio del repositorio, o copiar el script a Cloud Shell.
4. Solo si se desea reactivar el registro aprobado y hacer prueba: `./scripts/azure/i14-deploy.ps1 -EnableLeads -TestLead`.
5. El script no ejecuta rollback de App Settings. Si una prueba falla se necesita diagnóstico; no declarar activado el registro sin respuesta real del API.

**No se han fusionado PRs ni ejecutado despliegues en Azure durante I09–I14.**
