import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../src/LceWeb.Api/wwwroot/", import.meta.url);
const page = readFileSync(new URL("index.html", root), "utf8");
const css = readFileSync(new URL("assets/site.css", root), "utf8");
const js = readFileSync(new URL("assets/site.js", root), "utf8");
const favicon = readFileSync(new URL("assets/favicon.svg", root), "utf8");

test("homepage has actual branding and valid business contact links", () => {
  assert.match(page, /<html lang="es-MX">/);
  assert.match(page, /Líderes en Comercio Exterior/);
  assert.match(page, /mailto:asanchez@lce\.com\.mx/);
  assert.match(page, /href="tel:\+525590476846"/);
  assert.doesNotMatch(page, /alert\(|onsubmit=|testimonios ficticios|demo: conectar/i);
  assert.match(page, /<meta name="description"/);
});

test("all internal anchors resolve to existing sections", () => {
  const ids = new Set([...page.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]));
  const anchors = [...page.matchAll(/href="#([^"]+)"/g)].map(match => match[1]);
  assert.ok(anchors.length >= 8);
  for (const anchor of anchors) assert.ok(ids.has(anchor), "missing anchor: #" + anchor);
});

test("public assets exist and responsive accessible navigation is provided", () => {
  for (const path of ["/assets/site.css", "/assets/site.js", "/assets/favicon.svg"]) {
    assert.ok(page.includes(path));
  }
  assert.match(css, /@media\(max-width:800px\)/);
  assert.match(css, /@media\(prefers-reduced-motion:reduce\)/);
  assert.match(page, /aria-expanded="false"/);
  assert.match(js, /aria-expanded/);
  assert.match(favicon, /<svg/);
});

test("homepage deliberately does not accept leads until Azure Table Storage is ready", () => {
  assert.doesNotMatch(page, /<form\b/i);
  assert.doesNotMatch(page, /\/api\/leads/);
});