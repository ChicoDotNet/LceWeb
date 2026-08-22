function collectAcquisition(locationValue = window.location, referrer = document.referrer) {
  const params = new URLSearchParams(locationValue.search);
  return {
    utmSource: params.get("utm_source"),
    utmMedium: params.get("utm_medium"),
    utmCampaign: params.get("utm_campaign"),
    utmTerm: params.get("utm_term"),
    utmContent: params.get("utm_content"),
    referrer: referrer || null,
    pageUrl: locationValue.href
  };
}

export function buildLeadSubmissionPayload(diagnosticResult, contact, acquisition) {
  const answers = Object.entries(diagnosticResult.answers ?? {}).map(([questionId, answer]) => ({
    questionId,
    answerIds: answer.answerIds ?? [],
    text: answer.text ?? null
  }));

  const number = contact.phoneNumber?.trim() ?? "";
  return {
    diagnosticId: diagnosticResult.diagnosticId,
    definitionVersion: diagnosticResult.definitionVersion,
    contact: {
      name: contact.name.trim(),
      email: contact.email.trim(),
      phone: number
        ? {
            callingCode: contact.callingCode.trim(),
            number
          }
        : null
    },
    answers,
    acquisition,
    website: contact.website ?? ""
  };
}

export function attachLeadCapture({
  diagnosticRoot,
  formRoot,
  endpoint = "/api/leads"
}) {
  if (!diagnosticRoot || !formRoot) {
    throw new Error("diagnosticRoot and formRoot are required.");
  }

  let diagnosticResult = null;

  formRoot.hidden = true;
  formRoot.innerHTML = `
    <form class="lce-lead" novalidate>
      <h2 class="lce-lead__title">Recibe una revisión de tu diagnóstico</h2>
      <p class="lce-lead__intro">Comparte tus datos para que podamos dar seguimiento con el contexto que acabas de responder.</p>

      <label class="lce-lead__field">
        <span>Nombre *</span>
        <input name="name" autocomplete="name" maxlength="200" required>
      </label>

      <label class="lce-lead__field">
        <span>Correo electrónico *</span>
        <input name="email" type="email" autocomplete="email" maxlength="320" required>
      </label>

      <div class="lce-lead__phone">
        <label class="lce-lead__field lce-lead__calling-code">
          <span>Clave LD</span>
          <input name="callingCode" inputmode="tel" autocomplete="tel-country-code" value="+52" maxlength="5">
        </label>
        <label class="lce-lead__field">
          <span>Teléfono (opcional)</span>
          <input name="phoneNumber" inputmode="tel" autocomplete="tel-national" maxlength="24">
        </label>
      </div>

      <label class="lce-lead__honeypot" aria-hidden="true">
        <span>Sitio web</span>
        <input name="website" tabindex="-1" autocomplete="off">
      </label>

      <div class="lce-lead__errors" role="alert" hidden></div>
      <button class="lce-lead__submit" type="submit">Enviar diagnóstico</button>
      <p class="lce-lead__status" role="status" aria-live="polite"></p>
    </form>`;

  const form = formRoot.querySelector("form");
  const errorsElement = formRoot.querySelector(".lce-lead__errors");
  const statusElement = formRoot.querySelector(".lce-lead__status");
  const submitButton = formRoot.querySelector(".lce-lead__submit");

  diagnosticRoot.addEventListener("diagnostic:completed", event => {
    diagnosticResult = event.detail;
    formRoot.hidden = false;
    formRoot.dispatchEvent(new CustomEvent("lead:ready", {
      bubbles: true,
      detail: diagnosticResult
    }));
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    errorsElement.hidden = true;
    errorsElement.replaceChildren();
    statusElement.textContent = "";

    if (!diagnosticResult) {
      statusElement.textContent = "Completa primero el diagnóstico.";
      return;
    }

    if (!form.reportValidity()) {
      return;
    }

    const data = new FormData(form);
    const payload = buildLeadSubmissionPayload(
      diagnosticResult,
      {
        name: String(data.get("name") ?? ""),
        email: String(data.get("email") ?? ""),
        callingCode: String(data.get("callingCode") ?? ""),
        phoneNumber: String(data.get("phoneNumber") ?? ""),
        website: String(data.get("website") ?? "")
      },
      collectAcquisition());

    submitButton.disabled = true;
    statusElement.textContent = "Enviando…";

    try {
      const response = await fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (body.errors) {
          const list = document.createElement("ul");
          for (const messages of Object.values(body.errors)) {
            for (const message of messages) {
              const item = document.createElement("li");
              item.textContent = message;
              list.append(item);
            }
          }
          errorsElement.replaceChildren(list);
          errorsElement.hidden = false;
        }

        throw new Error(body.detail ?? body.title ?? `Lead submission failed with HTTP ${response.status}.`);
      }

      statusElement.textContent = "Gracias. Tu diagnóstico fue enviado correctamente.";
      formRoot.dispatchEvent(new CustomEvent("lead:submitted", {
        bubbles: true,
        detail: body
      }));
      submitButton.hidden = true;
    } catch (error) {
      statusElement.textContent = "No pudimos enviar la información. Intenta nuevamente.";
      formRoot.dispatchEvent(new CustomEvent("lead:error", {
        bubbles: true,
        detail: { error }
      }));
    } finally {
      submitButton.disabled = false;
    }
  });

  return { form };
}
