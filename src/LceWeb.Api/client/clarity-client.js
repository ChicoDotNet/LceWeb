import Clarity from "@microsoft/clarity";

let initialized = false;

/**
 * Start only after the visitor actively opted in.
 * No user IDs, names, email or diagnostic answers are sent to Clarity.
 */
export function activateClarity() {
  if (!initialized) {
    Clarity.init("zd6brtj9wu");
    initialized = true;
  }
  // Consent Mode V2; analytics only, advertising intentionally denied.
  if (typeof window.clarity === "function") {
    window.clarity("consentv2", {
      ad_Storage: "denied",
      analytics_Storage: "granted"
    });
  }
}
