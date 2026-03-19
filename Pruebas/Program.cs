using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;

// ── Cargar .env ──────────────────────────────────────────────────────────────
EnvLoader.LoadDotEnv();

var org = Environment.GetEnvironmentVariable("AZDO_ORG") ?? "";
var project = Environment.GetEnvironmentVariable("AZDO_PROJECT") ?? "";
var pat = Environment.GetEnvironmentVariable("AZDO_PAT") ?? "";
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

// ── Configurar app ───────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.Urls.Add("http://localhost:5000");
app.UseCors();

// ══════════════════════════════════════════════════════════════════════════════
//  POST /api/generate  — Recibe un prompt, llama a OpenAI, devuelve el JSON
// ══════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/generate", async (GenerateRequest req) =>
{
    if (string.IsNullOrWhiteSpace(openAiKey))
        return Results.BadRequest(new { error = "OPENAI_API_KEY no está configurada en el archivo .env del backend." });
    if (string.IsNullOrWhiteSpace(req.Prompt))
        return Results.BadRequest(new { error = "El prompt es requerido." });

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiKey.Trim());

    var body = JsonSerializer.Serialize(new
    {
        model = string.IsNullOrWhiteSpace(req.Model) ? "gpt-4.1-mini" : req.Model.Trim(),
        input = new object[]
        {
            new { role = "system", content = "Eres un asistente que responde solo JSON valido para una HU. No agregues texto fuera del JSON." },
            new { role = "user",   content = req.Prompt }
        }
    });

    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    using var resp = await http.PostAsync("https://api.openai.com/v1/responses", content);
    var respBody = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
        return Results.Problem($"OpenAI devolvió {(int)resp.StatusCode}: {respBody}");

    using var doc = JsonDocument.Parse(respBody);
    var outputText = AiHelpers.ExtractOutputText(doc.RootElement);
    var jsonStr = AiHelpers.ExtractJsonFromText(outputText);

    try
    {
        var parsed = JsonSerializer.Deserialize<HuConfig>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed?.Hu is null)
            return Results.BadRequest(new { error = "El JSON generado no contiene la sección 'hu'.", raw = outputText });
        return Results.Ok(new { json = jsonStr, raw = outputText });
    }
    catch
    {
        return Results.Ok(new { json = jsonStr, raw = outputText, warning = "El JSON generado no pudo validarse como HuConfig." });
    }
});

// ══════════════════════════════════════════════════════════════════════════════
//  POST /api/create  — Recibe un JSON de HU y lo crea en Azure DevOps
// ══════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/create", async (HuConfig cfg) =>
{
    if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(pat))
        return Results.BadRequest(new { error = "Faltan variables AZDO_ORG, AZDO_PROJECT o AZDO_PAT en el archivo .env." });
    if (cfg?.Hu is null || string.IsNullOrWhiteSpace(cfg.Hu.Title))
        return Results.BadRequest(new { error = "El JSON debe contener hu.title." });

    const string apiVersion = "7.1";
    using var client = new HttpClient();
    var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    var azdo = new AzDoService(org, project, client, apiVersion);

    try
    {
        // Crear HU
        int huId = await azdo.CreateUserStoryAsync(cfg);
        if (huId < 0) return Results.Problem("Error creando la User Story en Azure DevOps.");

        // Crear Test Cases y vincular
        var createdTcs = new List<CreatedItem>();
        if (cfg.TestCases is { Count: > 0 })
        {
            foreach (var tc in cfg.TestCases)
            {
                int tcId = await azdo.CreateTestCaseAsync(cfg.IterationPath, cfg.AreaPath, tc);
                if (tcId > 0)
                {
                    await azdo.LinkTestedByAsync(huId, tcId);
                    createdTcs.Add(new CreatedItem(tcId, tc.Title, azdo.WiUrl(tcId)));
                }
            }
        }

        // Crear Requirement Based Suite
        int suiteId = -1;
        if (cfg.TestSuite is not null)
        {
            int planId = cfg.TestSuite.PlanId;
            if (planId == 0 && !string.IsNullOrWhiteSpace(cfg.TestSuite.PlanName))
                planId = await azdo.ResolveTestPlanIdAsync(cfg.TestSuite.PlanName);

            if (planId > 0)
                suiteId = await azdo.CreateRequirementSuiteAsync(planId, huId);
        }

        return Results.Ok(new
        {
            huId,
            huUrl = azdo.WiUrl(huId),
            testCases = createdTcs,
            suiteId,
            message = $"HU #{huId} creada con {createdTcs.Count} Test Cases."
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error inesperado: {ex.Message}");
    }
});

