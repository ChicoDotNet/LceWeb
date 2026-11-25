import { t, normalizeLanguage } from "../diagnostics/commercial-i18n.js";

export function collectAcquisition(locationValue = window.location, referrer = document.referrer, campaign = document.body?.dataset?.campaign) {
  const params = new URLSearchParams(locationValue.search);
  return {
    utmSource: params.get("utm_source") ?? (campaign ? "landing" : null),
    utmMedium: params.get("utm_medium") ?? (campaign ? "web" : null),
    utmCampaign: params.get("utm_campaign") ?? campaign ?? null,
    utmTerm: params.get("utm_term"),
    utmContent: params.get("utm_content"),
    referrer: referrer || null,
    pageUrl: locationValue.href
  };
}

export function buildLeadSubmissionPayload(diagnosticResult, contact, acquisition, privacyNoticeVersion, privacyAccepted = false) {
  const answers = Object.entries(diagnosticResult.answers ?? {}).map(([questionId, answer]) => ({
    questionId,
    answerIds: answer.answerIds ?? [],
    text: answer.text ?? null
  }));

  const number = contact.phoneNumber?.trim() ?? "";
  return {
    diagnosticId: diagnosticResult.diagnosticId,
    definitionVersion: diagnosticResult.definitionVersion,
    ...(diagnosticResult.definitionVersion >= 2 ? { language: normalizeLanguage(diagnosticResult.language) } : {}),
    privacyNoticeVersion,
    privacyAccepted,
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
  endpoint = "/api/leads",
  privacyNoticeVersion,
  attachmentsEnabled = false,
  language = "es"
}) {
  if (!diagnosticRoot || !formRoot) {
    throw new Error("diagnosticRoot and formRoot are required.");
  }
  if (!privacyNoticeVersion || !/^[A-Za-z0-9._-]{1,64}$/.test(privacyNoticeVersion)) {
    throw new Error("An approved privacy notice revision is required to capture leads.");
  }

  let diagnosticResult = null;
  language = normalizeLanguage(language);
  const tr = key => t(key, language);
  const privacyHref = (language === "es" ? "" : "/" + language) + "/privacidad.html";

  formRoot.hidden = true;
  formRoot.innerHTML = `
    <form class="lce-lead clarity-mask" data-clarity-mask novalidate>
      <h2 class="lce-lead__title">${tr("formTitle")}</h2>
      <p class="lce-lead__intro">${tr("formIntro")}</p>

      <label class="lce-lead__field">
        <span>${tr("name")}</span>
        <input name="name" autocomplete="name" maxlength="200" required>
      </label>

      <label class="lce-lead__field">
        <span>${tr("email")}</span>
        <input name="email" type="email" autocomplete="email" maxlength="320" required>
      </label>

      <div class="lce-lead__phone">
        <label class="lce-lead__field lce-lead__calling-code">
          <span>${tr("code")}</span>
          <input name="callingCode" inputmode="tel" autocomplete="tel-country-code" value="+52" maxlength="5">
        </label>
        <label class="lce-lead__field">
          <span>${tr("phoneOptional")}</span>
          <input name="phoneNumber" inputmode="tel" autocomplete="tel-national" maxlength="24">
        </label>
      </div>

      <label class="lce-lead__field" data-attachment-field hidden>
        <span>${tr("attachments")}</span>
        <input type="file" name="attachments" accept=".pdf,.jpg,.jpeg,.png" multiple disabled>
      </label>

      <label class="lce-lead__honeypot" aria-hidden="true">
        <span>Sitio web</span>
        <input name="website" tabindex="-1" autocomplete="off">
      </label>

      <label class="lce-lead__privacy">
        <input name="privacyAgreement" type="checkbox" required>
        <span>${tr("privacyBefore")}<a href="${privacyHref}" target="_blank" rel="noopener noreferrer">${tr("privacyLink")}</a>${tr("privacyAfter")}</span>
      </label>
      <p class="lce-lead__caution">${tr("caution")}</p>

      <div class="lce-lead__errors" role="alert" hidden></div>
      <button class="lce-lead__submit" type="submit">${tr("send")}</button>
      <p class="lce-lead__status" role="status" aria-live="polite"></p>
    </form>`;

  const form = formRoot.querySelector("form");
  const revisionText = document.createElement("small");
  revisionText.className = "lce-lead__revision";
  revisionText.textContent = `${tr("revision")} ${privacyNoticeVersion}`;
  form.querySelector(".lce-lead__privacy").append(revisionText);
  const errorsElement = formRoot.querySelector(".lce-lead__errors");
  const statusElement = formRoot.querySelector(".lce-lead__status");
  const submitButton = formRoot.querySelector(".lce-lead__submit");

  diagnosticRoot.addEventListener("diagnostic:completed", event => {
    diagnosticResult = event.detail;
    const commercial = diagnosticResult.definitionVersion >= 2;
    form.elements.phoneNumber.required = commercial;
    form.elements.callingCode.required = commercial;
    form.elements.phoneNumber.closest("label").querySelector("span").textContent = commercial ? tr("phone") : tr("phoneOptional");
    if (commercial) submitButton.textContent = tr("submit");
    const canAttach = commercial && attachmentsEnabled && diagnosticResult.answers?.technical?.answerIds?.includes("available");
    form.querySelector("[data-attachment-field]").hidden = !canAttach;
    form.elements.attachments.disabled = !canAttach;
    if (!canAttach) form.elements.attachments.value = "";
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
      statusElement.textContent = tr("completeFirst");
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
      collectAcquisition(),
      privacyNoticeVersion,
      data.get("privacyAgreement") === "on");

    submitButton.disabled = true;
    statusElement.textContent = tr("sending");

    try {
      const files = form.elements.attachments.disabled ? [] : [...form.elements.attachments.files];
      if (files.length > 3 || files.some(f => f.size <= 0 || f.size > 10 * 1024 * 1024)) {
        statusElement.textContent = tr("fileLimit");
        return;
      }
      const multipart = new FormData();
      if (files.length) {
        multipart.append("payload", JSON.stringify(payload));
        for (const file of files) multipart.append("attachments", file);
      }
      const response = await fetch(endpoint, {
        method: "POST",
        ...(files.length ? { body: multipart } : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) })
      });

      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (body.errors) {
          const list = document.createElement("ul");
          for (const messages of Object.values(body.errors)) {
            for (const message of messages) {
              const item = document.createElement("li");
              item.textContent = language === "es" ? message : tr("invalidContact");
              list.append(item);
            }
          }
          errorsElement.replaceChildren(list);
          errorsElement.hidden = false;
        }

        throw new Error(body.detail ?? body.title ?? `Lead submission failed with HTTP ${response.status}.`);
      }

      statusElement.textContent = diagnosticResult.definitionVersion >= 2
        ? tr("success")
        : "Gracias. Tu diagnóstico fue enviado correctamente.";
      form.reset();
      for (const field of form.querySelectorAll("input")) field.disabled = true;
      form.classList.add("lce-lead--submitted");
      statusElement.setAttribute("tabindex", "-1");
      statusElement.focus();
      formRoot.dispatchEvent(new CustomEvent("lead:submitted", {
        bubbles: true,
        detail: body
      }));
      submitButton.hidden = true;
    } catch (error) {
      statusElement.textContent = tr("failure");
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
