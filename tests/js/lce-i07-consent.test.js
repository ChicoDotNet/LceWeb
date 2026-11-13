import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = path => readFileSync(new URL("../../" + path, import.meta.url), "utf8");

const program = read("src/LceWeb.Api/Program.cs");
const models = read("src/LceWeb.Api/Leads/LeadSubmissionModels.cs");
const storage = read("src/LceWeb.Api/Leads/AzureTableLeadSubmissionRepository.cs");
const client = read("src/LceWeb.Api/wwwroot/js/leads/lead-capture.js");
const controller = read("src/LceWeb.Api/wwwroot/assets/diagnostico.js");
const technical = read("src/LceWeb.Api/wwwroot/diagnostic-demo.html");
const notice = read("src/LceWeb.Api/wwwroot/privacidad.html");
const schema = JSON.parse(read("contracts/lead-submission.schema.json"));
const activate = read("scripts/azure/i07-activate-leads.ps1");

test("production public capture requires AzureTable and a versioned approved privacy notice", () => {
  assert.match(program, /LeadStorageOptions\.AzureTableProvider/);
  assert.match(program, /Leads:PublicCaptureEnabled/);
  assert.match(program, /Leads:PrivacyNoticeApproved/);
  assert.match(program, /Leads:PrivacyNoticeVersion/);
  assert.match(program, /!string\.IsNullOrWhiteSpace\(privacyNoticeVersion\)/);
  assert.match(program, /privacyNoticeVersion = publicLeadCaptureEnabled \? privacyNoticeVersion : null/);
});

test("a lead with no or stale privacy acceptance is rejected before durable store", () => {
  assert.match(program, /if \(!request\.PrivacyAccepted \|\|/);
  assert.match(program, /!string\.Equals\(request\.PrivacyNoticeVersion, privacyNoticeVersion/);
  assert.ok(program.indexOf("if (!request.PrivacyAccepted") < program.indexOf("await leadRepository.StoreAsync"));
  assert.match(models, /public bool PrivacyAccepted \{ get; init; \}/);
  assert.match(models, /public string\? PrivacyNoticeVersion \{ get; init; \}/);
  assert.match(models, /public DateTimeOffset\? PrivacyAcceptedUtc \{ get; init; \}/);
  assert.deepEqual(schema.properties.privacyAccepted, {
    type: "boolean",
    const: true,
    description: "The user expressly accepted the notice for responding to this inquiry; no marketing opt-in is implied."
  });
  assert.ok(schema.required.includes("privacyNoticeVersion"));
});

test("only the server generates consent UTC and stores notice version in Azure Tables", () => {
  assert.match(program, /var acceptedUtc = timeProvider\.GetUtcNow\(\)/);
  assert.match(program, /PrivacyAcceptedUtc = acceptedUtc/);
  assert.match(storage, /\["PrivacyAcceptedUtc"\] = submission\.PrivacyAcceptedUtc/);
  assert.match(storage, /\["PrivacyNoticeVersion"\] = submission\.PrivacyNoticeVersion/);
  assert.match(client, /data\.get\("privacyAgreement"\) === "on"/);
  assert.match(client, /privacyNoticeVersion/);
  assert.doesNotMatch(client, /privacyAcceptedUtc/);
  assert.match(controller, /capabilities\.privacyNoticeVersion/);
  assert.match(technical, /capabilities\.privacyNoticeVersion/);
});

test("operator activation is gated, preview by default and rejects still-draft legal notice", () => {
  assert.match(activate, /\[switch\]\$Apply/);
  assert.match(activate, /\[switch\]\$ConfirmLegalApproval/);
  assert.match(activate, /ApprovedNoticeVersion/);
  assert.match(activate, /legalPlaceholdersPresent/);
  assert.match(activate, /data-lce-privacy-revision=/);
  assert.match(activate, /Rollback|rollback|roll.?back/i);
  assert.match(notice, /Aviso de privacidad integral/);
  assert.match(notice, /data-lce-privacy-revision="2026-11-13-lce-diagnostico-v1"/);
});
