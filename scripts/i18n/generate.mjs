// Rebuild static localized pages from the canonical Spanish source. Run from repository root.
import {readFileSync,writeFileSync,mkdirSync} from "node:fs";
import {dirname} from "node:path";
const root="src/LceWeb.Api/wwwroot/";
const paths=["index.html","servicios/planeacion.html","servicios/logistica.html","servicios/normas-nom.html","l/costos-importacion.html","l/cumplimiento-nom.html","l/diagnostico-logistico.html","l/manifestacion-de-valor.html","l/proveedores-confiables.html","l/compliance-aduanero.html","diagnostico.html","privacidad.html"];
const langs=["es","en","fr","pt"];
const dict=Object.assign({},...["common","landings","privacy","extra"].map(n=>JSON.parse(readFileSync("scripts/i18n/"+n+".json","utf8"))));
const ui=JSON.parse(readFileSync(root+"js/diagnostics/commercial-i18n.js","utf8").match(/const ui = (\{[\s\S]+?\});\s*const definitions/)[1]);
for(const v of Object.values(ui))if(!dict[v[0]])dict[v[0]]=v.slice(1);
const stable=new Set(["LCE","Patrik Mattson","Aldo Morante","Itzell Escorcia","· Dometic MX","· AM Denmark","· RICOM","asanchez@lce.com.mx","NOM","AM / NOM","NOM / ?","ETA / ?","Español","English","Français","Português","Rosa Angélica Sánchez Osnaya","Plaza Melchor Ocampo 30, interior 5, colonia Cuauhtémoc, C.P. 06500, Ciudad de México, México","2026-11-13-lce-diagnostico-v1 ·","2026-11-13-lce-diagnostico-v1.","Microsoft Clarity","zd6brtj9wu","_clck","_clsk","CLID","MUID","ANONCHK","MR","SM","lce.com.mx/index.php/privacy-policy-es/","Secretaría Anticorrupción y Buen Gobierno","Idioma / Language / Langue / Idioma"]);
const numbered={"CONTROL":["CONTROL","CONTRÔLE","CONTROLE"],"RUTA":["ROUTE","PARCOURS","ROTA"],"DECISIÓN":["DECISION","DÉCISION","DECISÃO"],"REVISIÓN":["REVIEW","EXAMEN","REVISÃO"],"LECTURA":["REVIEW","ANALYSE","ANÁLISE"]};
const labels={"es":["Idioma del sitio","Español","English","Français","Português"],"en":["Website language","Español","English","Français","Português"],"fr":["Langue du site","Español","English","Français","Português"],"pt":["Idioma do site","Español","English","Français","Português"]};
const reference={"en":["Reference translation of the approved Spanish notice. It has not been separately approved. The original Spanish text remains authoritative.","Read the approved Spanish notice"],"fr":["Traduction de référence de l’avis espagnol approuvé. Elle n’a pas fait l’objet d’une approbation distincte. Le texte original espagnol fait foi.","Lire l’avis espagnol approuvé"],"pt":["Tradução de referência do aviso aprovado em espanhol. Não foi aprovada separadamente. O texto original em espanhol prevalece.","Ler o aviso aprovado em espanhol"]};
function escapeHtml(v){return v.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;")}
function pageUrl(p,lang){return (lang==="es"?"":"/"+lang)+(p==="index.html"?"/":"/"+p)}
function renderPage(source,p,lang,dict,stable,numbered,labels,reference,paths,langs){
 const origin="https://lce-glt.azurewebsites.net";
 const i=langs.indexOf(lang)-1, missing=new Set();
 const lookup=(s,strict=true)=>{
   if(i<0||!s||stable.has(s)||!/[a-záéíóúñ]/i.test(s))return s;
   if(dict[s])return dict[s][i];
   const n=s.match(/^(\d{2}) \/ (.+)$/);
   if(n&&numbered[n[2]])return n[1]+" / "+numbered[n[2]][i];
   if(strict)missing.add(s);
   return s;
 };
 let html=source.replace(/<!-- lce-locale-(head|nav) -->[\s\S]*?<!-- \/lce-locale-\1 -->/g,"");
 if(p==="l/diagnostico-logistico.html")html=html.replace("La API y el navegador presentan una lectura orientativa.","Tus respuestas ayudan a nuestro equipo a preparar una primera conversación.");
 if(i>=0){
   html=html.replace(/<html lang="[^"]+"/,`<html lang="${lang}"`);
   html=html.split(/(<script\b[^>]*>[\s\S]*?<\/script>)/gi).map((part,n)=>n%2?part:part.replace(/>([^<>]+)(?=<)/g,(all,txt)=>{
     const s=txt.trim();const translated=lookup(s);
     return translated===s?all:">"+txt.replace(s,()=>escapeHtml(translated));
   })).join("");
   html=html.replace(/\b(aria-label|alt|title|placeholder)="([^"]*)"/g,(all,key,value)=>`${key}="${escapeHtml(lookup(value))}"`);
   html=html.replace(/(<meta\s+(?:name="description"|property="og:(?:title|description)")\s+content=")([^"]*)"/g,(all,start,value)=>start+escapeHtml(lookup(value))+'"');
   html=html.replace(/property="og:locale" content="es_MX"/,`property="og:locale" content="${{en:"en_US",fr:"fr_FR",pt:"pt_BR"}[lang]}"`);
   html=html.replace(/(href|data-form-target)="(\/[^"]*)"/g,(all,attr,val)=>{
     if(val.startsWith("//"))return all;
     const match=val.match(/^([^?#]*)(.*)$/);
     const target=match[1]==="/"||match[1]==="/index.html"?"index.html":match[1].slice(1);
     return paths.includes(target)?`${attr}="${pageUrl(target,lang)}${match[2]}"`:all;
   });
   html=html.replace(/property="og:url" content="[^"]*"/,`property="og:url" content="${origin+pageUrl(p,lang)}"`);
   html=html.replace(/<meta name="robots" content="[^"]*"\s*\/?>/g,"");
   if(p==="privacidad.html"){
     html=html.replace(/(<article class="privacy-content"[^>]*>)/,`$1<p class="lce-translation-notice" role="note">${escapeHtml(reference[lang][0])} <a href="/privacidad.html" lang="es-MX">${escapeHtml(reference[lang][1])}</a>.</p>`);
   }
 }
 html=html.replace(/<link rel="canonical" href="[^"]*"\s*\/?>/g,"");
 const head=`<!-- lce-locale-head -->\n<link rel="canonical" href="${origin+pageUrl(p,lang)}">\n${p==="privacidad.html"?"":langs.map(l=>`<link rel="alternate" hreflang="${l}" href="${origin+pageUrl(p,l)}">`).join("\n")+'\n<link rel="alternate" hreflang="x-default" href="'+origin+pageUrl(p,"es")+'">'}\n${i>=0?`<meta name="robots" content="noindex,follow" data-lce-translation="${p==="privacidad.html"?"reference":"editorial"}">`:""}\n<link rel="stylesheet" href="/assets/site-locale.css">\n<script src="/assets/site-locale.js" defer></script>\n<!-- /lce-locale-head -->`;
 const menu=`<!-- lce-locale-nav --><nav class="lce-language-bar" aria-label="${escapeHtml(labels[lang][0])}"><div class="shell"><span>${escapeHtml(labels[lang][0])}</span><div class="lce-language-options">${langs.map((l,n)=>`<a href="${pageUrl(p,l)}" data-lce-locale="${l}" lang="${l}" hreflang="${l}"${l===lang?' aria-current="page"':""}>${labels[lang][n+1]}</a>`).join("")}</div></div></nav><!-- /lce-locale-nav -->`;
 html=html.replace("</head>",head+"\n</head>").replace("</header>","</header>"+menu);
 return {html,missing:[...missing]};
}
const originals=paths.map(p=>[p,readFileSync(root+p,"utf8")]);
for(const [p,source] of originals)for(const lang of langs){
  const {html,missing}=renderPage(source,p,lang,dict,stable,numbered,labels,reference,paths,langs);
  if(missing.length)throw new Error(p+" "+lang+": "+missing.join(" | "));
  const target=root+(lang==="es"?"":lang+"/")+p;
  mkdirSync(dirname(target),{recursive:true});writeFileSync(target,html.trimEnd()+"\n");
}
console.log("Generated 48 static pages (12 routes × 4 languages).");
