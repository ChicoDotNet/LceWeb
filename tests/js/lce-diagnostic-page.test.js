import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../src/LceWeb.Api/", import.meta.url);
const read = path => readFileSync(new URL(path, root), "utf8");
const page = read("wwwroot/diagnostico.html");
const js = read("wwwroot/assets/diagnostico.js");
const styles = read("wwwroot/assets/diagnostico.css");
const program = read("Program.cs");
const technical = read("wwwroot/diagnostic-demo.html");

test("diagnostic route connects to versioned definition via existing runtime", () => {
  assert.match(page, /id="lce-diagnostic"/);
  assert.match(page, /data-diagnostic-id="16f5812b-6a27-44f6-b5b4-557a720a6425"/);
  assert.match(page, /\/assets\/diagnostico\.js/);
  assert.match(js, /\/js\/diagnostics\/diagnostic-runtime\.js/);
  assert.match(js, /diagnostic:completed/);
  assert.match(styles, /@media\(max-width:850px\)/);
});

test("diagnostic is read-only and offers real outbound contact after result", () => {
  assert.match(page, /id="diagnostic-next-step"[^>]*hidden/);
  assert.match(page, /mailto:asanchez@lce\.com\.mx/);
  assert.match(page, /tel:\+525590476846/);
  assert.match(page, /no se envían ni se almacenan en LCE/);
  assert.match(page, /Evita introducir RFC, e\.firma/);
  assert.doesNotMatch(page, /\/api\/leads|name="email"|name="phoneNumber"/);
  assert.doesNotMatch(js, /\/api\/leads|fetch\(|localStorage|sessionStorage/);
});

test("published capture must be durable and explicitly enabled, not memory", () => {
  assert.match(program, /Leads:PublicCaptureEnabled/);
  assert.match(program, /LeadStorageOptions\.AzureTableProvider/);
  assert.match(program, /if \(!publicLeadCaptureEnabled\)/);
  assert.match(program, /Status503ServiceUnavailable/);
  assert.match(program, /\/api\/public-capabilities/);
  assert.match(technical, /\/api\/public-capabilities/);
  assert.match(technical, /leadCaptureEnabled === true/);
});
