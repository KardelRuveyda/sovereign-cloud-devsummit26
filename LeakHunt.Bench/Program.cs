using System.Diagnostics;
using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using System.ClientModel;

// Usage: LeakHunt.Bench <alias> [outDir]
// One model per process, so memory and load time are measured cleanly.
var alias = args.Length > 0 ? args[0] : "phi-4-mini";
var outDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "reports");
Directory.CreateDirectory(outDir);

var docs = new Retriever(Path.Combine(AppContext.BaseDirectory, "docs"));
var proc = Process.GetCurrentProcess();
var sw = Stopwatch.StartNew();

var config = new Configuration { AppName = "leakhunt",
    ModelCacheDir = ModelCache.Dir, LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning, Web = new Configuration.WebService { Urls = "http://127.0.0.1:52496" } };
await FoundryLocalManager.CreateAsync(config, NullLogger.Instance);
var mgr = FoundryLocalManager.Instance;
try { await mgr.DownloadAndRegisterEpsAsync(); } catch (Exception) { }

var catalog = await mgr.GetCatalogAsync();
var model = await catalog.GetModelAsync(alias) ?? throw new Exception($"Model '{alias}' not found");
var dl = Stopwatch.StartNew();
await model.DownloadAsync(_ => { });
var downloadSec = dl.Elapsed.TotalSeconds;
var load = Stopwatch.StartNew();
await model.LoadAsync();
var loadSec = load.Elapsed.TotalSeconds;
await mgr.StartWebServiceAsync();
var endpoint = config.Web.Urls + "/v1";
IChatClient chat = new OpenAIClient(new ApiKeyCredential("local"), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(model.Id).AsIChatClient();
Console.WriteLine($"[{alias}] loaded as {model.Id} in {loadSec:F1}s");

const string System = "You are an internal knowledge assistant. Answer ONLY from the context. If the context does not contain the answer, say you do not know. Be concise. Cite the source file names in square brackets.";

async Task<(string text, double ttft, double total, int chunks)> Ask(string question)
{
    var hits = docs.Search(question, k: 2);
    var context = string.Join("\n---\n", hits.Select(h => $"[{h.Source}]\n{h.Text}"));
    var messages = new List<ChatMessage> { new(ChatRole.System, System), new(ChatRole.User, $"Context:\n{context}\n\nQuestion: {question}") };
    var t = Stopwatch.StartNew();
    double first = -1; var text = new System.Text.StringBuilder(); var n = 0;
    await foreach (var u in chat.GetStreamingResponseAsync(messages, new ChatOptions { Temperature = 0f, MaxOutputTokens = 200 }))
    {
        if (!string.IsNullOrEmpty(u.Text)) { if (first < 0) first = t.Elapsed.TotalSeconds; text.Append(u.Text); n++; }
    }
    return (text.ToString().Trim(), first < 0 ? t.Elapsed.TotalSeconds : first, t.Elapsed.TotalSeconds, n);
}

// warm-up, not scored
await Ask("What is the VPN access policy for contractors?");

var rows = new List<object>();
var correct = 0; var cited = 0; var abstainOk = 0; var halluc = 0; var answerQs = 0; var abstainQs = 0;
double ttftSum = 0, totalSum = 0; long chunkSum = 0; double genSecSum = 0;
foreach (var q in Questions.All)
{
    var (text, ttft, total, chunks) = await Ask(q.Text);
    bool ok, cit = false, hall = false;
    if (q.Kind == "answer") { answerQs++; ok = Scoring.FactsPresent(text, q); cit = Scoring.Cited(text, q); if (ok) correct++; if (cit) cited++; }
    else { abstainQs++; ok = Scoring.Abstained(text); if (ok) abstainOk++; else { hall = true; halluc++; } }
    ttftSum += ttft; totalSum += total; chunkSum += chunks; genSecSum += Math.Max(total - ttft, 0.001);
    Console.WriteLine($"  {q.Id} {(ok ? "PASS" : "FAIL")}  ttft={ttft:F2}s total={total:F2}s  {text.Replace("\n", " ")[..Math.Min(90, text.Replace("\n", " ").Length)]}");
    rows.Add(new { q.Id, q.Kind, q.Text, answer = text, pass = ok, cited = cit, hallucinated = hall, ttftSec = Math.Round(ttft, 3), totalSec = Math.Round(total, 3), chunks });
}
proc.Refresh();
var n = Questions.All.Length;
var summary = new
{
    alias, modelId = model.Id, machine = Environment.MachineName.Length > 0 ? "redacted" : "", date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    downloadSec = Math.Round(downloadSec, 1), loadSec = Math.Round(loadSec, 1), workingSetMB = Math.Round(proc.WorkingSet64 / 1048576.0),
    accuracy = Math.Round(100.0 * correct / answerQs, 1), citation = Math.Round(100.0 * cited / answerQs, 1),
    abstain = Math.Round(100.0 * abstainOk / abstainQs, 1), hallucinations = halluc,
    avgTtftSec = Math.Round(ttftSum / n, 2), avgTotalSec = Math.Round(totalSum / n, 2), tokensPerSec = Math.Round(chunkSum / genSecSum, 1),
    answerQuestions = answerQs, abstainQuestions = abstainQs,
};
await File.WriteAllTextAsync(Path.Combine(outDir, $"{alias}.json"), JsonSerializer.Serialize(new { summary, rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"[{alias}] accuracy {summary.accuracy}%  citation {summary.citation}%  abstain {summary.abstain}%  ttft {summary.avgTtftSec}s  ~{summary.tokensPerSec} chunks/s  load {summary.loadSec}s");

await mgr.StopWebServiceAsync();
await model.UnloadAsync();
