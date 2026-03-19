import { useEffect, useState } from "react";
import { FormProvider, useForm } from "react-hook-form";
import DOMPurify from "dompurify";
import { payloadToFormValues, TEMPLATE_PAYLOAD } from "./lib/template";
import { generatePromptAndJson } from "./components/PromptGenerator";
import type { FormValues, HuPayload } from "./types";
import { copyToClipboard, downloadJson } from "./utils/download";

type ModuleKey = "builder" | "json-preview";
const BUILDER_STORAGE_KEY = "hu.builder.state.v1";
const PREVIEW_STORAGE_KEY = "hu.preview.state.v1";
const LOG_STORAGE_KEY = "hu.app.logs.v1";

type LogLevel = "success" | "error" | "info";

interface AppLog {
    id: string;
    timestamp: string;
    level: LogLevel;
    module: "builder" | "preview" | "app";
    action: string;
    message: string;
    detail?: string;
}

interface BuilderPersistedState {
    formValues: FormValues;
    prompt: string;
    jsonOut: HuPayload | null;
    message: string;
}

interface CreatedTc {
    id: number;
    title: string;
    url: string;
}

interface CreateResult {
    huId: number;
    huUrl: string;
    testCases: CreatedTc[];
    suiteId: number;
    message: string;
}

interface PreviewPersistedState {
    promptInput: string;
    rawJson: string;
    parsed: HuPayload | null;
    rawResponse: string;
    status: string;
    error: string;
    createResult: CreateResult | null;
}

function loadLocalState<T>(key: string): T | null {
    try {
        const raw = localStorage.getItem(key);
        if (!raw) return null;
        return JSON.parse(raw) as T;
    } catch {
        return null;
    }
}

function parseHuPayload(value: string): HuPayload {
    const parsed = JSON.parse(value) as HuPayload;
    if (!parsed.hu || !parsed.testCases || !Array.isArray(parsed.testCases)) {
        throw new Error("El JSON debe contener 'hu' y 'testCases'.");
    }
    return parsed;
}

interface BuilderModuleProps {
    onPromptReady: (prompt: string) => void;
    onOpenAiModule: (prompt: string) => void;
    onLog: (entry: Omit<AppLog, "id" | "timestamp">) => void;
}

