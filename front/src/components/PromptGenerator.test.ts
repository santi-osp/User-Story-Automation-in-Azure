import { describe, expect, it } from "vitest";
import { payloadToFormValues, TEMPLATE_PAYLOAD } from "../lib/template";
import { buildFinalJson, generatePromptAndJson } from "./PromptGenerator";

describe("PromptGenerator", () => {
  it("reemplaza placeholders con los campos editables nuevos", () => {
    const values = payloadToFormValues(TEMPLATE_PAYLOAD);
    values.iterationPath = "Nuevo\\Sprint 22";
    values.areaPath = "Nuevo\\Area";
    values.testSuite.planId = 777;
    values.testSuite.planName = "Plan Q2";
    values.tcCount = 3;
    values.tcState = "Design";
    values.need = "Necesito depurar cargas con trazabilidad";

    const result = generatePromptAndJson(values);

    expect(result.prompt).toContain("Actúa como analista funcional experto en Azure DevOps.");
    expect(result.prompt).toContain("```json");
    expect(result.prompt).toContain('"iterationPath": "Nuevo\\\\Sprint 22"');
    expect(result.prompt).toContain('"planId": 777');
    expect(result.prompt).toContain('"planName": "Plan Q2"');
    expect(result.prompt).toContain("# NUEVA HISTORIA DE USUARIO");
    expect(result.prompt).toContain("Necesito depurar cargas con trazabilidad");
    expect(result.finalJson.testCases).toHaveLength(3);
    expect(result.finalJson.testCases[0].state).toBe("Design");
    expect(result.finalJson.hu.description).toContain("Necesito depurar cargas con trazabilidad");
  });

  it("mantiene título y acceptance criteria del template", () => {
    const values = payloadToFormValues(TEMPLATE_PAYLOAD);
    values.need = "Necesidad nueva";
    values.tcCount = 1;
    values.tcState = "Ready";

    const finalJson = buildFinalJson(values);

    expect(finalJson.hu.title).toBe(TEMPLATE_PAYLOAD.hu.title);
    expect(finalJson.hu.acceptanceCriteria).toBe(TEMPLATE_PAYLOAD.hu.acceptanceCriteria);
  });
});
