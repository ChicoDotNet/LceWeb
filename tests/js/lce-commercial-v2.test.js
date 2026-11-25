import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { computeScores, getVisibleSteps, resolveResults } from "../../src/LceWeb.Api/wwwroot/js/diagnostics/diagnostic-core.js";
const definition = JSON.parse(readFileSync(new URL("../../src/LceWeb.Api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425.v2.json", import.meta.url)));
test("commercial v2 has five steps, unique IDs and non-overlapping bands", () => {
  assert.equal(definition.steps.length, 5);
  const questions = definition.steps.flatMap(s => s.questions);
  assert.equal(new Set(questions.map(q => q.id)).size, questions.length);
  for(let score=0;score<=17;score++) assert.equal(resolveResults(definition,{commercial:score}).length,1);
});
test("other merchandise requires the conditional text only when selected", () => {
  assert.ok(!getVisibleSteps(definition,{}).flatMap(s=>s.questions).some(q=>q.id==="other-product"));
  assert.ok(getVisibleSteps(definition,{products:{answerIds:["other"]}}).flatMap(s=>s.questions).some(q=>q.id==="other-product"));
});
test("maximum without verified attachments is 16; priorities do not affect scoring", () => {
  const answers = { business:{answerIds:["company-foreign"]},stage:{answerIds:["blocked"]},ticket:{answerIds:["over-100k"]},volume:{answerIds:["over-12"]},technical:{answerIds:["available"]},registry:{answerIds:["yes"]},priorities:{answerIds:["trust","cost"]}};
  assert.equal(computeScores(definition,answers).commercial,16);
  assert.equal(resolveResults(definition,{commercial:16})[0].id,"priority");
});