function BuilderModule({ onPromptReady, onOpenAiModule, onLog }: BuilderModuleProps) {
    const methods = useForm<FormValues>({
        mode: "onBlur",
        defaultValues: payloadToFormValues(TEMPLATE_PAYLOAD)
    });

    const [prompt, setPrompt] = useState("");
    const [jsonOut, setJsonOut] = useState<HuPayload | null>(null);
    const [message, setMessage] = useState("");

    useEffect(() => {
        const persisted = loadLocalState<BuilderPersistedState>(BUILDER_STORAGE_KEY);
        if (!persisted) return;

        methods.reset(persisted.formValues);
        setPrompt(persisted.prompt ?? "");
        setJsonOut(persisted.jsonOut ?? null);
        setMessage(persisted.message ?? "");

        if (persisted.prompt?.trim()) {
            onPromptReady(persisted.prompt);
        }
    }, [methods, onPromptReady]);

    useEffect(() => {
        const subscription = methods.watch(() => {
            const toStore: BuilderPersistedState = {
                formValues: methods.getValues(),
                prompt,
                jsonOut,
                message
            };
            localStorage.setItem(BUILDER_STORAGE_KEY, JSON.stringify(toStore));
        });

        return () => subscription.unsubscribe();
    }, [methods, prompt, jsonOut, message]);

    useEffect(() => {
        const toStore: BuilderPersistedState = {
            formValues: methods.getValues(),
            prompt,
            jsonOut,
            message
        };
        localStorage.setItem(BUILDER_STORAGE_KEY, JSON.stringify(toStore));
    }, [methods, prompt, jsonOut, message]);

    const onGenerate = methods.handleSubmit((values) => {
        const result = generatePromptAndJson(values);
        setPrompt(result.prompt);
        setJsonOut(result.finalJson);
        onPromptReady(result.prompt);
        setMessage("Prompt y JSON generados correctamente.");
        onLog({
            level: "success",
            module: "builder",
            action: "generate-prompt",
            message: "Prompt y JSON generados correctamente.",
            detail: `TCs: ${result.finalJson.testCases.length}`
        });
    });

    const copyPrompt = async () => {
        if (!prompt) return;
        await copyToClipboard(prompt);
        setMessage("Prompt copiado.");
        onLog({
            level: "info",
            module: "builder",
            action: "copy-prompt",
            message: "Prompt copiado al portapapeles."
        });
    };

    const copyJson = async () => {
        if (!jsonOut) return;
        await copyToClipboard(JSON.stringify(jsonOut, null, 2));
        setMessage("JSON copiado.");
        onLog({
            level: "info",
            module: "builder",
            action: "copy-json",
            message: "JSON copiado al portapapeles."
        });
    };

    const resetTemplate = () => {
        methods.reset(payloadToFormValues(TEMPLATE_PAYLOAD));
        setPrompt("");
        setJsonOut(null);
        setMessage("Formulario restaurado al template.");
        localStorage.removeItem(BUILDER_STORAGE_KEY);
        onLog({
            level: "info",
            module: "builder",
            action: "reset-template",
            message: "Formulario restaurado al template."
        });
    };

    return (
        <FormProvider {...methods}>
            <div className="space-y-6">
                <section className="-mx-5 -mt-5 bg-white px-5 py-6 md:-mx-8 md:-mt-8 md:px-8">
                    <div className="mx-auto w-full max-w-[1120px] space-y-4">
                        <h2 className="text-4xl font-display text-ink">Armar Prompt</h2>
                        <p className="text-sm text-ink/75">
                            Solo puedes editar necesidad, cantidad de test cases, estado de test cases, area, iteration path,
                            assigned to, fechas, valueArea, tipoHU, frenteDeTrabajo, risk, priority y testSuite.
                        </p>
                    </div>
                </section>

                <div className="content-wrap">
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
                                        <option value="1 - High">1 - High</option>
                                        <option value="2 - Medium">2 - Medium</option>
                                        <option value="3 - Low">3 - Low</option>
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
                                    <select className="field-input" {...methods.register("hu.valueArea")}>
                                        <option value="Business">Business </option>
                                        <option value="Architectural">Architectural</option>
                                    </select>
                                </label>
                                <label className="field-label">
                                    hu.tipoHU
                                    <select className="field-input" {...methods.register("hu.tipoHU")}>
                                        <option value="Funcional">Funcional</option>
                                        <option value="Técnica">Técnica</option>
                                    </select>
                                </label>

                                <label className="field-label md:col-span-2">
                                    hu.frenteDeTrabajo
                                    <select className="field-input" {...methods.register("hu.frenteDeTrabajo")}>
                                        <option value="ControlCambios">ControlCambios</option>
                                        <option value="Mejoras">Mejoras</option>
                                        <option value="OptimizacionBackEnd">OptimizacionBackEnd</option>
                                        <option value="OptimizacionFrontEnd">OptimizacionFrontEnd</option>
                                        <option value="Proyecto">Proyecto </option>
                                        <option value="Seguridad">Seguridad</option>
                                    </select>
                                </label>
                            </div>

                            <div className="grid gap-4 md:grid-cols-2">
                                <label className="field-label">
                                    <span className="flex items-center gap-2">
                                        <span>testSuite.planId</span>
                                        <span className="text-xs font-semibold text-red-400">*Obligatorio</span>
                                    </span>
                                    <input
                                        type="number"
                                        className="field-input"
                                        {...methods.register("testSuite.planId", { valueAsNumber: true })}
                                    />
                                </label>
                                <label className="field-label">
                                    <span className="flex items-center gap-2">
                                        <span>testSuite.planName</span>
                                        <span className="text-xs font-semibold text-green-400">*Opcional</span>
                                    </span>
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
                            <button
                                type="button"
                                className="btn-secondary"
                                onClick={() => onOpenAiModule(prompt)}
                                disabled={!prompt}
                            >
                                Enviar prompt a IA
                            </button>
                        </div>
                        {message && <p className="rounded-xl bg-white/80 px-5 py-2 text-2m text-green-500">{message}</p>}
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
            </div>
        </FormProvider>
    );
}

