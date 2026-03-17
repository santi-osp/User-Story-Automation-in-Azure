import { useState } from "react";
import { FormProvider, useForm } from "react-hook-form";
import DOMPurify from "dompurify";
import { payloadToFormValues, TEMPLATE_PAYLOAD } from "./lib/template";
import { generatePromptAndJson } from "./components/PromptGenerator";
import type { FormValues, HuPayload } from "./types";
import { copyToClipboard, downloadJson } from "./utils/download";

type ModuleKey = "builder" | "json-preview";

function BuilderModule() {
    const methods = useForm<FormValues>({
        mode: "onBlur",
        defaultValues: payloadToFormValues(TEMPLATE_PAYLOAD)
    });

    const [prompt, setPrompt] = useState("");
    const [jsonOut, setJsonOut] = useState<HuPayload | null>(null);
    const [message, setMessage] = useState("");

    const onGenerate = methods.handleSubmit((values) => {
        const result = generatePromptAndJson(values);
        setPrompt(result.prompt);
        setJsonOut(result.finalJson);
        setMessage("Prompt y JSON generados correctamente.");
    });

    const copyPrompt = async () => {
        if (!prompt) return;
        await copyToClipboard(prompt);
        setMessage("Prompt copiado.");
    };

    const copyJson = async () => {
        if (!jsonOut) return;
        await copyToClipboard(JSON.stringify(jsonOut, null, 2));
        setMessage("JSON copiado.");
    };

    const resetTemplate = () => {
        methods.reset(payloadToFormValues(TEMPLATE_PAYLOAD));
        setPrompt("");
        setJsonOut(null);
        setMessage("Formulario restaurado al template.");
    };

    return (
        <FormProvider {...methods}>
            <div className="space-y-6">
                <section className="panel space-y-4">
                    <h2 className="text-2xl font-display text-ink">Módulo 1: Armar Prompt</h2>
                    <p className="text-sm text-ink/75">
                        Solo puedes editar necesidad, cantidad de test cases, estado de test cases, area, iteration path,
                        assigned to, fechas, valueArea, tipoHU, frenteDeTrabajo, risk, priority y testSuite.
                    </p>
                    {message && <p className="rounded-xl bg-white/80 px-3 py-2 text-sm">{message}</p>}
                </section>

                <form className="space-y-5" onSubmit={onGenerate}>
                    <section className="panel space-y-4">
                        <label className="field-label">
                            Necesidad
                            <textarea
                                rows={4}
                                className="field-input"
                                {...methods.register("need")}
                                placeholder="Describe la necesidad que reemplaza la sección Necesito"
                            />
                        </label>

                        <div className="grid gap-4 md:grid-cols-3">
                            <label className="field-label">
                                Cantidad de Test Cases
                                <input
                                    type="number"
                                    min={1}
                                    max={50}
                                    className="field-input"
                                    {...methods.register("tcCount", { valueAsNumber: true, min: 1, max: 50 })}
                                />
                            </label>

                            <label className="field-label">
                                Estado de Test Cases
                                <select className="field-input" {...methods.register("tcState")}>
                                    <option value="Ready">Ready</option>
                                    <option value="Closed">Closed</option>
                                    <option value="Desing">Desing</option>
                                </select>
                            </label>

                            <label className="field-label">
                                hu.priority
                                <input
                                    type="number"
                                    min={1}
                                    max={4}
                                    className="field-input"
                                    {...methods.register("hu.priority", {
                                        valueAsNumber: true,
                                        required: true,
                                        min: 1,
                                        max: 4
                                    })}
                                />
                            </label>
                        </div>

                        <div className="grid gap-4 md:grid-cols-2">
                            <label className="field-label">
                                iterationPath
                                <input className="field-input" {...methods.register("iterationPath", { required: true })} />
                            </label>
                            <label className="field-label">
                                areaPath
                                <input className="field-input" {...methods.register("areaPath", { required: true })} />
                            </label>

                            <label className="field-label">
                                hu.assignedTo
                                <input className="field-input" {...methods.register("hu.assignedTo")} />
                            </label>
                            <label className="field-label">
                                hu.risk
                                <select className="field-input" {...methods.register("hu.risk")}>
                                    <option value="Baja">Baja</option>
                                    <option value="Media">Media</option>
                                    <option value="Alta">Alta</option>
                                </select>
                            </label>

                            <label className="field-label">
                                hu.startDate
                                <input type="date" className="field-input" {...methods.register("hu.startDate")} />
                            </label>
                            <label className="field-label">
                                hu.finishDate
                                <input type="date" className="field-input" {...methods.register("hu.finishDate")} />
                            </label>

                            <label className="field-label">
                                hu.valueArea
                                <input className="field-input" {...methods.register("hu.valueArea")} />
                            </label>
                            <label className="field-label">
                                hu.tipoHU
                                <input className="field-input" {...methods.register("hu.tipoHU")} />
                            </label>

                            <label className="field-label md:col-span-2">
                                hu.frenteDeTrabajo
                                <input className="field-input" {...methods.register("hu.frenteDeTrabajo")} />
                            </label>
                        </div>

                        <div className="grid gap-4 md:grid-cols-2">
                            <label className="field-label">
                                testSuite.planId
                                <input
                                    type="number"
                                    className="field-input"
                                    {...methods.register("testSuite.planId", { valueAsNumber: true })}
                                />
                            </label>
                            <label className="field-label">
                                testSuite.planName
                                <input className="field-input" {...methods.register("testSuite.planName")} />
                            </label>
                        </div>
                    </section>

                    <div className="flex flex-wrap gap-2">
                        <button type="submit" className="btn-primary">
                            Generar prompt
                        </button>
                        <button type="button" className="btn-secondary" onClick={resetTemplate}>
                            Reset
                        </button>
                        <button type="button" className="btn-muted" onClick={() => void copyPrompt()} disabled={!prompt}>
                            Copiar prompt
                        </button>
                        <button type="button" className="btn-muted" onClick={() => void copyJson()} disabled={!jsonOut}>
                            Copiar JSON
                        </button>
                        <button
                            type="button"
                            className="btn-muted"
                            onClick={() => jsonOut && downloadJson("hu.generated.json", jsonOut)}
                            disabled={!jsonOut}
                        >
                            Descargar JSON
                        </button>
                    </div>
                </form>

                <section className="panel grid gap-4 lg:grid-cols-2">
                    <article>
                        <h3 className="mb-2 font-semibold text-ink">Prompt final</h3>
                        <pre className="output-box">{prompt || "Aún no generado"}</pre>
                    </article>
                    <article>
                        <h3 className="mb-2 font-semibold text-ink">JSON final</h3>
                        <pre className="output-box">{jsonOut ? JSON.stringify(jsonOut, null, 2) : "Aún no generado"}</pre>
                    </article>
                </section>
            </div>
        </FormProvider>
    );
}

