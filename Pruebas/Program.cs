using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

/// <summary>
/// Herramienta de línea de comandos que lee un archivo JSON con los parámetros de una
/// Historia de Usuario (HU) y sus Test Cases, los crea en Azure DevOps mediante la REST API
/// y establece las relaciones Tested By y Requirement Suite.
/// <para>Uso: <c>dotnet run [-- archivo.json]</c>  (por defecto busca <c>hu.json</c>)</para>
/// </summary>
class Program
{
    static string org     = "";
    static string project = "";
    static string pat     = "";

    /// <summary>Opciones de deserialización JSON reutilizadas en toda la aplicación.</summary>
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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
            cfg = JsonSerializer.Deserialize<HuConfig>(raw, JsonOpts)!;
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

            // ── PASO 6: Crear Requirement Based Suite ──────────────────────
            int createdSuiteId = -1;
            if (cfg.TestSuite is not null)
            {
                int planId = cfg.TestSuite.PlanId;

                if (planId == 0 && !string.IsNullOrWhiteSpace(cfg.TestSuite.PlanName))
                {
                    Console.WriteLine($"\n[4] Buscando Test Plan \"{cfg.TestSuite.PlanName}\"...");
                    planId = await ResolveTestPlanIdAsync(client, apiVersion, cfg.TestSuite.PlanName);
                    if (planId > 0)
                        Console.WriteLine($"    Plan encontrado: ID={planId}");
                    else
                        Console.WriteLine($"    ERROR: No se encontró ningún Test Plan con el nombre \"{cfg.TestSuite.PlanName}\".");
                }

                if (planId > 0)
                {
                    Console.WriteLine($"\n[4] Creando Requirement Based Suite (vinculada a HU {huId})...");
                    createdSuiteId = await CreateRequirementSuiteAsync(client, apiVersion, planId, huId);
                    if (createdSuiteId > 0)
                        Console.WriteLine($"    Suite creada: ID={createdSuiteId}  (los TCs se incluyen automáticamente vía 'Tested By')");
                    else
                        Console.WriteLine("    ERROR creando el Requirement Based Suite.");
                }
            }

            // ── RESUMEN ──────────────────────────────────────────────────────
            Console.WriteLine("\n══════════════════════════════════════════════════");
            Console.WriteLine("  PROCESO FINALIZADO");
            Console.WriteLine("══════════════════════════════════════════════════");
            Console.WriteLine($"  Iteración : {cfg.IterationPath}");
            Console.WriteLine($"  HU        : {WiUrl(huId)}");
            Console.WriteLine($"  Test Cases: {createdTcs.Count} creados y vinculados");
            if (createdSuiteId > 0)
                Console.WriteLine($"  Test Suite: ID={createdSuiteId}");
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

    /// <summary>Raíz del archivo JSON de configuración.</summary>
    class HuConfig
    {
        /// <summary>Ruta de iteración (ej: <c>"Proyecto\\Sprint 1"</c>).</summary>
        public string           IterationPath { get; set; } = "";
        /// <summary>Ruta del área (ej: <c>"Proyecto\\Equipo"</c>).</summary>
        public string           AreaPath      { get; set; } = "";
        /// <summary>Campos de la Historia de Usuario.</summary>
        public HuFields         Hu            { get; set; } = new();
        /// <summary>Test Cases asociados a la HU.</summary>
        public List<TcFields>   TestCases     { get; set; } = [];
        /// <summary>Configuración del Test Suite. Opcional; si se omite no se crea suite.</summary>
        public TestSuiteConfig? TestSuite     { get; set; }
    }

    /// <summary>Referencia al Test Plan donde se creará el Requirement Suite.</summary>
    class TestSuiteConfig
    {
        /// <summary>ID directo del Test Plan. Si es 0 se resuelve automáticamente por <see cref="PlanName"/>.</summary>
        public int    PlanId   { get; set; } = 0;
        /// <summary>Nombre exacto del Test Plan (alternativa a <see cref="PlanId"/>).</summary>
        public string PlanName { get; set; } = "";
    }