app.Run();

// ══════════════════════════════════════════════════════════════════════════════
//  Request / Response DTOs
// ══════════════════════════════════════════════════════════════════════════════

record GenerateRequest(string Prompt, string? Model);
record CreatedItem(int Id, string Title, string Url);

// ══════════════════════════════════════════════════════════════════════════════
//  Modelos del JSON de HU (compartidos entre generación y creación)
// ══════════════════════════════════════════════════════════════════════════════

class HuConfig
{
    public string IterationPath { get; set; } = "";
    public string AreaPath { get; set; } = "";
    public HuFields Hu { get; set; } = new();
    public List<TcFields> TestCases { get; set; } = [];
    public TestSuiteConfig? TestSuite { get; set; }
}

class TestSuiteConfig
{
    public int PlanId { get; set; } = 0;
    public string PlanName { get; set; } = "";
}

class HuFields
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public int Priority { get; set; } = 0;
    public string Risk { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string FinishDate { get; set; } = "";
    public string ValueArea { get; set; } = "";
    public string TipoHU { get; set; } = "";
    public string FrenteDeTrabajo { get; set; } = "";
    public string AssignedTo { get; set; } = "";
}

class TcFields
{
    public string Title { get; set; } = "";
    public string Action { get; set; } = "";
    public string Expected { get; set; } = "";
    public string State { get; set; } = "";
}

// ══════════════════════════════════════════════════════════════════════════════
//  Helpers de IA (extraer texto / JSON de la respuesta de OpenAI)
// ══════════════════════════════════════════════════════════════════════════════

static class AiHelpers
{
    public static string ExtractOutputText(JsonElement root)
    {
        var sb = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var c in content.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var text))
                        sb.AppendLine(text.GetString());
                }
            }
        }
        return sb.ToString();
    }

    public static string ExtractJsonFromText(string rawText)
    {
        // Intentar extraer de bloque de código fenced
        var match = Regex.Match(rawText, @"```json\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(rawText, @"```\s*([\s\S]*?)\s*```");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Extraer desde primera { hasta última }
        int start = rawText.IndexOf('{');
        int end = rawText.LastIndexOf('}');
        if (start >= 0 && end > start)
            return rawText[start..(end + 1)].Trim();

        return rawText.Trim();
    }
}

// ══════════════════════════════════════════════════════════════════════════════
//  Servicio Azure DevOps (toda la lógica de creación de Work Items)
// ══════════════════════════════════════════════════════════════════════════════

class AzDoService
{
    readonly string _org;
    readonly string _project;
    readonly HttpClient _client;
    readonly string _api;

    public AzDoService(string org, string project, HttpClient client, string apiVersion)
    {
        _org = org;
        _project = project;
        _client = client;
        _api = apiVersion;
    }

    public string WiUrl(int id) =>
        $"https://dev.azure.com/{_org}/{_project}/_workitems/edit/{id}";

    static Dictionary<string, object> Op(string path, object value) =>
        new() { { "op", "add" }, { "path", path }, { "value", value } };

    public async Task<int> CreateUserStoryAsync(HuConfig cfg)
    {
        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/wit/workitems/$User%20Story?api-version={_api}";
        var hu = cfg.Hu;
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

        var res = await PatchWiAsync(url, patch);
        return res.HasValue ? res.Value.GetProperty("id").GetInt32() : -1;
    }

