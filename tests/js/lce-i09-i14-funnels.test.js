import {readFileSync, existsSync} from "node:fs";
import test from "node:test";
import assert from "node:assert/strict";
import {fileURLToPath} from "node:url";
const root = new URL("../../src/LceWeb.Api/wwwroot/",import.meta.url);
const read=p=>readFileSync(new URL(p,root),"utf8");
const home=read("index.html");
const pages=[
 ["A","l/costos-importacion.html","hidden-leverage-costos","Una importación puede salir cara"],
 ["B","l/cumplimiento-nom.html","proof-first-nom","Cuando una importación importa"],
 ["C","l/diagnostico-logistico.html","diagnostic-first-logistica","Antes de pedir una cotización"]
];
test("corporate Home contains client proof, commitment and real service navigation",()=>{
 for(const client of ["Dometic-logo.png","AM_denmark-logo.png","RICOM-logo.png"]){assert.match(home,new RegExp(client.replace(".","\\.")));}
 for(const path of ["/servicios/planeacion.html","/servicios/logistica.html","/servicios/normas-nom.html"]){assert.ok(home.includes(path),path);}
 assert.match(home,/control-tower.css/);assert.match(home,/Nuestra promesa/i);
 assert.match(home,/id="experiencia"/);
 for(const [id,path] of pages){assert.ok(!home.includes(path),"Home must not leak experiment "+id);}
});
test("three landings are distinct, never crosslink and always route to Home",()=>{
 for(const [id,path,campaign,headline] of pages){
 const p=read(path);
 assert.ok(p.includes(headline),path);
 assert.ok(p.includes('data-campaign="'+campaign+'"'),path);
 assert.ok(p.includes('href="/"'),path+" must link to Home");
 assert.ok(p.includes('landing-control-tower.css'),path);
 assert.ok(p.includes('analytics-consent.js'),path);
 assert.ok(p.includes('landing-experiments.js'),path);
 for(const [otherId,other] of pages){if(id!==otherId)assert.ok(!p.includes(other),path+" cross-links "+other);}
 }
});
test("four CTA variants are supported without collecting PII in analytics events",()=>{
 const js=read("assets/landing-experiments.js");
 for(const mode of ["call","whatsapp","email","form"]){assert.ok(js.includes(mode));}
 assert.match(js,/new URLSearchParams/);
 assert.match(js,/data-cta-primary/);
 assert.match(js,/lce_cta_/);
 assert.doesNotMatch(js,/contact\.name|contact\.email|phoneNumber/);
});
test("diagnostic-first uses real versioned backend and only gated contact",()=>{
 const p=read("l/diagnostico-logistico.html");
 assert.match(p,/data-diagnostic-id="16f5812b-6a27-44f6-b5b4-557a720a6425"/);
 assert.match(p,/id="lead-capture"/);
 assert.match(p,/data-clarity-mask/);
 assert.match(p,/assets\/diagnostico.js/);
 const runtime=read("assets/diagnostico.js");
 assert.match(runtime,/api\/public-capabilities/);
 assert.match(runtime,/leadCaptureEnabled/);
});
test("each corporate service page exists and links back to Home",()=>{
 for(const p of ["servicios/planeacion.html","servicios/logistica.html","servicios/normas-nom.html"]){
 assert.ok(existsSync(fileURLToPath(new URL(p,root))));
 const s=read(p);assert.match(s,/href="\/"|href="\/\#servicios"/);assert.match(s,/service-pages.css/);
 }
});
