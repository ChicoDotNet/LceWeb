import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
const root = new URL("../../src/LceWeb.Api/wwwroot/", import.meta.url);
const read = path => readFileSync(new URL(path,root),"utf8");
const pages = [
 "index.html",
 "diagnostico.html",
 "privacidad.html",
 "l/costos-importacion.html",
 "l/cumplimiento-nom.html",
 "l/diagnostico-logistico.html",
 "servicios/planeacion.html",
 "servicios/logistica.html",
 "servicios/normas-nom.html"
];
test("QA: no development, API status or experiment language remains in public copy",()=>{
 const banned = [
   /El formulario s[oó]lo aparece cuando la API/i,
   /definici[oó]n de diagn[oó]stico versionada y servida/i,
   /API real/i,
   /CTA \/ FORM/i,
   /CTA \/ (CALL|EMAIL|WHATSAPP)/i,
   /HIDDEN LEVERAGE/i,
   /PROOF FIRST/i,
   /DIAGNOSTIC FIRST/i,
   /estamos en construcci[oó]n/i,
   /sitio original de LCE/i,
   /Recuperamos el respaldo/i,
   /Conservamos los compromisos publicados/i,
   /Estas actividades reflejan la descripci[oó]n de los servicios publicados/i
 ];
 for (const page of pages){
   const source = read(page);
   for (const pattern of banned) assert.doesNotMatch(source,pattern,page+" leaks "+pattern);
 }
 const js = read("assets/landing-experiments.js");
 assert.doesNotMatch(js,/e\.textContent="CTA/);
 assert.doesNotMatch(js,/Campaña: "/);
});
test("QA: corporate diagnostic retains exact runtime selectors and hides contact data from Clarity",()=>{
 const d = read("diagnostico.html");
 for(const required of [
   'id="lce-diagnostic"',
   'id="lead-capture"',
   'id="diagnostic-next-step"',
   'data-diagnostic-id="16f5812b-6a27-44f6-b5b4-557a720a6425"',
   'data-clarity-mask',
   '/assets/diagnostico.js',
   '/assets/analytics-consent.js',
   '/assets/operational-subpages.css',
   '/assets/control-tower.css'
 ]) assert.ok(d.includes(required),"diagnostico.html missing "+required);
 assert.match(d,/class="control-tower-home lce-subpage lce-diagnostic-page"/);
 assert.match(d,/Consulta nuestro aviso de privacidad/);
 assert.doesNotMatch(d,/La API|el servidor|habilita el formulario/i);
});
test("QA: approved full privacy content, revision, cookies and rights are preserved",()=>{
 const p=read("privacidad.html");
 for(const required of [
   'data-lce-privacy-revision="2026-11-13-lce-diagnostico-v1"',
   'Rosa Angélica Sánchez Osnaya',
   'Plaza Melchor Ocampo 30',
   '4. Cookies, tecnologías similares y Microsoft Clarity',
   '7. Derechos ARCO, limitación y revocación',
   '8. Cambios al aviso y autoridad competente',
   'asanchez@lce.com.mx',
   '/assets/analytics-consent.js',
   '/assets/operational-subpages.css',
   '/assets/control-tower.css'
 ]) assert.ok(p.includes(required),"privacy missing "+required);
 assert.match(p,/class="control-tower-home lce-subpage lce-privacy-page"/);
 assert.doesNotMatch(p,/PROPUESTA PARA VISTO BUENO|Versión propuesta|pendiente del visto bueno/i);
});
test("QA: all three campaign landings preserve a shared visual identity and live consent",()=>{
 const css=read("assets/operational-subpages.css");
 assert.match(css,/\.lce-subpage \.diagnostic-layout/);
 assert.match(css,/\.lce-privacy-page \.privacy-layout/);
 assert.match(css,/@media\(max-width:650px\)/);
 for(const route of [
  "l/costos-importacion.html",
  "l/cumplimiento-nom.html",
  "l/diagnostico-logistico.html"
 ]){
   const html=read(route);
   assert.match(html,/\/assets\/landing-control-tower\.css/);
   assert.match(html,/\/assets\/analytics-consent\.js/);
   assert.match(html,/HABLEMOS DE TU OPERACIÓN/);
   assert.match(html,/Solicitud de orientación/);
 }
});