function JsonPreviewModule() {
    const [rawJson, setRawJson] = useState("");
    const [parsed, setParsed] = useState<HuPayload | null>(null);
    const [error, setError] = useState("");

    const parseInput = () => {
        try {
            const value = JSON.parse(rawJson) as HuPayload;
            if (!value.hu || !value.testCases) {
                throw new Error("El JSON debe tener 'hu' y 'testCases'.");
            }
            setParsed(value);
            setError("");
        } catch (err) {
            setParsed(null);
            setError((err as Error).message);
        }
    };

    return (
        <div className="space-y-6">
            <section className="panel space-y-4">
                <h2 className="text-2xl font-display text-ink">Módulo 2: Preview desde JSON</h2>
                <p className="text-sm text-ink/75">
                    Pega un JSON de HU para ver previsualización de HU y Test Cases.
                </p>

                <textarea
                    rows={10}
                    className="field-input font-mono"
                    placeholder='Pega aquí el JSON completo con "hu" y "testCases"'
                    value={rawJson}
                    onChange={(event) => setRawJson(event.target.value)}
                />
                <div className="flex gap-2">
                    <button type="button" className="btn-primary" onClick={parseInput}>
                        Previsualizar
                    </button>
                    <button
                        type="button"
                        className="btn-secondary"
                        onClick={() => {
                            setRawJson("");
                            setParsed(null);
                            setError("");
                        }}
                    >
                        Limpiar
                    </button>
                </div>

                {error && <p className="rounded-xl bg-ember/15 px-3 py-2 text-sm text-ember">{error}</p>}
            </section>

            {parsed && (
                <section className="panel space-y-4">
                    <h3 className="text-xl font-semibold text-ink">Previsualización HU</h3>
                    <div className="grid gap-4 md:grid-cols-2">
                        <div className="rounded-xl border border-ink/15 bg-white p-3">
                            <p className="text-xs uppercase text-ink/60">Título</p>
                            <p className="font-semibold">{parsed.hu.title}</p>
                        </div>
                        <div className="rounded-xl border border-ink/15 bg-white p-3">
                            <p className="text-xs uppercase text-ink/60">Asignado</p>
                            <p className="font-semibold">{parsed.hu.assignedTo || "(sin asignar)"}</p>
                        </div>
                    </div>

                    <div className="grid gap-4 md:grid-cols-2">
                        <article className="rounded-2xl border border-ink/10 bg-white p-4">
                            <h4 className="mb-2 font-semibold">Description (HTML)</h4>
                            <div
                                className="prose max-w-none text-sm"
                                dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(parsed.hu.description) }}
                            />
                        </article>
                        <article className="rounded-2xl border border-ink/10 bg-white p-4">
                            <h4 className="mb-2 font-semibold">Acceptance Criteria (HTML)</h4>
                            <div
                                className="prose max-w-none text-sm"
                                dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(parsed.hu.acceptanceCriteria) }}
                            />
                        </article>
                    </div>

                    <h3 className="text-xl font-semibold text-ink">Test Cases ({parsed.testCases.length})</h3>
                    <div className="space-y-3">
                        {parsed.testCases.map((tc, index) => (
                            <article key={`${tc.title}-${index}`} className="rounded-xl border border-ink/15 bg-white p-4">
                                <div className="mb-2 flex items-center justify-between gap-2">
                                    <h4 className="font-semibold text-ink">{tc.title}</h4>
                                    <span className="rounded-full bg-ink px-2 py-1 text-xs text-sand">{tc.state}</span>
                                </div>
                                <p className="text-sm"><strong>Action:</strong> {tc.action}</p>
                                <p className="text-sm"><strong>Expected:</strong> {tc.expected}</p>
                            </article>
                        ))}
                    </div>
                </section>
            )}
        </div>
    );
}

