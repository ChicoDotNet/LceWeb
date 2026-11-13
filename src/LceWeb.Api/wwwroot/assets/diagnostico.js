const root = document.querySelector("#lce-diagnostic");
const nextStep = document.querySelector("#diagnostic-next-step");
const formRoot = document.querySelector("#lead-capture");

// The questionnaire works whether or not lead collection is enabled.
if (root && nextStep) {
  root.addEventListener("diagnostic:completed", () => {
    nextStep.hidden = false;
  });
  root.addEventListener("diagnostic:error", () => {
    nextStep.hidden = true;
  });
}

// Discover server permissions BEFORE the diagnostic engine mounts, so the
// lead:ready listener cannot miss a fast completion event.
let leadCaptureEnabled = false;
try {
  const response = await fetch("/api/public-capabilities", {
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (response.ok) {
    const capabilities = await response.json();
    leadCaptureEnabled = capabilities.leadCaptureEnabled === true;
  }
} catch {
  // Fail closed: a damaged capabilities endpoint must not expose lead form.
}

if (root && formRoot && leadCaptureEnabled) {
  try {
    const { attachLeadCapture } = await import("/js/leads/lead-capture.js");
    attachLeadCapture({ diagnosticRoot: root, formRoot });
  } catch {
    formRoot.hidden = true;
  }
}

await import("/js/diagnostics/diagnostic-runtime.js");
