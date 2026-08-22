export function sortByOrder(items = []) {
  return [...items].sort((left, right) => (left.order ?? 0) - (right.order ?? 0));
}

export function conditionMatches(condition, answers = {}) {
  if (!condition) {
    return true;
  }

  const selected = answers[condition.questionId]?.answerIds ?? [];
  const expected = condition.values ?? [];
  const hasAny = expected.some(value => selected.includes(value));

  switch (condition.operator) {
    case "anyOf":
      return hasAny;
    case "noneOf":
      return !hasAny;
    default:
      throw new Error(`Unsupported condition operator: ${condition.operator}`);
  }
}

export function getVisibleSteps(definition, answers = {}) {
  return sortByOrder(definition.steps)
    .filter(step => conditionMatches(step.visibleWhen, answers))
    .map(step => ({
      ...step,
      questions: sortByOrder(step.questions)
        .filter(question => conditionMatches(question.visibleWhen, answers))
    }));
}

export function getVisibleQuestionIds(definition, answers = {}) {
  return new Set(
    getVisibleSteps(definition, answers)
      .flatMap(step => step.questions.map(question => question.id))
  );
}

export function pruneHiddenAnswers(definition, answers = {}) {
  const visibleQuestionIds = getVisibleQuestionIds(definition, answers);
  return Object.fromEntries(
    Object.entries(answers).filter(([questionId]) => visibleQuestionIds.has(questionId))
  );
}

export function validateAnswer(question, answer) {
  const errors = [];

  if (question.type === "text") {
    const text = answer?.text?.trim() ?? "";
    if (question.required && text.length === 0) {
      errors.push("Esta pregunta es obligatoria.");
    }
    if (question.maxLength && text.length > question.maxLength) {
      errors.push(`La respuesta no puede exceder ${question.maxLength} caracteres.`);
    }
    return errors;
  }

  const selected = answer?.answerIds ?? [];
  const validOptionIds = new Set((question.options ?? []).map(option => option.id));

  if (selected.some(value => !validOptionIds.has(value))) {
    errors.push("La respuesta contiene una opción no válida.");
  }

  if (question.type === "singleChoice") {
    if (question.required && selected.length !== 1) {
      errors.push("Selecciona una opción.");
    } else if (selected.length > 1) {
      errors.push("Selecciona sólo una opción.");
    }
    return errors;
  }

  if (question.type === "multipleChoice") {
    const minimum = question.minSelections ?? (question.required ? 1 : 0);
    const maximum = question.maxSelections ?? Number.POSITIVE_INFINITY;

    if (selected.length < minimum) {
      errors.push(`Selecciona al menos ${minimum} opción${minimum === 1 ? "" : "es"}.`);
    }
    if (selected.length > maximum) {
      errors.push(`Selecciona como máximo ${maximum} opción${maximum === 1 ? "" : "es"}.`);
    }
    return errors;
  }

  errors.push(`Tipo de pregunta no soportado: ${question.type}`);
  return errors;
}

export function validateVisibleStep(step, answers = {}) {
  const errors = {};

  for (const question of step.questions ?? []) {
    const questionErrors = validateAnswer(question, answers[question.id]);
    if (questionErrors.length > 0) {
      errors[question.id] = questionErrors;
    }
  }

  return errors;
}

export function computeScores(definition, answers = {}) {
  const scores = Object.fromEntries((definition.dimensions ?? []).map(dimension => [dimension.id, 0]));

  for (const step of getVisibleSteps(definition, answers)) {
    for (const question of step.questions) {
      if (question.type === "text") {
        continue;
      }

      const selected = new Set(answers[question.id]?.answerIds ?? []);
      for (const option of question.options ?? []) {
        if (!selected.has(option.id)) {
          continue;
        }

        for (const [dimensionId, value] of Object.entries(option.scores ?? {})) {
          scores[dimensionId] = (scores[dimensionId] ?? 0) + value;
        }
      }
    }
  }

  return scores;
}

export function resolveResults(definition, scores) {
  return (definition.results ?? []).filter(result => {
    const score = scores[result.dimensionId] ?? 0;
    const meetsMinimum = score >= result.minScore;
    const meetsMaximum = result.maxScore == null || score <= result.maxScore;
    return meetsMinimum && meetsMaximum;
  });
}

export function buildDiagnosticResult(definition, answers = {}) {
  const normalizedAnswers = pruneHiddenAnswers(definition, answers);
  const scores = computeScores(definition, normalizedAnswers);

  return {
    diagnosticId: definition.id,
    definitionVersion: definition.version,
    answers: normalizedAnswers,
    scores,
    results: resolveResults(definition, scores)
  };
}