export default function App() {
    const [module, setModule] = useState<ModuleKey>("builder");

    return (
        <div className="min-h-screen bg-theme text-ink">
            <div className="grid min-h-screen grid-cols-1 md:grid-cols-[290px_1fr]">
                <aside className="sidebar">
                    <div className="space-y-3">
                        <p className="chip">HU Workbench</p>
                        <h1 className="text-2xl font-display">Generador SDI</h1>
                        <p className="text-sm text-white/85">Selecciona un módulo para trabajar. Los accesos rápidos están arriba para reducir clics.</p>
                    </div>

                    <nav className="space-y-2">
                        <button
                            type="button"
                            className={`sidebar-link ${module === "builder" ? "sidebar-link-active" : ""}`}
                            onClick={() => setModule("builder")}
                        >
                            1. Armar Prompt
                        </button>
                        <button
                            type="button"
                            className={`sidebar-link ${module === "json-preview" ? "sidebar-link-active" : ""}`}
                            onClick={() => setModule("json-preview")}
                        >
                            2. Preview JSON
                        </button>
                    </nav>

                    <div className="mt-auto rounded-2xl border border-white/20 bg-white/10 p-3 text-xs text-white/85">
                        Consejo: en Armar Prompt completa primero necesidad y cantidad de TCs para obtener resultados más rápidos.
                    </div>
                </aside>

                <main className="content-area">
                    <div className="content-wrap">{module === "builder" ? <BuilderModule /> : <JsonPreviewModule />}</div>
                </main>
            </div>
        </div>
    );
}
