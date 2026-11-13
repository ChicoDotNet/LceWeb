import test from "node:test";
import assert from "node:assert/strict";

import { buildLeadSubmissionPayload } from "../../src/LceWeb.Api/wwwroot/js/leads/lead-capture.js";

test("buildLeadSubmissionPayload sends raw diagnostic answers and optional phone", () => {
  const payload = buildLeadSubmissionPayload(
    {
      diagnosticId: "16f5812b-6a27-44f6-b5b4-557a720a6425",
      definitionVersion: 1,
      answers: {
        stage: { answerIds: ["planning"] },
        "blocker-detail": { text: "  Documento pendiente  " }
      },
      scores: { readiness: 99 },
      results: [{ id: "client-only-result" }]
    },
    {
      name: "  Persona Demo  ",
      email: "  persona@example.com  ",
      callingCode: "+52",
      phoneNumber: "55 1234 5678",
      website: ""
    },
    {
      utmSource: "google",
      pageUrl: "https://example.test/landing"
    });

  assert.equal(payload.contact.name, "Persona Demo");
  assert.equal(payload.contact.email, "persona@example.com");
  assert.deepEqual(payload.contact.phone, {
    callingCode: "+52",
    number: "55 1234 5678"
  });
  assert.deepEqual(payload.answers, [
    { questionId: "stage", answerIds: ["planning"], text: null },
    { questionId: "blocker-detail", answerIds: [], text: "  Documento pendiente  " }
  ]);
  assert.equal("scores" in payload, false);
  assert.equal("results" in payload, false);
});

test("buildLeadSubmissionPayload omits phone when the optional number is empty", () => {
  const payload = buildLeadSubmissionPayload(
    {
      diagnosticId: "16f5812b-6a27-44f6-b5b4-557a720a6425",
      definitionVersion: 1,
      answers: {}
    },
    {
      name: "Demo",
      email: "demo@example.com",
      callingCode: "+52",
      phoneNumber: "",
      website: ""
    },
    {});

  assert.equal(payload.contact.phone, null);
});


test("lead submission includes the server-approved notice revision and explicit consent only", () => {
  const payload = buildLeadSubmissionPayload(
    { diagnosticId: "16f5812b-6a27-44f6-b5b4-557a720a6425", definitionVersion: 1, answers: {} },
    { name: "Demo", email: "demo@example.com", phoneNumber: "", website: "" },
    {},
    "2026-11-13-v1",
    true);
  assert.equal(payload.privacyAccepted, true);
  assert.equal(payload.privacyNoticeVersion, "2026-11-13-v1");
  assert.equal("privacyAcceptedUtc" in payload, false, "Only the server can assign acceptance timestamps");
});
