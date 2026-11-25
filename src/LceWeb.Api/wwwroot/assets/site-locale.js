(() => {
  const keys = ["utm_source","utm_medium","utm_campaign","utm_term","utm_content","contact"];
  const current = new URL(window.location.href);
  const pathLanguage = current.pathname.match(/^\/(en|fr|pt)(?:\/|$)/)?.[1];
  const legacyDiagnostic = current.pathname === "/diagnostico.html" ? current.searchParams.get("lang") : null;
  const active = pathLanguage || (["en","fr","pt"].includes(legacyDiagnostic) ? legacyDiagnostic : "es");
  for (const link of document.querySelectorAll("a[data-lce-locale]")) {
    const target = new URL(link.href, current.origin);
    for (const key of keys) if (current.searchParams.has(key)) target.searchParams.set(key,current.searchParams.get(key));
    target.hash = current.hash;
    link.href = target.pathname+target.search+target.hash;
    if(link.dataset.lceLocale===active)link.setAttribute("aria-current","page");else link.removeAttribute("aria-current");
  }
  const bar = document.querySelector(".lce-language-bar");
  const label = ({es:"Idioma del sitio",en:"Website language",fr:"Langue du site",pt:"Idioma do site"})[active];
  if(bar){bar.setAttribute("aria-label",label);const span=bar.querySelector(".shell > span");if(span)span.textContent=label;}
  // Existing /diagnostico.html?lang=xx entry points keep their language when navigating away.
  if(!pathLanguage && active!=="es"){
    for(const link of document.querySelectorAll('a[href^="/"]:not([data-lce-locale])')){
      const target=new URL(link.href,current.origin);
      if(target.origin!==current.origin || /^\/(en|fr|pt)(?:\/|$)/.test(target.pathname) || !(/^\/(?:$|index\.html$|diagnostico\.html$|privacidad\.html$|servicios\/[^/]+\.html$|l\/[^/]+\.html$)/.test(target.pathname)))continue;
      target.pathname="/"+active+(target.pathname==="/index.html"?"/":target.pathname);
      link.href=target.pathname+target.search+target.hash;
    }
  }
})();
