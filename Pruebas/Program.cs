// Program.cs
// Lee un archivo JSON con los parámetros de la HU y los TCs, los crea en
// Azure DevOps y los vincula (relación Tested By).
//
// Uso:
//   dotnet run                   → busca "hu.json" junto al ejecutable
//   dotnet run -- mi_historia.json
//
// El JSON debe seguir la estructura de "hu.json" incluido en el proyecto.

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

    static async Task<int> Main(string[] args)
    {
        // ── PASO 0: Cargar credenciales desde .env ───────────────────────────
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

        // ── PASO 1: Leer el JSON de parámetros ───────────────────────────────
        string jsonFile = args.Length > 0 ? args[0] : FindFile("hu.json");
        if (string.IsNullOrWhiteSpace(jsonFile) || !File.Exists(jsonFile))
        {
            Console.WriteLine($"ERROR: No se encontró el archivo JSON de parámetros: '{jsonFile ?? "hu.json"}'");
            Console.WriteLine("Uso: dotnet run [-- nombre_archivo.json]");
            return 1;
        }

        Console.WriteLine($"Leyendo parámetros desde: {jsonFile}\n");
        HuConfig cfg;
        try
        {
            var raw = await File.ReadAllTextAsync(jsonFile);
            cfg = JsonSerializer.Deserialize<HuConfig>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR leyendo/parseando el JSON: {ex.Message}");
            return 1;
        }

        // ── Validación mínima ────────────────────────────────────────────────
        if (cfg?.Hu is null)
        {
            Console.WriteLine("ERROR: El JSON no contiene la sección 'hu'.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(cfg.Hu.Title))
        {
            Console.WriteLine("ERROR: El campo 'hu.title' es obligatorio.");
            return 1;
        }

        // ── PASO 2: Mostrar resumen de lo que se va a crear ──────────────────
        Console.WriteLine("══════════════════════════════════════════════════");
        Console.WriteLine("  PARÁMETROS CARGADOS");
        Console.WriteLine("══════════════════════════════════════════════════");
        Console.WriteLine($"  Iteración      : {cfg.IterationPath}");
        Console.WriteLine($"  Área           : {cfg.AreaPath}");
        Console.WriteLine($"  Título HU      : {cfg.Hu.Title}");
        Console.WriteLine($"  Prioridad      : {cfg.Hu.Priority}");
        Console.WriteLine($"  Riesgo         : {cfg.Hu.Risk}");
        Console.WriteLine($"  Tipo HU        : {cfg.Hu.TipoHU}");
        Console.WriteLine($"  Frente trabajo : {cfg.Hu.FrenteDeTrabajo}");
        Console.WriteLine($"  Asignado a     : {(string.IsNullOrWhiteSpace(cfg.Hu.AssignedTo) ? "(sin asignar)" : cfg.Hu.AssignedTo)}");
        Console.WriteLine($"  Fecha inicio   : {cfg.Hu.StartDate}");
        Console.WriteLine($"  Fecha fin      : {cfg.Hu.FinishDate}");
        Console.WriteLine($"  Test Cases     : {cfg.TestCases?.Count ?? 0}");
        Console.WriteLine("══════════════════════════════════════════════════\n");

        const string apiVersion = "7.1";
        using var client = new HttpClient();
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            // ── PASO 3: Crear la HU ──────────────────────────────────────────
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
                huId = await CreateUserStoryAsync(client, apiVersion, cfg);
                if (huId < 0) { Console.WriteLine("ERROR creando HU."); return 1; }
                Console.WriteLine($"[1] HU creada: ID={huId}");
                Console.WriteLine($"    {WiUrl(huId)}");
            }

            // ── PASO 4: Crear Test Cases ─────────────────────────────────────
            var createdTcs = new List<int>();
            if (cfg.TestCases is { Count: > 0 })
            {
                Console.WriteLine("\n[2] Creando Test Cases...");
                foreach (var tc in cfg.TestCases)
                {
                    int tcId = await CreateTestCaseAsync(client, apiVersion, cfg.IterationPath, cfg.AreaPath, tc);
                    if (tcId > 0)
                    {
                        createdTcs.Add(tcId);
                        Console.WriteLine($"    ID={tcId,-6} \"{tc.Title}\"");
                    }
                    else
                    {
                        Console.WriteLine($"    ERROR creando: \"{tc.Title}\"");
                    }
                }

                // ── PASO 5: Vincular TCs a la HU (Tested By) ────────────────
                Console.WriteLine("\n[3] Vinculando Test Cases a la HU (Tested By)...");
                foreach (var tcId in createdTcs)
                {
                    bool ok = await LinkTestedByAsync(client, apiVersion, huId, tcId);
                    Console.WriteLine(ok
                        ? $"    HU {huId} <──[Tested By]── TC {tcId}  OK"
                        : $"    ERROR vinculando TC {tcId}");
                }
            }
            else
            {
                Console.WriteLine("\n[2] No hay Test Cases definidos en el JSON, se omite este paso.");
            }

            // ── RESUMEN ──────────────────────────────────────────────────────
            Console.WriteLine("\n══════════════════════════════════════════════════");
            Console.WriteLine("  PROCESO FINALIZADO");
            Console.WriteLine("══════════════════════════════════════════════════");
            Console.WriteLine($"  Iteración : {cfg.IterationPath}");
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

    // ── Modelos ───────────────────────────────────────────────────────────────

    class HuConfig
    {
        public string           IterationPath { get; set; } = "";
        public string           AreaPath      { get; set; } = "";
        public HuFields         Hu            { get; set; } = new();
        public List<TcFields>   TestCases     { get; set; } = [];
    }

    class HuFields
    {
        public string Title              { get; set; } = "";
        public string Description        { get; set; } = "";
        public string AcceptanceCriteria { get; set; } = "";
        public int    Priority           { get; set; } = 0;
        public string Risk               { get; set; } = "";
        public string StartDate          { get; set; } = "";
        public string FinishDate         { get; set; } = "";
        public string ValueArea          { get; set; } = "";
        public string TipoHU             { get; set; } = "";
        public string FrenteDeTrabajo    { get; set; } = "";
        public string AssignedTo         { get; set; } = "";
    }

    class TcFields
    {
        public string Title    { get; set; } = "";
        public string Action   { get; set; } = "";
        public string Expected { get; set; } = "";
        /// <summary>Estado del TC al crearse. Dejar vacío para usar el estado por defecto del proceso.</summary>
        public string State    { get; set; } = "";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string WiUrl(int id) =>
        $"https://dev.azure.com/{org}/{project}/_workitems/edit/{id}";

    /// <summary>Busca un archivo subiendo desde el directorio del ejecutable.</summary>
    static string FindFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, fileName);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }
        return fileName; // devuelve el nombre tal cual; fallará en la validación de existencia
    }

    // Crea una User Story con todos los campos leídos del JSON.
    static async Task<int> CreateUserStoryAsync(HttpClient client, string api, HuConfig cfg)
    {
        var url = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$User%20Story?api-version={api}";
        var hu  = cfg.Hu;
        var patch = new List<Dictionary<string, object>>
        {
            new() { {"op","add"}, {"path","/fields/System.Title"},
                    {"value", hu.Title} },
            new() { {"op","add"}, {"path","/fields/System.Description"},
                    {"value", hu.Description} },
            new() { {"op","add"}, {"path","/fields/System.IterationPath"},
                    {"value", cfg.IterationPath} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.AcceptanceCriteria"},
                    {"value", hu.AcceptanceCriteria} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.Priority"},
                    {"value", hu.Priority} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.Risk"},
                    {"value", hu.Risk} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Scheduling.StartDate"},
                    {"value", hu.StartDate} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Scheduling.FinishDate"},
                    {"value", hu.FinishDate} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.Common.ValueArea"},
                    {"value", hu.ValueArea} },
            new() { {"op","add"}, {"path","/fields/Custom.TipoHU"},
                    {"value", hu.TipoHU} },
            new() { {"op","add"}, {"path","/fields/Custom.FrenteDeTrabajo"},
                    {"value", hu.FrenteDeTrabajo} },
        };
        if (!string.IsNullOrWhiteSpace(hu.AssignedTo))
            patch.Add(new() { {"op","add"}, {"path","/fields/System.AssignedTo"}, {"value", hu.AssignedTo} });
        if (!string.IsNullOrWhiteSpace(cfg.AreaPath))
            patch.Add(new() { {"op","add"}, {"path","/fields/System.AreaPath"}, {"value", cfg.AreaPath} });
        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue ? res.Value.GetProperty("id").GetInt32() : -1;
    }

    // Crea un Test Case con los pasos leídos del JSON.
    // Si el estado indicado no se puede asignar al crear, crea el TC en estado
    // predeterminado (Design) y luego hace una transición al estado deseado.
    static async Task<int> CreateTestCaseAsync(HttpClient client, string api,
        string iterationPath, string areaPath, TcFields tc)
    {
        var url   = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$Test%20Case?api-version={api}";
        var steps = $"<steps id=\"0\" last=\"1\">" +
                    $"<step id=\"1\" type=\"ActionStep\">" +
                    $"<parameterizedString isformatted=\"true\">{tc.Action}</parameterizedString>" +
                    $"<parameterizedString isformatted=\"true\">{tc.Expected}</parameterizedString>" +
                    $"<description/></step></steps>";

        var basePatch = new List<Dictionary<string, object>>
        {
            new() { {"op","add"}, {"path","/fields/System.Title"},              {"value", tc.Title} },
            new() { {"op","add"}, {"path","/fields/Microsoft.VSTS.TCM.Steps"}, {"value", steps} },
            new() { {"op","add"}, {"path","/fields/System.IterationPath"},      {"value", iterationPath} }
        };
        if (!string.IsNullOrWhiteSpace(areaPath))
            basePatch.Add(new() { {"op","add"}, {"path","/fields/System.AreaPath"}, {"value", areaPath} });

        // Intento 1: crear directamente con estado (funciona si el proceso lo permite)
        if (!string.IsNullOrWhiteSpace(tc.State))
        {
            var patchConEstado = new List<Dictionary<string, object>>(basePatch)
            {
                new() { {"op","add"}, {"path","/fields/System.State"}, {"value", tc.State} }
            };
            var res1 = await PatchWiAsync(client, url, patchConEstado, silentError: true);
            if (res1.HasValue)
                return res1.Value.GetProperty("id").GetInt32();
        }

        // Intento 2: crear sin estado (queda en Design) y luego transicionar
        var res2 = await PatchWiAsync(client, url, basePatch);
        if (!res2.HasValue) return -1;

        int tcId = res2.Value.GetProperty("id").GetInt32();

        if (!string.IsNullOrWhiteSpace(tc.State))
        {
            var patchUrl   = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{tcId}?api-version={api}";
            var statePatch = new List<Dictionary<string, object>>
            {
                new() { {"op","add"}, {"path","/fields/System.State"}, {"value", tc.State} }
            };
            var res3 = await PatchWiAsync(client, patchUrl, statePatch, silentError: true);
            if (res3.HasValue)
                Console.WriteLine($"      Estado actualizado a '{tc.State}'");
            else
                Console.WriteLine($"      AVISO: No se pudo transicionar al estado '{tc.State}'. TC queda en 'Design'.");
        }

        return tcId;
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
        List<Dictionary<string, object>> patch, bool silentError = false)
    {
        var json = JsonSerializer.Serialize(patch);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json-patch+json");
        using var req  = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
        using var resp = await client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            if (!silentError)
                Console.WriteLine($"    HTTP {resp.StatusCode}: {body}");
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    // Lee el archivo .env más cercano y registra cada KEY=VALUE como variable de entorno.
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
