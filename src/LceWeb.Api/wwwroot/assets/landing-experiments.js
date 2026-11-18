(() => {
  "use strict";
  const allowed = new Set(["call","whatsapp","email","form"]);
  const params = new URLSearchParams(window.location.search);
  const requested = (params.get("contact") || "form").toLowerCase();
  const contact = allowed.has(requested) ? requested : "form";
  const variants = {call:"01",whatsapp:"02",email:"03",form:"04"};
  const campaign = document.body.dataset.campaign || "lce";
  const call = "tel:+525590476846";
  const msg = "Hola, me gustaría recibir orientación de LCE sobre una operación de comercio exterior.";
  const destinations = {
    call: {url:call, label:"Llamar a LCE"},
    whatsapp: {url:"https://wa.me/525590476846?text="+encodeURIComponent(msg), label:"Consultar por WhatsApp"},
    email: {url:"mailto:asanchez@lce.com.mx?subject="+encodeURIComponent("Solicitud de orientación en comercio exterior"),label:"Enviar correo a LCE"},
    form: {url:document.body.dataset.formTarget || "/diagnostico.html?utm_source=landing&utm_campaign="+encodeURIComponent(campaign),label:"Hacer diagnóstico y solicitar seguimiento"}
  };
  document.querySelectorAll("[data-cta-primary]").forEach(link => {
    link.href=destinations[contact].url;
    link.textContent=destinations[contact].label+" ↗";
    link.dataset.contactVariant=contact;
    link.dataset.experimentVariant=(campaign+"-"+variants[contact]);
    if(contact==="whatsapp"){link.target="_blank";link.rel="noopener noreferrer";}
  });
  const channelText = {call:"Atención telefónica",whatsapp:"Atención por WhatsApp",email:"Escríbenos por correo",form:"Solicitar orientación"};
  document.querySelectorAll("[data-cta-variant]").forEach(e => {e.textContent=channelText[contact];});
  document.querySelectorAll("[data-cta-primary]").forEach(link => link.addEventListener("click",()=>{
    if(typeof window.clarity==="function"){window.clarity("event","lce_cta_"+campaign+"_"+contact);}
  }));
})();