    /// <summary>Campos de la Historia de Usuario mapeados al JSON.</summary>
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
        /// <summary>Correo o nombre del usuario. Vacío deja el work item sin asignar.</summary>
        public string AssignedTo         { get; set; } = "";
    }

    /// <summary>Campos de un Test Case mapeados al JSON.</summary>
    class TcFields
    {
        public string Title    { get; set; } = "";
        public string Action   { get; set; } = "";
        public string Expected { get; set; } = "";
        /// <summary>
        /// Estado deseado al crear el TC (ej: <c>"Ready"</c>, <c>"Design"</c>).
        /// Si está vacío, Azure DevOps asigna el estado por defecto (<c>"Design"</c>).
        /// </summary>
        public string State    { get; set; } = "";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Genera la URL directa al work item en el portal de Azure DevOps.</summary>
    static string WiUrl(int id) =>
        $"https://dev.azure.com/{org}/{project}/_workitems/edit/{id}";

    /// <summary>
    /// Construye una operación <c>add</c> para JSON Patch (Work Items API),
    /// reduciendo la verbosidad de los payloads de creación y edición.
    /// </summary>
    static Dictionary<string, object> Op(string path, object value) =>
        new() { { "op", "add" }, { "path", path }, { "value", value } };

    /// <summary>
    /// Busca un archivo subiendo directorios desde la ubicación del ejecutable.
    /// Si no lo encuentra devuelve el nombre tal cual, que fallará en la validación de existencia.
    /// </summary>
    static string FindFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, fileName);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }
        return fileName;
    }

    /// <summary>
    /// Crea una User Story en Azure DevOps con todos los campos definidos en <paramref name="cfg"/>.
    /// </summary>
    /// <returns>ID del work item creado, o -1 si la operación falla.</returns>
    static async Task<int> CreateUserStoryAsync(HttpClient client, string api, HuConfig cfg)
    {
        var url = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$User%20Story?api-version={api}";
        var hu  = cfg.Hu;
        var patch = new List<Dictionary<string, object>>
        {
            Op("/fields/System.Title",                             hu.Title),
            Op("/fields/System.Description",                       hu.Description),
            Op("/fields/System.IterationPath",                     cfg.IterationPath),
            Op("/fields/Microsoft.VSTS.Common.AcceptanceCriteria", hu.AcceptanceCriteria),
            Op("/fields/Microsoft.VSTS.Common.Priority",           hu.Priority),
            Op("/fields/Microsoft.VSTS.Common.Risk",               hu.Risk),
            Op("/fields/Microsoft.VSTS.Scheduling.StartDate",      hu.StartDate),
            Op("/fields/Microsoft.VSTS.Scheduling.FinishDate",     hu.FinishDate),
            Op("/fields/Microsoft.VSTS.Common.ValueArea",          hu.ValueArea),
            Op("/fields/Custom.TipoHU",                            hu.TipoHU),
            Op("/fields/Custom.FrenteDeTrabajo",                   hu.FrenteDeTrabajo),
        };
        if (!string.IsNullOrWhiteSpace(hu.AssignedTo))
            patch.Add(Op("/fields/System.AssignedTo", hu.AssignedTo));
        if (!string.IsNullOrWhiteSpace(cfg.AreaPath))
            patch.Add(Op("/fields/System.AreaPath", cfg.AreaPath));

        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue ? res.Value.GetProperty("id").GetInt32() : -1;
    }

    /// <summary>
    /// Crea un Test Case en Azure DevOps con título, pasos y resultado esperado.
    /// Si <see cref="TcFields.State"/> está definido, realiza un segundo PATCH para
    /// transicionar al estado deseado (Azure DevOps no admite asignar estado en la creación).
    /// </summary>
    /// <returns>ID del work item creado, o -1 si la operación falla.</returns>
    static async Task<int> CreateTestCaseAsync(HttpClient client, string api,
        string iterationPath, string areaPath, TcFields tc)
    {
        var url   = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/$Test%20Case?api-version={api}";
        var steps = $"<steps id=\"0\" last=\"1\">" +
                    $"<step id=\"1\" type=\"ActionStep\">" +
                    $"<parameterizedString isformatted=\"true\">{tc.Action}</parameterizedString>" +
                    $"<parameterizedString isformatted=\"true\">{tc.Expected}</parameterizedString>" +
                    $"<description/></step></steps>";

        var patch = new List<Dictionary<string, object>>
        {
            Op("/fields/System.Title",             tc.Title),
            Op("/fields/Microsoft.VSTS.TCM.Steps", steps),
            Op("/fields/System.IterationPath",     iterationPath),
        };
        if (!string.IsNullOrWhiteSpace(areaPath))
            patch.Add(Op("/fields/System.AreaPath", areaPath));

        var res = await PatchWiAsync(client, url, patch);
        if (!res.HasValue) return -1;

        int tcId = res.Value.GetProperty("id").GetInt32();

        if (!string.IsNullOrWhiteSpace(tc.State))
        {
            var patchUrl = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{tcId}?api-version={api}";
            var stateRes = await PatchWiAsync(client, patchUrl, [Op("/fields/System.State", tc.State)]);
            if (!stateRes.HasValue)
                Console.WriteLine($"      AVISO: No se pudo transicionar al estado '{tc.State}'. TC queda en 'Design'.");
        }

        return tcId;
    }

    /// <summary>
    /// Busca un Test Plan por nombre recorriendo todas las páginas de resultados
    /// mediante el header <c>x-ms-continuationtoken</c> de la API.
    /// Si no lo encuentra, imprime la lista completa de planes disponibles para diagnóstico.
    /// </summary>
    /// <returns>ID del plan encontrado, o -1 si no existe ninguno con ese nombre.</returns>
    static async Task<int> ResolveTestPlanIdAsync(HttpClient client, string api, string planName)
    {
        string? continuationToken = null;
        var allPlans = new List<(int Id, string Name)>();

        do
        {
            var tokenParam = continuationToken != null ? $"&continuationToken={Uri.EscapeDataString(continuationToken)}" : "";
            var url = $"https://dev.azure.com/{org}/{project}/_apis/testplan/plans?$top=50{tokenParam}&api-version={api}";
            using var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"    HTTP {resp.StatusCode} al listar planes: {await resp.Content.ReadAsStringAsync()}");
                return -1;
            }
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            foreach (var plan in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var id   = plan.GetProperty("id").GetInt32();
                var name = plan.GetProperty("name").GetString() ?? "";
                if (name.Equals(planName, StringComparison.OrdinalIgnoreCase))
                    return id;
                allPlans.Add((id, name));
            }

            resp.Headers.TryGetValues("x-ms-continuationtoken", out var tokens);
            continuationToken = tokens?.FirstOrDefault();

        } while (continuationToken != null);

        // Plan no encontrado: mostrar los planes disponibles para diagnóstico
        Console.WriteLine($"    Planes disponibles en el proyecto ({allPlans.Count} encontrados):");
        foreach (var (id, name) in allPlans)
            Console.WriteLine($"      ID={id,-6} \"{name}\"");
        return -1;
    }

    /// <summary>
    /// Crea un Requirement Based Suite dentro del Test Plan indicado, vinculado a la HU.
    /// Azure DevOps incluye automáticamente en el suite todos los Test Cases que tienen
    /// relación "Tested By" con esa HU.
    /// </summary>
    /// <returns>ID del suite creado, o -1 si la operación falla.</returns>
    static async Task<int> CreateRequirementSuiteAsync(HttpClient client, string api, int planId, int requirementId)
    {
        // El suite raíz del plan es el parent obligatorio para crear sub-suites
        var planUrl  = $"https://dev.azure.com/{org}/{project}/_apis/testplan/plans/{planId}?api-version={api}";
        using var planResp = await client.GetAsync(planUrl);
        if (!planResp.IsSuccessStatusCode)
        {
            Console.WriteLine($"    HTTP {planResp.StatusCode} al leer el plan {planId}: {await planResp.Content.ReadAsStringAsync()}");
            return -1;
        }
        using var planDoc = JsonDocument.Parse(await planResp.Content.ReadAsStringAsync());
        int rootSuiteId = planDoc.RootElement.GetProperty("rootSuite").GetProperty("id").GetInt32();

        var url  = $"https://dev.azure.com/{org}/{project}/_apis/testplan/plans/{planId}/suites?api-version={api}";
        var body = JsonSerializer.Serialize(new
        {
            suiteType     = "requirementTestSuite",
            parentSuite   = new { id = rootSuiteId },
            requirementId = requirementId
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp    = await client.PostAsync(url, content);
        var respBody = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"    HTTP {resp.StatusCode}: {respBody}");
            return -1;
        }
        using var doc = JsonDocument.Parse(respBody);
        // La respuesta puede ser un array (un suite por requirementId)
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root[0].GetProperty("id").GetInt32();
        return root.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Agrega la relación "Tested By" desde la HU hacia el Test Case.
    /// Esta relación es la que permite que el Requirement Suite incluya automáticamente el TC.
    /// </summary>
    static async Task<bool> LinkTestedByAsync(HttpClient client, string api, int huId, int tcId)
    {
        var url   = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{huId}?api-version={api}";
        var tcUrl = $"https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{tcId}";
        var patch = new List<Dictionary<string, object>>
        {
            Op("/relations/-", new Dictionary<string, object>
            {
                { "rel",        "Microsoft.VSTS.Common.TestedBy-Forward" },
                { "url",        tcUrl },
                { "attributes", new Dictionary<string, object> { { "comment", "Vinculado automáticamente" } } },
            })
        };
        var res = await PatchWiAsync(client, url, patch);
        return res.HasValue;
    }

    /// <summary>
    /// Envía un PATCH <c>application/json-patch+json</c> a la Work Items API de Azure DevOps.
    /// </summary>
    /// <returns>El <see cref="JsonElement"/> raíz de la respuesta, o <c>null</c> si falla.</returns>
    static async Task<JsonElement?> PatchWiAsync(HttpClient client, string url,
        List<Dictionary<string, object>> patch)
    {
        var json = JsonSerializer.Serialize(patch);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json-patch+json");
        using var req  = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
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

    /// <summary>
    /// Busca el archivo <c>.env</c> más cercano subiendo desde el directorio del ejecutable
    /// y registra cada línea <c>KEY=VALUE</c> como variable de entorno del proceso.
    /// Las variables ya definidas en el entorno del sistema no se sobreescriben.
    /// </summary>
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
