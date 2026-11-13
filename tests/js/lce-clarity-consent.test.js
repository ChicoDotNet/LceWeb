import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../", import.meta.url);
const read = path => readFileSync(new URL(path, root), "utf8");
const page = read("src/LceWeb.Api/wwwroot/index.html");
const diagnostic = read("src/LceWeb.Api/wwwroot/diagnostico.html");
const privacy = read("src/LceWeb.Api/wwwroot/privacidad.html");
const consent = read("src/LceWeb.Api/wwwroot/assets/analytics-consent.js");
const clarity = read("src/LceWeb.Api/client/clarity-client.js");
const packageJson = JSON.parse(read("package.json"));
const bundleWorkflow = read(".github/workflows/lce-package.yml");
const lead = read("src/LceWeb.Api/wwwroot/js/leads/lead-capture.js");

test("Clarity is installed via pinned official npm package and compiled in deploy artifact", () => {
  assert.equal(packageJson.dependencies["@microsoft/clarity"], "1.0.2");
  assert.match(packageJson.scripts["build:clarity"], /esbuild/);
  assert.match(bundleWorkflow, /npm install --no-audit --no-fund/);
  assert.match(bundleWorkflow, /npm run build:clarity/);
  assert.match(bundleWorkflow, /publish\/wwwroot\/assets\/clarity-bundle\.js|lce-publish\/wwwroot\/assets\/clarity-bundle\.js/);
  assert.match(clarity, /from "@microsoft\/clarity"/);
  assert.match(clarity, /zd6brtj9wu/);
});

test("consent is required before Clarity bundle is even fetched", () => {
  assert.match(consent, /value === "accepted"/);
  assert.match(consent, /"Permitir analíticas"/);
  assert.match(consent, /"Rechazar"/);
  assert.match(consent, /if \(stored === "accepted"\)/);
  assert.match(consent, /import\("\/assets\/clarity-bundle\.js"\)/);
  assert.match(consent, /window\.location\.reload\(\)/);
  assert.match(clarity, /consentv2/);
  assert.match(clarity, /ad_Storage: "denied"/);
  assert.match(clarity, /analytics_Storage: "granted"/);
  for (const p of [page, diagnostic, privacy]) {
    assert.match(p, /\/assets\/analytics-consent\.js/);
    assert.match(p, /\/assets\/analytics-consent\.css/);
    assert.doesNotMatch(p, /https:\/\/www\.clarity\.ms\/tag\//);
  }
});

test("sensitive questionnaire and contact are masked, contact requires confirmation", () => {
  assert.match(diagnostic, /data-clarity-mask/);
  assert.match(lead, /<form class="lce-lead" data-clarity-mask/);
  assert.match(lead, /name="privacyAgreement" type="checkbox" required/);
  assert.match(lead, /\/privacidad\.html/);
  assert.match(privacy, /no sustituye el aviso de privacidad integral/);
});
