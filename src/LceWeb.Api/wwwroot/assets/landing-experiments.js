(() => {
  "use strict";
  const allowed = new Set(["call","whatsapp","email","form"]);
  const params = new URLSearchParams(window.location.search);
  const requested = (params.get("contact") || "form").toLowerCase();
  const contact = allowed.has(requested) ? requested : "form";
  const variants = {call:"01",whatsapp:"02",email:"03",form:"04"};
  const campaign = document.body.dataset.campaign || "lce";
  const lang = document.documentElement?.lang?.split("-")[0] || "es";
  const locale = ["en","fr","pt"].includes(lang) ? lang : "es";
  const translations = {
    es: ["Hola, me gustaría recibir orientación de LCE sobre una operación de comercio exterior.", "Llamar a LCE", "Consultar por WhatsApp", "Solicitud de orientación en comercio exterior", "Enviar correo a LCE", "Hacer diagnóstico y solicitar seguimiento", "Atención telefónica", "Atención por WhatsApp", "Escríbenos por correo", "Solicitar orientación"],
    en: ["Hello, I would like guidance from LCE about a foreign trade operation.", "Call LCE", "Contact via WhatsApp", "Foreign trade guidance request", "Email LCE", "Complete the assessment and request follow-up", "Phone assistance", "WhatsApp assistance", "Email us", "Request guidance"],
    fr: ["Bonjour, je souhaiterais un accompagnement de LCE pour une opération de commerce extérieur.", "Appeler LCE", "Contacter via WhatsApp", "Demande d’accompagnement en commerce extérieur", "Écrire à LCE", "Faire le diagnostic et demander un suivi", "Assistance téléphonique", "Assistance WhatsApp", "Écrivez-nous", "Demander un accompagnement"],
    pt: ["Olá, gostaria de receber orientação da LCE sobre uma operação de comércio exterior.", "Ligar para a LCE", "Consultar pelo WhatsApp", "Solicitação de orientação em comércio exterior", "Enviar e-mail à LCE", "Fazer diagnóstico e solicitar acompanhamento", "Atendimento telefônico", "Atendimento por WhatsApp", "Escreva-nos por e-mail", "Solicitar orientação"]
  };
  const copy = translations[locale];
  const prefix = locale === "es" ? "" : "/" + locale;
  const formTarget = document.body.dataset.formTarget || prefix + "/diagnostico.html?utm_source=landing&utm_campaign=" + encodeURIComponent(campaign);
  // Carry ad attribution into the central diagnostic, without forwarding unrelated URL values.
  let formUrl = formTarget;
  if(formTarget.startsWith("/")){
    const [path,query=""] = formTarget.split("?",2);
    const targetParams = new URLSearchParams(query);
    for(const key of ["utm_source","utm_medium","utm_campaign","utm_term","utm_content"])if(params.has(key))targetParams.set(key,params.get(key));
    formUrl = path + "?" + targetParams.toString();
  }
  const call = "tel:+525590476846";
  const msg = copy[0];
  const destinations = {
    call: {url:call, label:copy[1]},
    whatsapp: {url:"https://wa.me/525590476846?text="+encodeURIComponent(msg), label:copy[2]},
    email: {url:"mailto:contacto@lce.com.mx?subject="+encodeURIComponent(copy[3]),label:copy[4]},
    form: {url:formUrl,label:copy[5]}
  };
  document.querySelectorAll("[data-cta-primary]").forEach(link => {
    link.href=destinations[contact].url;
    link.textContent=destinations[contact].label+" ↗";
    link.dataset.contactVariant=contact;
    link.dataset.experimentVariant=(campaign+"-"+variants[contact]);
    if(contact==="whatsapp"){link.target="_blank";link.rel="noopener noreferrer";}
  });
  const channelText = {call:copy[6],whatsapp:copy[7],email:copy[8],form:copy[9]};
  document.querySelectorAll("[data-cta-variant]").forEach(e => {e.textContent=channelText[contact];});
  document.querySelectorAll("[data-cta-primary]").forEach(link => link.addEventListener("click",()=>{
    if(typeof window.clarity==="function"){window.clarity("event","lce_cta_"+campaign+"_"+contact);}
  }));
})();