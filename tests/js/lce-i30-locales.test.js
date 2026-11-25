import test from "node:test";
import assert from "node:assert/strict";
import {readFileSync,existsSync} from "node:fs";
import {runInNewContext} from "node:vm";
import {languageFromUrl} from "../../src/LceWeb.Api/wwwroot/js/diagnostics/commercial-i18n.js";
import {collectAcquisition,buildLeadSubmissionPayload} from "../../src/LceWeb.Api/wwwroot/js/leads/lead-capture.js";
const root=new URL("../../src/LceWeb.Api/wwwroot/",import.meta.url);
const read=p=>readFileSync(new URL(p,root),"utf8");
const langs=["es","en","fr","pt"];
const routes=["index.html","diagnostico.html","privacidad.html","servicios/planeacion.html","servicios/logistica.html","servicios/normas-nom.html","l/costos-importacion.html","l/cumplimiento-nom.html","l/diagnostico-logistico.html","l/manifestacion-de-valor.html","l/proveedores-confiables.html","l/compliance-aduanero.html"];
const route=(p,l)=>(l==="es"?"/":"/"+l+"/")+(p==="index.html"?"":p);
for(const l of langs)test("I30: complete static "+l+" site",()=>{
  for(const p of routes){
    const path=(l==="es"?"":l+"/")+p;
    const h=read(path);
    assert.ok(h.includes('<html lang="'+(l==="es"?"es-MX":l)+'"'),path);
    assert.ok(h.includes('rel="canonical" href="https://lce-glt.azurewebsites.net'+route(p,l)+'"'),path);
    assert.ok(h.includes('/assets/site-locale.css')&&h.includes('/assets/site-locale.js'),path);
    for(const a of langs)assert.ok(h.includes('href="'+route(p,a)+'" data-lce-locale="'+a+'"'),path+" -> "+a);
    if(p!=="privacidad.html")for(const a of langs)assert.ok(h.includes('hreflang="'+a+'" href="https://lce-glt.azurewebsites.net'+route(p,a)+'"'),path);
    if(l!=="es"&&p==="privacidad.html"){assert.match(h,/data-lce-translation="reference"/);assert.match(h,/lce-translation-notice/);assert.match(h,/href="\/privacidad.html" lang="es-MX"/);}
    if(p==="diagnostico.html"||p==="l/diagnostico-logistico.html"){assert.match(h,/data-clarity-mask/);assert.match(h,/id="lead-capture"/);}
    if(l!=="es"&&p!=="index.html"&&p!=="privacidad.html")assert.ok(h.includes('href="/'+l+'/"'),path);
    for(const a of h.matchAll(/href="(\/[^"#?]*)(?:[?#][^"]*)?"/g)){
      const target=a[1]; if(target.startsWith("//")||target.startsWith("/api/"))continue;
      const file=target.endsWith("/")?target+"index.html":target;
      assert.ok(existsSync(new URL(file.slice(1),root)),path+" -> "+file);
    }
  }
});
test("legacy diagnostic query and static routes share stable language IDs",()=>{
 assert.equal(languageFromUrl({pathname:"/diagnostico.html",search:"?lang=fr"}),"fr");
 assert.equal(languageFromUrl({pathname:"/en/l/diagnostico-logistico.html",search:"?lang=fr"}),"en");
 assert.equal(languageFromUrl({pathname:"/pt/diagnostico.html",search:""}),"pt");
 assert.equal(languageFromUrl({pathname:"/diagnostico.html",search:"?lang=xx"}),"es");
 const data=collectAcquisition({search:"",href:"https://example.test/fr/l/diagnostico-logistico.html"},"","diagnostic-first-logistica");
 assert.equal(data.utmCampaign,"diagnostic-first-logistica");
 const p=buildLeadSubmissionPayload({diagnosticId:"x",definitionVersion:2,language:"fr",answers:{}},{name:"Test",email:"test@example.test",phoneNumber:"5555555555",callingCode:"+52"},data,"notice",true);
 assert.equal(p.language,"fr");
});
test("all localized CTA modes retain locale, translated text and campaign attribution",()=>{
 const script=read("assets/landing-experiments.js");
 for(const l of ["en","fr","pt"])for(const mode of ["call","whatsapp","email","form"]){
   const links=[{dataset:{},addEventListener(){}}];const label={};
   const document={documentElement:{lang:l},body:{dataset:{campaign:"mve-2027",formTarget:"/"+l+"/diagnostico.html?utm_source=landing&utm_campaign=mve-2027"}},querySelectorAll(sel){return sel==="[data-cta-primary]"?links:[label]}};
   runInNewContext(script,{URLSearchParams,document,window:{location:{search:"?contact="+mode+"&utm_source=google&utm_term=customs"}}});
   assert.notEqual(links[0].textContent,"Hacer diagnóstico y solicitar seguimiento ↗");
   if(mode==="form"){assert.match(links[0].href,new RegExp("^/"+l+"/diagnostico.html"));assert.ok(links[0].href.includes("utm_source=google"));assert.ok(links[0].href.includes("utm_campaign=mve-2027"));}
 }
});
