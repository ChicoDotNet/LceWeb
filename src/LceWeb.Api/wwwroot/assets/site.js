(() => {
  "use strict";
  const lang = document.documentElement.lang.split("-")[0];
  const menuCopy = ({en:["Open navigation menu","Close navigation menu"],fr:["Ouvrir le menu de navigation","Fermer le menu de navigation"],pt:["Abrir menu de navegação","Fechar menu de navegação"]})[lang] || ["Abrir menú de navegación","Cerrar menú de navegación"];
  const toggle = document.querySelector(".nav-toggle");
  const nav = document.querySelector("#site-nav");
  if (toggle && nav) {
    const close = () => {
      toggle.setAttribute("aria-expanded", "false");
      toggle.setAttribute("aria-label", menuCopy[0]);
      nav.classList.remove("is-open");
    };
    toggle.addEventListener("click", () => {
      const opening = toggle.getAttribute("aria-expanded") !== "true";
      toggle.setAttribute("aria-expanded", String(opening));
      toggle.setAttribute("aria-label", opening ? menuCopy[1] : menuCopy[0]);
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