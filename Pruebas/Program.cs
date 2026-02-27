// Program.cs
// Crea una HU en la iteración "Pruebas\Soporte SDI\2026\Febrero"
// y le vincula varios Test Cases de ejemplo (relación Tested By).
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

class Program
{
    static string org     = "";
    static string project = "";
    static string pat     = "";

    // Iteración destino (debe existir en el proyecto)
    const string IterationPath = @"Pruebas\Soporte SDI\2026\Febrero";

    // ── Valores del User Story ─────────────────────────────────────────────
    const string HuTitle              = "HU (auto) – Soporte SDI Febrero 2026";
    const string HuDescription        = "Historia de usuario generada automáticamente por script.";
    const string HuAcceptanceCriteria = "<ul><li>El sistema debe responder en menos de 2 segundos.</li><li>El usuario puede completar el flujo sin errores.</li></ul>";
    //const double HuStoryPoints        = 3;
    const int    HuPriority           = 2;      // 1=Critical 2=High 3=Medium 4=Low
    const string HuRisk               = "2 - Medium"; // Low | Medium | High
    const string HuStartDate          = "2026-02-01";  // yyyy-MM-dd
    const string HuFinishDate         = "2026-02-28"; // yyyy-MM-dd
    const string HuValueArea          = "Business";   // Business | Architectural
    // Campos personalizados del proceso Agile-Necesidades
    const string HuTipoHU             = "Funcional";  // valor del picklist Tipo HU
    const string HuFrenteDeTrabajo    = "Mejoras";   // valor del picklist Frente de Trabajo

    static async Task<int> Main()
    {
        // Cargar .env y luego leer variables
        LoadDotEnv();
        org     = Environment.GetEnvironmentVariable("AZDO_ORG")     ?? "";
        project = Environment.GetEnvironmentVariable("AZDO_PROJECT") ?? "";
        pat     = Environment.GetEnvironmentVariable("AZDO_PAT")     ?? "";

        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(pat))
        {
            Console.WriteLine("ERROR: Faltan variables de entorno: AZDO_ORG, AZDO_PROJECT o AZDO_PAT.");
            Console.WriteLine("Asegúrate de que el archivo .env está en la carpeta del proyecto.");
            return 1;
        }

        const string apiVersion = "7.1";
        using var client = new HttpClient();
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            // ── PASO 1: Obtener o crear la HU ────────────────────────────────
            int huId;
            var envHu = Environment.GetEnvironmentVariable("TARGET_HU_ID");
            if (!string.IsNullOrWhiteSpace(envHu) && int.TryParse(envHu, out var parsed))
            {
                huId = parsed;
                Console.WriteLine($"[1] Usando HU existente: ID={huId}");
            }
            else
            {
                Console.WriteLine("[1] Creando User Story...");
                huId = await CreateUserStoryAsync(client, apiVersion);
                if (huId < 0) { Console.WriteLine("ERROR creando HU."); return 1; }
                Console.WriteLine($"[1] HU creada: ID={huId}");
                Console.WriteLine($"    {WiUrl(huId)}");
            }

            // ── PASO 2: Crear Test Cases de ejemplo ──────────────────────────
            Console.WriteLine("\n[2] Creando Test Cases...");
            var tests = new List<(string title, string action, string expected)>
            {
                ("TC-001 — Login válido",
                 "Ingresar usuario y contraseña correctos y hacer clic en Ingresar.",
                 "El sistema redirige al dashboard y muestra el nombre del usuario."),

                ("TC-002 — Login inválido",
                 "Ingresar credenciales incorrectas y hacer clic en Ingresar.",
                 "El sistema muestra 'Credenciales inválidas' y no redirige."),

                ("TC-003 — Campo obligatorio vacío",
                 "Dejar el campo contraseña vacío y hacer clic en Ingresar.",
                 "El sistema muestra 'Campo requerido' junto al campo vacío."),

                ("TC-004 — Cierre de sesión",
                 "Hacer clic en Cerrar sesión desde el menú de usuario.",
                 "La sesión se cierra y se redirige a la pantalla de login."),

                ("TC-005 — Responsive en móvil",
                 "Abrir la aplicación en un dispositivo de 375px de ancho.",
                 "El layout se adapta correctamente; sin desbordamiento ni solapamiento.")
            };

            var createdTcs = new List<int>();
            foreach (var (title, action, expected) in tests)
            {
                int tcId = await CreateTestCaseAsync(client, apiVersion, title, action, expected);
                if (tcId > 0)
                {
                    createdTcs.Add(tcId);
                    Console.WriteLine($"    ID={tcId,-6} \"{title}\"");
                }
                else
                {
                    Console.WriteLine($"    ERROR creando: \"{title}\"");
                }
            }

            // ── PASO 3: Vincular TCs a la HU (Tested By) ────────────────────
            Console.WriteLine("\n[3] Vinculando Test Cases a la HU (Tested By)...");
            foreach (var tcId in createdTcs)
            {
                bool ok = await LinkTestedByAsync(client, apiVersion, huId, tcId);
                Console.WriteLine(ok
                    ? $"    HU {huId} <──[Tested By]── TC {tcId}  ✓"
                    : $"    ERROR vinculando TC {tcId}");
            }