    public async Task<int> CreateTestCaseAsync(string iterationPath, string areaPath, TcFields tc)
    {
        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/wit/workitems/$Test%20Case?api-version={_api}";
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

        var res = await PatchWiAsync(url, patch);
        if (!res.HasValue) return -1;

        int tcId = res.Value.GetProperty("id").GetInt32();

        if (!string.IsNullOrWhiteSpace(tc.State))
        {
            var patchUrl = $"https://dev.azure.com/{_org}/{_project}/_apis/wit/workitems/{tcId}?api-version={_api}";
            var stateRes = await PatchWiAsync(patchUrl, [Op("/fields/System.State", tc.State)]);
            if (!stateRes.HasValue)
                Console.WriteLine($"AVISO: No se pudo transicionar TC {tcId} al estado '{tc.State}'.");
        }

        return tcId;
    }

    public async Task<int> ResolveTestPlanIdAsync(string planName)
    {
        string? continuationToken = null;
        do
        {
            var tokenParam = continuationToken != null ? $"&continuationToken={Uri.EscapeDataString(continuationToken)}" : "";
            var url = $"https://dev.azure.com/{_org}/{_project}/_apis/testplan/plans?$top=50{tokenParam}&api-version={_api}";
            using var resp = await _client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return -1;
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            foreach (var plan in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var name = plan.GetProperty("name").GetString() ?? "";
                if (name.Equals(planName, StringComparison.OrdinalIgnoreCase))
                    return plan.GetProperty("id").GetInt32();
            }
            resp.Headers.TryGetValues("x-ms-continuationtoken", out var tokens);
            continuationToken = tokens?.FirstOrDefault();
        } while (continuationToken != null);

        return -1;
    }

    public async Task<int> CreateRequirementSuiteAsync(int planId, int requirementId)
    {
        var planUrl = $"https://dev.azure.com/{_org}/{_project}/_apis/testplan/plans/{planId}?api-version={_api}";
        using var planResp = await _client.GetAsync(planUrl);
        if (!planResp.IsSuccessStatusCode) return -1;
        using var planDoc = JsonDocument.Parse(await planResp.Content.ReadAsStringAsync());
        int rootSuiteId = planDoc.RootElement.GetProperty("rootSuite").GetProperty("id").GetInt32();

        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/testplan/plans/{planId}/suites?api-version={_api}";
        var body = JsonSerializer.Serialize(new
        {
            suiteType = "requirementTestSuite",
            parentSuite = new { id = rootSuiteId },
            requirementId
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _client.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode) return -1;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root[0].GetProperty("id").GetInt32();
        return root.GetProperty("id").GetInt32();
    }

    public async Task<bool> LinkTestedByAsync(int huId, int tcId)
    {
        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/wit/workitems/{huId}?api-version={_api}";
        var tcUrl = $"https://dev.azure.com/{_org}/{_project}/_apis/wit/workitems/{tcId}";
        var patch = new List<Dictionary<string, object>>
        {
            Op("/relations/-", new Dictionary<string, object>
            {
                { "rel",        "Microsoft.VSTS.Common.TestedBy-Forward" },
                { "url",        tcUrl },
                { "attributes", new Dictionary<string, object> { { "comment", "Vinculado automáticamente" } } },
            })
        };
        var res = await PatchWiAsync(url, patch);
        return res.HasValue;
    }

    async Task<JsonElement?> PatchWiAsync(string url, List<Dictionary<string, object>> patch)
    {
        var json = JsonSerializer.Serialize(patch);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json-patch+json");
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        using var resp = await _client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"HTTP {resp.StatusCode}: {body}");
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }
}

// ══════════════════════════════════════════════════════════════════════════════
//  Carga de archivo .env
// ══════════════════════════════════════════════════════════════════════════════

static class EnvLoader
{
    public static void LoadDotEnv()
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
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
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
