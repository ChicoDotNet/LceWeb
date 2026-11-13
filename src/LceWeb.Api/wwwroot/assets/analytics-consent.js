const CONSENT_KEY = "lce_analytics_consent_v1";
let loadPromise = null;

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
  banner.setAttribute("aria-label", "Preferencias de analíticas");
  banner.className = "cookie-choice";
  banner.hidden = true;

  const content = document.createElement("div");
  content.className = "cookie-choice__body";
  const title = document.createElement("strong");
  title.textContent = "Analíticas opcionales";
  const description = document.createElement("p");
  description.textContent = "Usamos Microsoft Clarity únicamente con tu permiso para entender cómo se utiliza este sitio. Puedes rechazarlo sin afectar el diagnóstico ni el contacto.";
  const privacy = document.createElement("a");
  privacy.href = "/privacidad.html";
  privacy.textContent = "Información sobre privacidad";
  content.append(title, description, privacy);

  const actions = document.createElement("div");
  actions.className = "cookie-choice__actions";
  actions.append(
    createButton("Rechazar", false, () => save("rejected")),
    createButton("Permitir analíticas", true, () => save("accepted"))
  );
  banner.append(content, actions);
  document.body.append(banner);

  const settingsButton = document.createElement("button");
  settingsButton.type = "button";
  settingsButton.className = "cookie-settings";
  settingsButton.textContent = "Preferencias de analíticas";
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

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", buildConsentUi, { once: true });
} else {
  buildConsentUi();
}