interface JsonPreviewModuleProps {
    initialPrompt: string;
    onGoBack: () => void;
    onLog: (entry: Omit<AppLog, "id" | "timestamp">) => void;
}

function JsonPreviewModule({ initialPrompt, onGoBack, onLog }: JsonPreviewModuleProps) {
    const [promptInput, setPromptInput] = useState(initialPrompt);
    const [rawJson, setRawJson] = useState("");
    const [parsed, setParsed] = useState<HuPayload | null>(null);
    const [rawResponse, setRawResponse] = useState("");
    const [isLoading, setIsLoading] = useState(false);
    const [isCreating, setIsCreating] = useState(false);
    const [status, setStatus] = useState("");
    const [error, setError] = useState("");
    const [createResult, setCreateResult] = useState<CreateResult | null>(null);

    useEffect(() => {
        const persisted = loadLocalState<PreviewPersistedState>(PREVIEW_STORAGE_KEY);
        if (!persisted) return;

        setPromptInput(persisted.promptInput ?? "");
        setRawJson(persisted.rawJson ?? "");
        setParsed(persisted.parsed ?? null);
        setRawResponse(persisted.rawResponse ?? "");
        setStatus(persisted.status ?? "");
        setError(persisted.error ?? "");
        setCreateResult(persisted.createResult ?? null);
    }, []);

    useEffect(() => {
        if (initialPrompt.trim() && initialPrompt !== promptInput) {
            setPromptInput(initialPrompt);
        }
    }, [initialPrompt, promptInput]);

    useEffect(() => {
        const toStore: PreviewPersistedState = {
            promptInput,
            rawJson,
            parsed,
            rawResponse,
            status,
            error,
            createResult
        };
        localStorage.setItem(PREVIEW_STORAGE_KEY, JSON.stringify(toStore));
    }, [promptInput, rawJson, parsed, rawResponse, status, error, createResult]);

    const resetAll = () => {
        setRawJson("");
        setParsed(null);
        setError("");
        setStatus("");
        setRawResponse("");
        setCreateResult(null);
        localStorage.removeItem(PREVIEW_STORAGE_KEY);
        onLog({
            level: "info",
            module: "preview",
            action: "reset-preview",
            message: "Estado de preview limpiado."
        });
    };

    const callBackendGenerate = async () => {
        if (!promptInput.trim()) {
            setError("Pega el prompt antes de invocar la IA.");
            onLog({
                level: "error",
                module: "preview",
                action: "generate-json",
                message: "No se pudo generar JSON: prompt vacío."
            });
            return;
        }

        setIsLoading(true);
        setError("");
        setCreateResult(null);
        setStatus("Consultando OpenAI via backend...");
        onLog({
            level: "info",
            module: "preview",
            action: "generate-json-start",
            message: "Solicitud enviada al backend para generar JSON con IA."
        });

        try {
            const response = await fetch("/api/generate", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ prompt: promptInput })
            });

            const data = (await response.json()) as { json?: string; raw?: string; error?: string; warning?: string };

            if (!response.ok) {
                throw new Error(data.error ?? `Error ${response.status}`);
            }

            setRawResponse(data.raw ?? "");
            const jsonStr = data.json ?? "";
            setRawJson(jsonStr);

            const value = parseHuPayload(jsonStr);
            setParsed(value);
            setStatus(data.warning ? `JSON generado (con advertencia: ${data.warning})` : "JSON generado por IA y previsualizado.");
            onLog({
                level: data.warning ? "info" : "success",
                module: "preview",
                action: "generate-json-success",
                message: data.warning
                    ? "JSON generado con advertencia."
                    : "JSON generado y previsualizado correctamente.",
                detail: data.warning ? data.warning : `TCs: ${value.testCases.length}`
            });
        } catch (err) {
            setParsed(null);
            setStatus("");
            const errorMessage = (err as Error).message;
            setError(errorMessage);
            onLog({
                level: "error",
                module: "preview",
                action: "generate-json-error",
                message: "Error al generar JSON con IA.",
                detail: errorMessage
            });
        } finally {
            setIsLoading(false);
        }
    };

    const createInAzureDevOps = async () => {
        if (!parsed) return;

        setIsCreating(true);
        setError("");
        setCreateResult(null);
        setStatus("Creando HU en Azure DevOps...");
        onLog({
            level: "info",
            module: "preview",
            action: "create-azdo-start",
            message: "Solicitud enviada para crear HU en Azure DevOps."
        });

        try {
            const response = await fetch("/api/create", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: rawJson
            });

            const data = (await response.json()) as CreateResult & { error?: string };

            if (!response.ok) {
                throw new Error(data.error ?? `Error ${response.status}`);
            }

            setCreateResult(data);
            setStatus(data.message);
            onLog({
                level: "success",
                module: "preview",
                action: "create-azdo-success",
                message: `HU creada en Azure DevOps (#${data.huId}).`,
                detail: `TCs creados: ${data.testCases.length}`
            });
        } catch (err) {
            setStatus("");
            const errorMessage = (err as Error).message;
            setError(errorMessage);
            onLog({
                level: "error",
                module: "preview",
                action: "create-azdo-error",
                message: "Error al crear HU en Azure DevOps.",
                detail: errorMessage
            });
        } finally {
            setIsCreating(false);
        }
    };

    const copyJsonOutput = async () => {
        if (!rawJson.trim()) return;
        await copyToClipboard(rawJson);
        setStatus("JSON copiado al portapapeles.");
        onLog({
            level: "info",
            module: "preview",
            action: "copy-json",
            message: "JSON de salida copiado al portapapeles."
        });
    };

    return (
        <div className="space-y-6">
            {!parsed && !createResult && (
                <section className="panel space-y-4">
                    <h2 className="text-2xl font-display text-ink">Generar JSON con IA</h2>
                    <p className="text-sm text-ink/75">
                        El prompt se envía al backend que consulta OpenAI. El JSON resultante se previsualiza automáticamente.
                    </p>

                    <details className="rounded-xl border border-ink/15 bg-white p-3">
                        <summary className="cursor-pointer text-sm font-semibold text-ink">Ver / editar prompt</summary>
                        <textarea
                            rows={8}
                            className="field-input font-mono mt-2"
                            value={promptInput}
                            onChange={(event) => setPromptInput(event.target.value)}
                        />
                    </details>

                    <div className="flex flex-wrap gap-2">
                        <button type="button" className="btn-primary" onClick={() => void callBackendGenerate()} disabled={isLoading || !promptInput.trim()}>
                            {isLoading ? "Generando..." : "Generar JSON con IA"}
                        </button>
                        <button type="button" className="btn-secondary" onClick={onGoBack}>
                            Volver a Armar Prompt
                        </button>
                    </div>

                    {status && <p className="rounded-xl bg-white/80 px-3 py-2 text-sm text-green-600">{status}</p>}
                    {error && <p className="rounded-xl bg-ember/15 px-3 py-2 text-sm text-ember">{error}</p>}
                </section>
            )}

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

                    {!createResult && (
                        <div className="flex flex-wrap gap-2 pt-2">
                            <button
                                type="button"
                                className="btn-primary"
                                onClick={() => void createInAzureDevOps()}
                                disabled={isCreating}
                            >
                                {isCreating ? "Creando en Azure DevOps..." : "Crear en Azure DevOps"}
                            </button>
                            <button
                                type="button"
                                className="btn-secondary"
                                onClick={() => { resetAll(); onGoBack(); }}
                                disabled={isCreating}
                            >
                                Volver a empezar
                            </button>
                            <button type="button" className="btn-muted" onClick={() => void copyJsonOutput()} disabled={!rawJson.trim()}>
                                Copiar JSON
                            </button>
                            <button
                                type="button"
                                className="btn-muted"
                                onClick={() => parsed && downloadJson("hu.generated.json", parsed)}
                            >
                                Descargar JSON
                            </button>
                        </div>
                    )}

                    {rawResponse && (
                        <details className="rounded-xl border border-ink/15 bg-white p-3">
                            <summary className="cursor-pointer text-sm font-semibold text-ink">Respuesta cruda del modelo</summary>
                            <pre className="output-box mt-2">{rawResponse}</pre>
                        </details>
                    )}

                    {status && <p className="rounded-xl bg-white/80 px-3 py-2 text-sm text-green-600">{status}</p>}
                    {error && <p className="rounded-xl bg-ember/15 px-3 py-2 text-sm text-ember">{error}</p>}
                </section>
            )}

            {createResult && (
                <section className="panel space-y-4">
                    <h3 className="text-xl font-semibold text-green-700">Creado en Azure DevOps</h3>
                    <div className="rounded-xl border border-green-200 bg-green-50 p-4 space-y-2">
                        <p className="font-semibold">
                            HU #{createResult.huId}
                            <a
                                href={createResult.huUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="ml-2 text-sm font-normal text-blue-600 underline"
                            >
                                Abrir en Azure DevOps
                            </a>
                        </p>
                        {createResult.testCases.length > 0 && (
                            <div>
                                <p className="text-sm font-semibold">Test Cases:</p>
                                <ul className="list-disc pl-5 text-sm space-y-1">
                                    {createResult.testCases.map((tc) => (
                                        <li key={tc.id}>
                                            #{tc.id} — {tc.title}{" "}
                                            <a
                                                href={tc.url}
                                                target="_blank"
                                                rel="noopener noreferrer"
                                                className="text-blue-600 underline"
                                            >
                                                ver
                                            </a>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        )}
                        {createResult.suiteId > 0 && (
                            <p className="text-sm">Test Suite ID: {createResult.suiteId}</p>
                        )}
                    </div>
                    <div className="flex flex-wrap gap-2 pt-2">
                        <button type="button" className="btn-primary" onClick={() => { resetAll(); onGoBack(); }}>
                            Crear otra HU
                        </button>
                    </div>
                </section>
            )}
        </div>
    );
}

export default function App() {
    const [module, setModule] = useState<ModuleKey>("builder");
    const [promptForAi, setPromptForAi] = useState("");
    const [logs, setLogs] = useState<AppLog[]>(() => loadLocalState<AppLog[]>(LOG_STORAGE_KEY) ?? []);

    const goToBuilderModule = () => {
        setModule("builder");
        window.scrollTo({ top: 0, behavior: "smooth" });
    };

    const addLog = (entry: Omit<AppLog, "id" | "timestamp">) => {
        const next: AppLog = {
            id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
            timestamp: new Date().toISOString(),
            ...entry
        };
        setLogs((prev) => [next, ...prev].slice(0, 200));
    };

    useEffect(() => {
        localStorage.setItem(LOG_STORAGE_KEY, JSON.stringify(logs));
    }, [logs]);

    const clearLogs = () => {
        setLogs([]);
        localStorage.removeItem(LOG_STORAGE_KEY);
    };

    const copyLogs = async () => {
        const text = logs
            .map((item) => {
                const parts = [`[${item.timestamp}]`, item.level.toUpperCase(), `${item.module}:${item.action}`, item.message];
                if (item.detail) parts.push(`detail=${item.detail}`);
                return parts.join(" | ");
            })
            .join("\n");
        await copyToClipboard(text || "Sin logs registrados.");
        addLog({
            level: "info",
            module: "app",
            action: "copy-logs",
            message: "Panel de logs copiado al portapapeles."
        });
    };

    const openAiModuleWithPrompt = (prompt: string) => {
        setPromptForAi(prompt);
        setModule("json-preview");
        addLog({
            level: "info",
            module: "app",
            action: "open-preview",
            message: "Prompt enviado al modulo IA para generar JSON."
        });
    };

    return (
        <div className="min-h-screen bg-theme text-ink">
            <div className="grid min-h-screen grid-cols-1 md:grid-cols-[290px_1fr]">
                <aside className="sidebar">
                    <div className="space-y-3">
                        <p className="chip">User Story Automation</p>
                        <h1 className="text-3xl font-display">Generador HU</h1>
                        <p className="text-sm text-white/85">Selecciona un módulo para trabajar.</p>
                    </div>

                    <nav className="space-y-2">
                        <button
                            type="button"
                            className={`sidebar-link ${module === "builder" ? "sidebar-link-active" : ""}`}
                            onClick={goToBuilderModule}
                        >
                            Armar Prompt
                        </button>
                        <button
                            type="button"
                            className={`sidebar-link ${module === "json-preview" ? "sidebar-link-active" : ""}`}
                            onClick={() => setModule("json-preview")}
                        >
                            IA + Preview JSON
                        </button>
                    </nav>

                    <div className="mt-auto rounded-2xl border border-white/20 bg-white/10 p-3 text-sm text-white/85">
                        Modo de Uso: En Armar Prompt personaliza todos los campos para generar la HU según tu necesidad y después deja que la IA haga su magia.
                    </div>
                </aside>

                <main className="content-area">
                    <div className={module === "builder" ? "block" : "hidden"}>
                        <BuilderModule
                            onPromptReady={setPromptForAi}
                            onOpenAiModule={openAiModuleWithPrompt}
                            onLog={addLog}
                        />
                    </div>

                    <div className={module === "json-preview" ? "block" : "hidden"}>
                        <div className="content-wrap">
                            <JsonPreviewModule
                                initialPrompt={promptForAi}
                                onGoBack={() => setModule("builder")}
                                onLog={addLog}
                            />
                        </div>
                    </div>

                    <section className="content-wrap mt-6">
                        <div className="panel space-y-3">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                                <h3 className="text-lg font-semibold text-ink">Panel de logs</h3>
                                <div className="flex flex-wrap gap-2">
                                    <button type="button" className="btn-muted" onClick={() => void copyLogs()}>
                                        Copiar logs
                                    </button>
                                    <button type="button" className="btn-secondary" onClick={clearLogs}>
                                        Limpiar logs
                                    </button>
                                </div>
                            </div>

                            <p className="text-xs text-ink/70">
                                Registra eventos de exito, error e informacion para facilitar diagnostico en despliegues.
                            </p>

                            <div className="max-h-72 space-y-2 overflow-auto rounded-xl border border-ink/15 bg-white p-3">
                                {logs.length === 0 ? (
                                    <p className="text-sm text-ink/60">Sin eventos por ahora.</p>
                                ) : (
                                    logs.map((item) => (
                                        <article key={item.id} className="rounded-lg border border-ink/10 bg-slate-50 p-2 text-xs">
                                            <div className="flex flex-wrap items-center gap-2">
                                                <span className="font-semibold text-ink">{item.level.toUpperCase()}</span>
                                                <span className="text-ink/60">{new Date(item.timestamp).toLocaleString()}</span>
                                                <span className="rounded bg-ink/10 px-2 py-0.5 text-ink">{item.module}:{item.action}</span>
                                            </div>
                                            <p className="mt-1 text-sm text-ink">{item.message}</p>
                                            {item.detail && <p className="mt-1 text-ink/70">{item.detail}</p>}
                                        </article>
                                    ))
                                )}
                            </div>
                        </div>
                    </section>
                </main>
            </div>
        </div>
    );
}
