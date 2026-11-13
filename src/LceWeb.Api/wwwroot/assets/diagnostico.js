import "/js/diagnostics/diagnostic-runtime.js";

const root = document.querySelector("#lce-diagnostic");
const nextStep = document.querySelector("#diagnostic-next-step");

if (root && nextStep) {
  root.addEventListener("diagnostic:completed", () => {
    nextStep.hidden = false;
  });
  root.addEventListener("diagnostic:error", () => {
    nextStep.hidden = true;
  });
}

// Las respuestas permanecen dentro del runtime de la página; no se
// construyen payloads de leads ni se realiza POST con datos personales.
