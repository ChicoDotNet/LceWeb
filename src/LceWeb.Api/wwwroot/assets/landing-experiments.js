(() => {
  "use strict";
  const allowed = new Set(["call","whatsapp","email","form"]);
  const params = new URLSearchParams(window.location.search);
  const requested = (params.get("contact") || "form").toLowerCase();
  const contact = allowed.has(requested) ? requested : "form";
  const campaign = document.body.dataset.campaign || "lce";
  const call = "tel:+525590476846";
  const msg = "Hola, quiero revisar una operación de comercio exterior con LCE. Campaña: " + campaign;
  const destinations = {
    call: {url:call, label:"Llamar a LCE"},
    whatsapp: {url:"https://wa.me/525590476846?text="+encodeURIComponent(msg), label:"Consultar por WhatsApp"},
    email: {url:"mailto:asanchez@lce.com.mx?subject="+encodeURIComponent("Consulta LCE / "+campaign),label:"Enviar correo a LCE"},
    form: {url:document.body.dataset.formTarget || "/diagnostico.html?utm_source=landing&utm_campaign="+encodeURIComponent(campaign),label:"Hacer diagnóstico y solicitar seguimiento"}
  };
  document.querySelectorAll("[data-cta-primary]").forEach(link => {
    link.href=destinations[contact].url;
    link.textContent=destinations[contact].label+" ↗";
    link.dataset.contactVariant=contact;
    if(contact==="whatsapp"){link.target="_blank";link.rel="noopener noreferrer";}
  });
  document.querySelectorAll("[data-cta-variant]").forEach(e => {e.textContent="CTA / "+contact.toUpperCase();});
  document.querySelectorAll("[data-cta-primary]").forEach(link => link.addEventListener("click",()=>{
    if(typeof window.clarity==="function"){window.clarity("event","lce_cta_"+campaign+"_"+contact);}
  }));
})();