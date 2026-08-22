import {
  buildDiagnosticResult,
  getVisibleSteps,
  pruneHiddenAnswers,
  validateVisibleStep
} from "./diagnostic-core.js";

function createElement(tagName, className, text) {
  const node = document.createElement(tagName);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

function buildApiUrl(baseUrl, diagnosticId) {
  const normalizedBase = (baseUrl ?? "").replace(/\/$/, "");
  return `${normalizedBase}/api/diagnostics/${encodeURIComponent(diagnosticId)}`;
}

function emit(root, name, detail) {
  root.dispatchEvent(new CustomEvent(name, { bubbles: true, detail }));
}

class DiagnosticRuntime {
  constructor(root, definition) {
    this.root = root;
    this.definition = definition;
    this.answers = {};
    this.stepIndex = 0;
    this.errors = {};
    this.showScores = root.dataset.diagnosticShowScores === "true";
  }

  renderIntro() {
    this.root.replaceChildren();
    const section = createElement("section", "lce-diagnostic lce-diagnostic--intro");
    section.append(createElement("h2", "lce-diagnostic__title", this.definition.copy.introTitle));
    if (this.definition.copy.introBody) {
      section.append(createElement("p", "lce-diagnostic__body", this.definition.copy.introBody));
    }

    const start = createElement("button", "lce-diagnostic__button lce-diagnostic__button--primary", "Comenzar");
    start.type = "button";
    start.addEventListener("click", () => this.renderStep());
    section.append(start);
    this.root.append(section);
  }

  renderStep() {
    const visibleSteps = getVisibleSteps(this.definition, this.answers);
    if (visibleSteps.length === 0) return this.complete();

    this.stepIndex = Math.max(0, Math.min(this.stepIndex, visibleSteps.length - 1));
    const step = visibleSteps[this.stepIndex];
    this.root.replaceChildren();

    const section = createElement("section", "lce-diagnostic lce-diagnostic--step");
    section.dataset.stepId = step.id;
    section.append(createElement("p", "lce-diagnostic__progress", `Paso ${this.stepIndex + 1} de ${visibleSteps.length}`));
    section.append(createElement("h2", "lce-diagnostic__title", step.title));
    if (step.description) section.append(createElement("p", "lce-diagnostic__body", step.description));

    const form = createElement("form", "lce-diagnostic__form");
    form.noValidate = true;
    for (const question of step.questions) form.append(this.renderQuestion(question));
    form.addEventListener("submit", event => {
      event.preventDefault();
      this.next();
    });

    const actions = createElement("div", "lce-diagnostic__actions");
    if (this.stepIndex > 0) {
      const previous = createElement("button", "lce-diagnostic__button lce-diagnostic__button--secondary", "Anterior");
      previous.type = "button";
      previous.addEventListener("click", () => {
        this.errors = {};
        this.stepIndex -= 1;
        this.renderStep();
      });
      actions.append(previous);
    }

    const next = createElement(
      "button",
      "lce-diagnostic__button lce-diagnostic__button--primary",
      this.stepIndex === visibleSteps.length - 1 ? "Ver resultado" : "Continuar"
    );
    next.type = "submit";
    actions.append(next);
    form.append(actions);
    section.append(form);
    this.root.append(section);
  }

  renderQuestion(question) {
    const fieldset = createElement("fieldset", "lce-diagnostic__question");
    fieldset.dataset.questionId = question.id;
    fieldset.append(createElement("legend", "lce-diagnostic__prompt", question.prompt));
    if (question.helpText) fieldset.append(createElement("p", "lce-diagnostic__help", question.helpText));

    if (question.type === "text") {
      const textarea = createElement("textarea", "lce-diagnostic__text");
      textarea.name = question.id;
      textarea.value = this.answers[question.id]?.text ?? "";
      textarea.required = Boolean(question.required);
      if (question.maxLength) textarea.maxLength = question.maxLength;
      textarea.addEventListener("input", event => {
        this.answers[question.id] = { text: event.currentTarget.value };
        delete this.errors[question.id];
        this.emitAnswer(question.id);
      });
      fieldset.append(textarea);
    } else {
      const options = createElement("div", "lce-diagnostic__options");
      const selected = new Set(this.answers[question.id]?.answerIds ?? []);
      const inputType = question.type === "singleChoice" ? "radio" : "checkbox";

      for (const option of [...(question.options ?? [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))) {
        const label = createElement("label", "lce-diagnostic__option");
        const input = document.createElement("input");
        input.type = inputType;
        input.name = question.id;
        input.value = option.id;
        input.checked = selected.has(option.id);

        input.addEventListener("change", () => {
          if (question.type === "singleChoice") {
            this.answers[question.id] = { answerIds: [option.id] };
          } else {
            const current = new Set(this.answers[question.id]?.answerIds ?? []);
            input.checked ? current.add(option.id) : current.delete(option.id);
            this.answers[question.id] = { answerIds: [...current] };
          }

          this.answers = pruneHiddenAnswers(this.definition, this.answers);
          delete this.errors[question.id];
          this.emitAnswer(question.id);
          this.renderStep();
        });

        label.append(input);
        const copy = createElement("span", "lce-diagnostic__option-copy");
        copy.append(createElement("span", "lce-diagnostic__option-label", option.label));
        if (option.description) copy.append(createElement("span", "lce-diagnostic__option-description", option.description));
        label.append(copy);
        options.append(label);
      }
      fieldset.append(options);
    }

    const questionErrors = this.errors[question.id] ?? [];
    if (questionErrors.length > 0) {
      const list = createElement("ul", "lce-diagnostic__errors");
      list.setAttribute("role", "alert");
      for (const error of questionErrors) list.append(createElement("li", "lce-diagnostic__error", error));
      fieldset.append(list);
    }

    return fieldset;
  }

  emitAnswer(questionId) {
    emit(this.root, "diagnostic:answer", {
      diagnosticId: this.definition.id,
      definitionVersion: this.definition.version,
      questionId,
      answer: this.answers[questionId] ?? null
    });
  }

  next() {
    const visibleSteps = getVisibleSteps(this.definition, this.answers);
    const step = visibleSteps[this.stepIndex];
    this.errors = validateVisibleStep(step, this.answers);
    if (Object.keys(this.errors).length > 0) {
      this.renderStep();
      return;
    }

    if (this.stepIndex >= visibleSteps.length - 1) return this.complete();
    this.stepIndex += 1;
    this.errors = {};
    this.renderStep();
  }

  complete() {
    const result = buildDiagnosticResult(this.definition, this.answers);
    this.answers = result.answers;
    this.root.replaceChildren();

    const section = createElement("section", "lce-diagnostic lce-diagnostic--complete");
    section.append(createElement("h2", "lce-diagnostic__title", this.definition.copy.completionTitle));
    if (this.definition.copy.completionBody) {
      section.append(createElement("p", "lce-diagnostic__body", this.definition.copy.completionBody));
    }

    if (result.results.length > 0) {
      const resultList = createElement("div", "lce-diagnostic__results");
      const dimensions = new Map((this.definition.dimensions ?? []).map(dimension => [dimension.id, dimension]));
      for (const band of result.results) {
        const card = createElement("article", "lce-diagnostic__result");
        const dimension = dimensions.get(band.dimensionId);
        if (dimension) card.append(createElement("p", "lce-diagnostic__result-dimension", dimension.label));
        card.append(createElement("h3", "lce-diagnostic__result-label", band.label));
        card.append(createElement("p", "lce-diagnostic__result-summary", band.summary));
        if (this.showScores) {
          card.append(createElement("p", "lce-diagnostic__result-score", `Puntaje: ${result.scores[band.dimensionId] ?? 0}`));
        }
        resultList.append(card);
      }
      section.append(resultList);
    }

    this.root.append(section);
    emit(this.root, "diagnostic:completed", result);
  }
}

export async function mountDiagnostic(root, options = {}) {
  if (!(root instanceof Element)) throw new TypeError("root must be a DOM Element.");

  const diagnosticId = options.diagnosticId ?? root.dataset.diagnosticId;
  if (!diagnosticId) throw new Error("A diagnostic GUID is required through options or data-diagnostic-id.");

  const baseUrl = options.apiBaseUrl ?? root.dataset.diagnosticApiBase ?? "";
  root.setAttribute("aria-live", "polite");
  root.replaceChildren(createElement("p", "lce-diagnostic__status", "Cargando diagnóstico…"));

  try {
    const response = await fetch(buildApiUrl(baseUrl, diagnosticId), { headers: { Accept: "application/json" } });
    if (!response.ok) throw new Error(`No se pudo cargar el diagnóstico (${response.status}).`);

    const definition = await response.json();
    const runtime = new DiagnosticRuntime(root, definition);
    runtime.renderIntro();
    emit(root, "diagnostic:loaded", {
      diagnosticId: definition.id,
      definitionVersion: definition.version,
      name: definition.name
    });
    return runtime;
  } catch (error) {
    root.replaceChildren(createElement("p", "lce-diagnostic__status lce-diagnostic__status--error", "No pudimos cargar el diagnóstico. Intenta nuevamente."));
    emit(root, "diagnostic:error", {
      diagnosticId,
      message: error instanceof Error ? error.message : String(error)
    });
    throw error;
  }
}

export function autoMountDiagnostics(scope = document) {
  return Promise.allSettled([...scope.querySelectorAll("[data-diagnostic-id]")].map(root => mountDiagnostic(root)));
}

if (typeof document !== "undefined") {
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => void autoMountDiagnostics());
  } else {
    void autoMountDiagnostics();
  }
}
