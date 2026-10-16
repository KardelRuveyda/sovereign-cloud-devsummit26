using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Egress Traffic Monitor: stands in for the corporate egress proxy / AI gateway.
// It shows exactly what an outbound AI call carries. If CLOUD_UPSTREAM is unset it answers with a mock model.
var upstream = Environment.GetEnvironmentVariable("CLOUD_UPSTREAM")?.TrimEnd('/');
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5099");
var app = builder.Build();
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var count = 0;
const string R = "\u001b[91m", G = "\u001b[92m", Y = "\u001b[93m", C = "\u001b[96m", D = "\u001b[90m", B = "\u001b[1m", X = "\u001b[0m";

var detectors = new (string Name, Regex Rx)[]
{
    ("national ID", new(@"\b\d{11}\b")),
    ("IBAN", new(@"\bTR\d{2}(?:\s?\d{4}){5}\s?\d{2}\b")),
    ("email", new(@"[\w.+-]+@[\w-]+\.[\w.-]+")),
    ("phone", new(@"\+\d{2}\s?\d{2,3}\s?\d{3}\s?\d{2}\s?\d{2,4}")),
    ("amount", new(@"EUR\s?[\d.,]+\s?(?:million|M)?", RegexOptions.IgnoreCase)),
    ("secret keyword", new(@"walk-away|board confidential|retention bonus", RegexOptions.IgnoreCase)),
};

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"{B}{C}EGRESS TRAFFIC MONITOR{X}  listening on http://127.0.0.1:5099   upstream: {(upstream ?? "MOCK (no CLOUD_UPSTREAM)")}");
Console.WriteLine($"{D}Every AI request that leaves the app shows up here.{X}\n");

app.Map("/{**path}", async (HttpContext ctx, string? path) =>
{
    var n = Interlocked.Increment(ref count);
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var target = upstream is null ? "mock-cloud.example" : new Uri(upstream).Host;
    Print(n, ctx.Request.Method, "/" + path, target, body);

    if (upstream is null) { await Mock(ctx, body); return; }

    var rel = Regex.Replace("/" + path, "^/v1", "");
    using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), upstream + rel);
    if (ctx.Request.Headers.TryGetValue("Authorization", out var auth)) req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());
    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    ctx.Response.StatusCode = (int)resp.StatusCode;
    ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    await using var s = await resp.Content.ReadAsStreamAsync();
    var buf = new byte[4096]; int read;
    while ((read = await s.ReadAsync(buf)) > 0) { await ctx.Response.Body.WriteAsync(buf.AsMemory(0, read)); await ctx.Response.Body.FlushAsync(); }
});

app.Run();

void Print(int n, string method, string path, string host, string body)
{
    string model = "?", stream = "?"; var userText = "";
    try
    {
        using var doc = JsonDocument.Parse(body);
        model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() ?? "?" : "?";
        stream = doc.RootElement.TryGetProperty("stream", out var st) ? st.ToString() : "false";
        foreach (var msg in doc.RootElement.GetProperty("messages").EnumerateArray())
            if (msg.GetProperty("role").GetString() == "user") userText = msg.GetProperty("content").ToString();
    }
    catch { userText = body; }

    Console.WriteLine($"{B}{R}OUTBOUND #{n}{X}  {DateTime.Now:HH:mm:ss}  {method} {path}  ->  {B}{host}{X}  ({body.Length / 1024.0:F1} KB)   model={model}  stream={stream}");
    Console.WriteLine($"{D}----------------------------------------------------------------------------{X}");
    var shown = userText.Length > 1100 ? userText[..1100] + " ..." : userText;
    foreach (var d in detectors) shown = d.Rx.Replace(shown, mt => $"{R}{B}{mt.Value}{X}");
    Console.WriteLine(shown);
    Console.WriteLine($"{D}----------------------------------------------------------------------------{X}");
    var found = detectors.Select(d => (d.Name, Cnt: d.Rx.Matches(userText).Count)).Where(x => x.Cnt > 0).ToList();
    Console.WriteLine($"{Y}{B}DETECTED IN THIS REQUEST:{X} " + (found.Count == 0 ? "nothing" : string.Join(",  ", found.Select(f => $"{R}{f.Cnt} {f.Name}{X}"))));
    Console.WriteLine($"{R}This text is now on the provider's servers, in gateway logs, and in anything between.{X}\n");
}

async Task Mock(HttpContext ctx, string body)
{
    var answer = "(mock cloud) I read your documents and the answer is in the cited files. This reply came from a stand-in model.";
    var streaming = body.Contains("\"stream\":true");
    if (!streaming)
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { id = "mock", @object = "chat.completion", created = 0, model = "mock", choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } } }));
        return;
    }
    ctx.Response.ContentType = "text/event-stream";
    foreach (var word in answer.Split(' '))
    {
        var chunk = JsonSerializer.Serialize(new { id = "mock", @object = "chat.completion.chunk", created = 0, model = "mock", choices = new[] { new { index = 0, delta = new { content = word + " " }, finish_reason = (string?)null } } });
        await ctx.Response.WriteAsync($"data: {chunk}\n\n"); await ctx.Response.Body.FlushAsync(); await Task.Delay(40);
    }
    await ctx.Response.WriteAsync("data: [DONE]\n\n");
}
