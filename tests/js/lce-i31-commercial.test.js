import test from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
const root=new URL("../../src/LceWeb.Api/wwwroot/",import.meta.url);
const read=p=>readFileSync(new URL(p,root),"utf8");
const paths=["index.html","diagnostico.html","servicios/planeacion.html","servicios/logistica.html","servicios/normas-nom.html","l/costos-importacion.html","l/cumplimiento-nom.html","l/diagnostico-logistico.html","l/manifestacion-de-valor.html","l/proveedores-confiables.html","l/compliance-aduanero.html"];
for(const lang of ["es","en","fr","pt"])test("I31: institutional commercial contact and advisor-first diagnostic "+lang,()=>{
 const prefix=lang==="es"?"":lang+"/";
 for(const path of paths){
   const html=read(prefix+path);
   assert.doesNotMatch(html,/(?:asanchez|angelica)@lce\.com\.mx/i,path);
   assert.ok(html.includes("contacto@lce.com.mx"),path);
 }
 assert.doesNotMatch(read(prefix+"diagnostico.html"),/class="diagnostic-sidebar__contact"/);
 assert.match(read(prefix+"diagnostico.html"),/id="diagnostic-next-step"[^>]*hidden/);
 assert.match(read(prefix+"privacidad.html"),/data-lce-privacy-revision="2026-11-13-lce-diagnostico-v1"/);
});
test("I31: email CTA uses only corporate address",()=>{
 const js=read("assets/landing-experiments.js");
 assert.match(js,/mailto:contacto@lce\.com\.mx/);
 assert.doesNotMatch(js,/(?:asanchez|angelica)@lce\.com\.mx/i);
});
