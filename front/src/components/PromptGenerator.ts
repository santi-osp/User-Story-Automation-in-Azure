import {
  PROMPT_TEMPLATE_WITH_PLACEHOLDERS,
  TEMPLATE_PAYLOAD
} from "../lib/template";
import type { FormValues, HuPayload, PromptResult } from "../types";

function deepCloneTemplate(): HuPayload {
  return JSON.parse(JSON.stringify(TEMPLATE_PAYLOAD)) as HuPayload;
}

function escapeHtml(raw: string): string {
  return raw
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function applyNeedToDescription(currentDescription: string, need: string): string {
  if (!need.trim()) {
    return currentDescription;
  }

  const escaped = escapeHtml(need.trim());
  const block = `<p><strong>Necesito:</strong> ${escaped}</p>`;

  if (currentDescription.includes("<p><strong>Necesito:</strong>")) {
    return currentDescription.replace(
      /<p><strong>Necesito:<\/strong>.*?<\/p>/,
      block
    );
  }

  return `${currentDescription}${block}`;
}

function normalizeState(state: FormValues["tcState"]): "Ready" | "Design" | "Closed" {
  if (state === "Desing") {
    return "Design";
  }
  return state;
}

function buildTestCases(values: FormValues): HuPayload["testCases"] {
  const count = Math.max(1, Math.min(50, Number(values.tcCount) || 1));
  const templateCases = TEMPLATE_PAYLOAD.testCases;
  const forcedState = normalizeState(values.tcState);

  const generated: HuPayload["testCases"] = [];

  for (let i = 0; i < count; i += 1) {
    const source = templateCases[i % templateCases.length];
    const titleSuffix = source.title.includes("—")
      ? source.title.split("—").slice(1).join("—").trim()
      : source.title;

    generated.push({
      title: `TC-${String(i + 1).padStart(3, "0")} — ${titleSuffix}`,
      action: source.action,
      expected: source.expected,
      state: forcedState
    });
  }

  return generated;
}

export function buildFinalJson(values: FormValues): HuPayload {
  const finalPayload = deepCloneTemplate();

  finalPayload.iterationPath = values.iterationPath;
  finalPayload.areaPath = values.areaPath;

  finalPayload.hu.priority = Number(values.hu.priority);
  finalPayload.hu.risk = values.hu.risk;
  finalPayload.hu.startDate = values.hu.startDate;
  finalPayload.hu.finishDate = values.hu.finishDate;
  finalPayload.hu.valueArea = values.hu.valueArea;
  finalPayload.hu.tipoHU = values.hu.tipoHU;
  finalPayload.hu.frenteDeTrabajo = values.hu.frenteDeTrabajo;
  finalPayload.hu.assignedTo = values.hu.assignedTo;
  finalPayload.hu.description = applyNeedToDescription(
    finalPayload.hu.description,
    values.need
  );

  finalPayload.testSuite.planId = Number(values.testSuite.planId);
  finalPayload.testSuite.planName = values.testSuite.planName;
  finalPayload.testCases = buildTestCases(values);

  return finalPayload;
}

export function generatePrompt(
  template: string,
  payload: HuPayload,
  need: string
): string {
  const prettyJson = JSON.stringify(payload, null, 2);
  return template
    .replace("{{jsonTemplate}}", prettyJson)
    .replace("{{need}}", need.trim() || "Necesidad.");
}

export function generatePromptAndJson(values: FormValues): PromptResult {
  const finalJson = buildFinalJson(values);
  const prompt = generatePrompt(
    PROMPT_TEMPLATE_WITH_PLACEHOLDERS,
    finalJson,
    values.need
  );
  return {
    prompt,
    finalJson
  };
}
