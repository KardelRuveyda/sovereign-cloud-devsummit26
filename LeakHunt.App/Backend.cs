using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using System.ClientModel;

static class Backend
{
    // Sends prompts to a hosted OpenAI-compatible endpoint (via the egress gateway on localhost:5099).
    public static IChatClient Cloud()
    {
        var endpoint = Environment.GetEnvironmentVariable("CLOUD_ENDPOINT") ?? "http://127.0.0.1:5099/v1";
        var key = Environment.GetEnvironmentVariable("CLOUD_API_KEY") ?? "not-a-real-key";
        var model = Environment.GetEnvironmentVariable("CLOUD_MODEL") ?? "gpt-4o-mini";
        Info = $"CLOUD  {model}  ->  {endpoint}";
        return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(model)
            .AsIChatClient();
    }

    // Foundry Local on Azure Local: same client, pointed at the cluster's gateway URL with an API key.
    public static IChatClient Edge()
    {
        var endpoint = Environment.GetEnvironmentVariable("EDGE_ENDPOINT") ?? throw new InvalidOperationException("Set EDGE_ENDPOINT, e.g. https://<gateway>/<deployment-name>/v1");
        var key = Environment.GetEnvironmentVariable("EDGE_API_KEY") ?? throw new InvalidOperationException("Set EDGE_API_KEY");
        var model = Environment.GetEnvironmentVariable("EDGE_MODEL") ?? "phi-3.5-mini";
        Info = $"EDGE   {model}  ->  {endpoint}";
        return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(model)
            .AsIChatClient();
    }

    // Runs the model on this machine with Foundry Local. Nothing leaves the device.
    public static async Task<IChatClient> LocalAsync(string? alias = null)
    {
        alias ??= Environment.GetEnvironmentVariable("LH_MODEL") ?? "phi-3.5-mini";
        var config = new Configuration
        {
            AppName = "leakhunt",
            ModelCacheDir = ModelCache.Dir,
            LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
            Web = new Configuration.WebService { Urls = "http://127.0.0.1:52495" }
        };
        Console.WriteLine("Starting local model (Foundry Local) ...");
        var sw = System.Diagnostics.Stopwatch.StartNew(); double Lap() { var s = sw.Elapsed.TotalSeconds; sw.Restart(); return s; }
        await FoundryLocalManager.CreateAsync(config, NullLogger.Instance); var tInit = Lap();
        var mgr = FoundryLocalManager.Instance;

        try { await mgr.DownloadAndRegisterEpsAsync(); }   // needs network only on first run
        catch (Exception) { /* already cached: continue offline */ }
        var tEps = Lap();

        var catalog = await mgr.GetCatalogAsync();
        var model = await catalog.GetModelAsync(alias) ?? throw new Exception($"Model '{alias}' not found"); var tCatalog = Lap();
        await model.DownloadAsync(_ => { }); var tDownload = Lap();   // no-op when cached
        await model.LoadAsync(); var tLoad = Lap();
        await mgr.StartWebServiceAsync(); var tWeb = Lap();
        Console.WriteLine($"Model loaded on this device: {model.Id}");
        Console.WriteLine($"  startup: SDK init {tInit:F1} s | execution providers {tEps:F1} s | catalog {tCatalog:F1} s | download check {tDownload:F1} s | model load {tLoad:F1} s | web service {tWeb:F1} s");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await http.GetStringAsync(config.Web.Urls + "/v1/models");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var ids = doc.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToArray();
            Console.WriteLine($"Models available in this Foundry Local service (downloaded): {string.Join(", ", ids)}");
        }
        catch (Exception) { /* listing is informational only */ }

        var endpoint = config.Web.Urls + "/v1";
        Info = $"LOCAL  {model.Id}  ->  {endpoint}";
        return new OpenAIClient(new ApiKeyCredential("local"), new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(model.Id)
            .AsIChatClient();
    }

    public static string Info { get; private set; } = "";
}