            // ── RESUMEN ──────────────────────────────────────────────────────
            Console.WriteLine("\n══════════════════════════════════════════════════");
            Console.WriteLine("  PROCESO FINALIZADO");
            Console.WriteLine("══════════════════════════════════════════════════");
            Console.WriteLine($"  Iteración : {IterationPath}");
            Console.WriteLine($"  HU        : {WiUrl(huId)}");
            Console.WriteLine($"  Test Cases: {createdTcs.Count} creados y vinculados");
            Console.WriteLine("══════════════════════════════════════════════════");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR inesperado: " + ex.Message);
            return 1;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static string WiUrl(int id) =>
        $"https://dev.azure.com/{org}/{project}/_workitems/edit/{id}";

    // Crea una User Story con todos los campos del layout del proceso Agile-Necesidades.
    static async Task<int> CreateUserStoryAsync(HttpClient client, string api)
    {
        var url = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$User%20Story?api-version={api}";
        var patch = new List<Dictionary<string, object>>
        {
            // ── Campos estándar ───────────────────────────────────────────────
            new() { {"op","add"}, {"path","/fields/System.Title"},
                    {"value", HuTitle} },
            new() { {"op","add"}, {"path","/fields/System.Description"},
                    {"value", HuDescription} },
            new() { {"op","add"}, {"path","/fields/System.IterationPath"},
                    {"value", IterationPath} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.AcceptanceCriteria"},
                    {"value", HuAcceptanceCriteria} },
            // new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Scheduling.StoryPoints"},
                    //{"value", HuStoryPoints} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.Priority"},
                    {"value", HuPriority} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.Risk"},
                    {"value", HuRisk} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Scheduling.StartDate"},
                    {"value", HuStartDate} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Scheduling.FinishDate"},
                    {"value", HuFinishDate} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.ValueArea"},
                    {"value", HuValueArea} },
            // ── Campos personalizados (proceso Agile-Necesidades) ─────────────
            // Ref names: ir a Project Settings > Process > User Story para confirmarlos
            new() { {"op","add"}, {"path","/fields/Custom.TipoHU"},
                    {"value", HuTipoHU} },
            new() { {"op","add"}, {"path","/fields/Custom.FrenteDeTrabajo"},
                    {"value", HuFrenteDeTrabajo} },
        };
        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue ? res.Value.GetProperty("id").GetInt32() : -1;
    }

    // Crea un Test Case con pasos estructurados en la misma iteración.
    static async Task<int> CreateTestCaseAsync(HttpClient client, string api,
        string title, string action, string expected)
    {
        var url = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$Test%20Case?api-version={api}";
        var steps = $"<steps id=\"0\" last=\"1\">" +
                    $"<step id=\"1\" type=\"ActionStep\">" +
                    $"<parameterizedString isformatted=\"true\">{action}</parameterizedString>" +
                    $"<parameterizedString isformatted=\"true\">{expected}</parameterizedString>" +
                    $"<description/></step></steps>";
        var patch = new List<Dictionary<string, object>>
        {
            new() { {"op","add"}, {"path","/fields/System.Title"},              {"value", title} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.TCM.Steps"}, {"value", steps} },
            new() { {"op","add"}, {"path","/fields/System.IterationPath"},      {"value", IterationPath} }
        };
        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue ? res.Value.GetProperty("id").GetInt32() : -1;
    }

    // Agrega relación "Tested By" desde la HU hacia el Test Case.
    static async Task<bool> LinkTestedByAsync(HttpClient client, string api, int huId, int tcId)
    {
        var url   = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{huId}?api-version={api}";
        var tcUrl = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{tcId}";
        var patch = new List<Dictionary<string, object>>
        {
            new()
            {
                {"op","add"}, {"path","/relations/-"},
                {"value", new Dictionary<string, object>
                    {
                        {"rel",        "Microsoft.VSTS.Common.TestedBy-Forward"},
                        {"url",        tcUrl},
                        {"attributes", new Dictionary<string, object>
                            { {"comment","Vinculado automáticamente"} }}
                    }
                }
            }
        };
        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue;
    }

    // PATCH application/json-patch+json (Work Items API).
    static async Task<JsonElement?> PatchWiAsync(HttpClient client, string url,
        List<Dictionary<string, object>> patch)
    {
        var json = JsonSerializer.Serialize(patch);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json-patch+json");
        using var req  = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
        using var resp = await client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"    HTTP {resp.StatusCode}: {body}");
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    // Lee el archivo .env más cercano (sube desde el exe hasta encontrarlo)
    // y registra cada KEY=VALUE como variable de entorno del proceso.
    static void LoadDotEnv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var idx = line.IndexOf('=');
                    if (idx < 1) continue;
                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                        Environment.SetEnvironmentVariable(key, val);
                }
                return;
            }
            dir = dir.Parent;
        }
    }
}
