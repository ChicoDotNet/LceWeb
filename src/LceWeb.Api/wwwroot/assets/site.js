(() => {
  "use strict";
  const toggle = document.querySelector(".nav-toggle");
  const nav = document.querySelector("#site-nav");
  if (toggle && nav) {
    const close = () => {
      toggle.setAttribute("aria-expanded", "false");
      toggle.setAttribute("aria-label", "Abrir menú de navegación");
      nav.classList.remove("is-open");
    };
    toggle.addEventListener("click", () => {
      const opening = toggle.getAttribute("aria-expanded") !== "true";
      toggle.setAttribute("aria-expanded", String(opening));
      toggle.setAttribute("aria-label", opening ? "Cerrar menú de navegación" : "Abrir menú de navegación");
      nav.classList.toggle("is-open", opening);
    });
    nav.querySelectorAll("a").forEach(link => link.addEventListener("click", close));
    document.addEventListener("keydown", event => {
      if (event.key === "Escape") close();
    });
  }
  const year = document.querySelector("#year");
  if (year) year.textContent = String(new Date().getFullYear());
})();