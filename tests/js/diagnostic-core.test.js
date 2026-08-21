import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

import {
  buildDiagnosticResult,
  conditionMatches,
  getVisibleSteps,
  pruneHiddenAnswers,
  validateAnswer
} from "../../src/LceWeb.Api/wwwroot/js/diagnostics/diagnostic-core.js";

const definition = JSON.parse(
  fs.readFileSync(
    new URL("../../src/LceWeb.Api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425.v1.json", import.meta.url),
    "utf8"
  )
);

test("conditions support anyOf and noneOf", () => {
  const answers = { stage: { answerIds: ["blocked"] } };

  assert.equal(
    conditionMatches({ questionId: "stage", operator: "anyOf", values: ["blocked", "in-progress"] }, answers),
    true
  );
  assert.equal(
    conditionMatches({ questionId: "stage", operator: "noneOf", values: ["blocked"] }, answers),
    false
  );
});

test("conditional steps become visible from stable answer ids", () => {
  assert.deepEqual(getVisibleSteps(definition, {}).map(step => step.id), ["context"]);

  const activeAnswers = { stage: { answerIds: ["in-progress"] } };
  assert.deepEqual(getVisibleSteps(definition, activeAnswers).map(step => step.id), ["context", "detail"]);
});

test("hidden branch answers are pruned", () => {
  const answers = {
    stage: { answerIds: ["planning"] },
    "blocker-detail": { text: "This should disappear when detail is hidden." }
  };

  assert.deepEqual(pruneHiddenAnswers(definition, answers), {
    stage: { answerIds: ["planning"] }
  });
});

test("multiple choice validation enforces configured maximum", () => {
  const priorities = definition.steps[0].questions.find(question => question.id === "priorities");
  const errors = validateAnswer(priorities, { answerIds: ["cost", "time", "compliance"] });

  assert.ok(errors.some(error => error.includes("máximo 2")));
});

test("result calculation scores only visible answers", () => {
  const result = buildDiagnosticResult(definition, {
    stage: { answerIds: ["planning"] },
    priorities: { answerIds: ["compliance"] },
    "blocker-detail": { text: "Hidden stale answer" }
  });

  assert.deepEqual(result.scores, { readiness: 2, urgency: 2 });
  assert.deepEqual(result.results.map(item => item.id), ["readiness-active"]);
  assert.equal("blocker-detail" in result.answers, false);
});

test("high urgency can resolve alongside a readiness band", () => {
  const result = buildDiagnosticResult(definition, {
    stage: { answerIds: ["blocked"] },
    priorities: { answerIds: ["time"] }
  });

  assert.deepEqual(result.scores, { readiness: 1, urgency: 4 });
  assert.deepEqual(result.results.map(item => item.id), ["readiness-low", "urgency-high"]);
});
