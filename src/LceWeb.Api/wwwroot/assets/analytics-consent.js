const CONSENT_KEY = "lce_analytics_consent_v1";
let loadPromise = null;
const prefix = window.location.pathname.match(/^\/(en|fr|pt)(?:\/|$)/)?.[1];
const requestedLanguage = new URLSearchParams(window.location.search).get("lang");
const language = prefix ?? (["en","fr","pt"].includes(requestedLanguage) && window.location.pathname === "/diagnostico.html" ? requestedLanguage : "es");
const analyticsCopy = {
  es: ["Preferencias de analíticas", "Analíticas opcionales", "Usamos Microsoft Clarity únicamente con tu permiso para entender cómo se utiliza este sitio. Puedes rechazarlo sin afectar el diagnóstico ni el contacto.", "Información sobre privacidad", "Rechazar", "Permitir analíticas"],
  en: ["Analytics preferences", "Optional analytics", "We use Microsoft Clarity only with your permission to understand how this site is used. You may reject it without affecting the assessment or contact.", "Privacy information", "Reject", "Allow analytics"],
  fr: ["Préférences de statistiques", "Statistiques facultatives", "Nous utilisons Microsoft Clarity uniquement avec votre autorisation pour comprendre l’utilisation de ce site. Vous pouvez refuser sans affecter le diagnostic ni le contact.", "Informations de confidentialité", "Refuser", "Autoriser les statistiques"],
  pt: ["Preferências de análises", "Análises opcionais", "Usamos o Microsoft Clarity somente com sua autorização para entender como este site é utilizado. Você pode rejeitar sem afetar o diagnóstico ou o contato.", "Informações de privacidade", "Rejeitar", "Permitir análises"]
};
const copy = analyticsCopy[language];


function readPreference() {
  try {
    const value = window.localStorage.getItem(CONSENT_KEY);
    return value === "accepted" || value === "rejected" ? value : null;
  } catch {
    return null;
  }
}

function writePreference(value) {
  try {
    window.localStorage.setItem(CONSENT_KEY, value);
    return true;
  } catch {
    return false;
  }
}

async function enableAnalytics() {
  // Clarity code is not requested from the server at all without affirmative consent.
  loadPromise ??= import("/assets/clarity-bundle.js")
    .then(module => module.activateClarity())
    .catch(() => { loadPromise = null; });
  return loadPromise;
}

function createButton(copy, primary, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = copy;
  button.className = primary ? "cookie-button cookie-button--primary" : "cookie-button";
  button.addEventListener("click", onClick);
  return button;
}

function buildConsentUi() {
  const banner = document.createElement("aside");
  banner.id = "lce-cookie-choice";
  banner.setAttribute("aria-label", copy[0]);
  banner.className = "cookie-choice";
  banner.hidden = true;

  const content = document.createElement("div");
  content.className = "cookie-choice__body";
  const title = document.createElement("strong");
  title.textContent = copy[1];
  const description = document.createElement("p");
  description.textContent = copy[2];
  const privacy = document.createElement("a");
  privacy.href = (language === "es" ? "" : "/" + language) + "/privacidad.html";
  privacy.textContent = copy[3];
  content.append(title, description, privacy);

  const actions = document.createElement("div");
  actions.className = "cookie-choice__actions";
  actions.append(
    createButton(copy[4], false, () => save("rejected")),
    createButton(copy[5], true, () => save("accepted"))
  );
  banner.append(content, actions);
  document.body.append(banner);

  const settingsButton = document.createElement("button");
  settingsButton.type = "button";
  settingsButton.className = "cookie-settings";
  settingsButton.textContent = copy[0];
  settingsButton.addEventListener("click", () => {
    banner.hidden = false;
    actions.querySelector("button")?.focus();
  });
  document.body.append(settingsButton);

  function save(value) {
    const previous = readPreference();
    const saved = writePreference(value);
    banner.hidden = true;
    if (value === "accepted") {
      void enableAnalytics();
    } else if (previous === "accepted" && typeof window.clarity === "function") {
      // Stop storing cookies immediately, then unload the script via a page reload.
      window.clarity("consentv2", { ad_Storage: "denied", analytics_Storage: "denied" });
      window.clarity("consent", false);
      if (saved) window.location.reload();
    }
  }

  const stored = readPreference();
  if (stored === "accepted") {
    void enableAnalytics();
  } else if (stored === null) {
    banner.hidden = false;
  }
}

// Only fixed event names, no diagnostic responses, scores or contact details.
for (const [eventName, analyticsName] of [["diagnostic:completed", "lce_diagnostic_completed"], ["lead:submitted", "lce_lead_submitted"]]) {
  document.addEventListener(eventName, () => {
    if (readPreference() === "accepted" && typeof window.clarity === "function") window.clarity("event", analyticsName);
  });
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", buildConsentUi, { once: true });
} else {
  buildConsentUi();
}
