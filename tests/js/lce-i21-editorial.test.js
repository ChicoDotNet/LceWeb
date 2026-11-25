import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const root=new URL('../../src/LceWeb.Api/wwwroot/',import.meta.url);
const read=p=>readFileSync(new URL(p,root),'utf8');
const sitemap=read('sitemap.xml');
for(const [slug,campaign] of [['manifestacion-de-valor','mve-2027'],['proveedores-confiables','proveedores-2027'],['compliance-aduanero','compliance-2027']]) test(slug+' is an isolated editorial candidate',()=>{const page=read('l/'+slug+'.html');assert.match(page,/name="robots" content="noindex,follow"/);assert.ok(page.includes('utm_campaign='+campaign));assert.ok(page.includes('/diagnostico.html'));assert.ok(page.includes('/privacidad.html'));assert.ok(!sitemap.includes(slug));});
test('MVE source and date are explicit',()=>{const page=read('l/manifestacion-de-valor.html');assert.ok(page.includes('5804604'));assert.ok(page.includes('2026-11-25'));});
test('sitemap and robots use same current canonical origin',()=>{assert.ok(read('robots.txt').includes('https://lce-glt.azurewebsites.net/sitemap.xml'));assert.ok(sitemap.includes('https://lce-glt.azurewebsites.net/diagnostico.html'));});
