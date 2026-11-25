import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { languages, localizeDefinition, normalizeLanguage, t } from "../../src/LceWeb.Api/wwwroot/js/diagnostics/commercial-i18n.js";
const d = JSON.parse(readFileSync(new URL("../../src/LceWeb.Api/diagnostics/16f5812b-6a27-44f6-b5b4-557a720a6425.v2.json",import.meta.url)));
test("translations preserve IDs, question rules and scoring across all four languages",()=>{
  for(const language of languages){
    const v=localizeDefinition(d,language);
    assert.equal(v.steps.length,5);
    for(let i=0;i<5;i++)for(let j=0;j<d.steps[i].questions.length;j++){
      const a=d.steps[i].questions[j],b=v.steps[i].questions[j];
      assert.equal(a.id,b.id);
      assert.deepEqual(a.options.map(o=>[o.id,o.scores]),b.options.map(o=>[o.id,o.scores]));
      if(language!=="es") assert.notEqual(a.prompt,b.prompt);
    }
    assert.ok(t("success",language).length>10);
  }
  assert.equal(normalizeLanguage("de"),"es");
});
