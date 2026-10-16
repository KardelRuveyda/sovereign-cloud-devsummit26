using Microsoft.Extensions.AI;

var docs = new Retriever(Path.Combine(AppContext.BaseDirectory, "docs"));

IChatClient chat = Backend.Cloud();                      // data leaves the building
// IChatClient chat = await Backend.LocalAsync();        // data stays on this machine
// IChatClient chat = Backend.Edge();                    // Foundry Local on Azure Local (cluster)

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"Knowledge Assistant  |  {docs.Count} chunks indexed  |  {Backend.Info}");
Console.ResetColor();
Console.WriteLine("Ask about the internal documents. Empty line to quit.\n");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("? "); Console.ResetColor();
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    var hits = docs.Search(question, k: 2);
    var context = string.Join("\n---\n", hits.Select(h => $"[{h.Source}]\n{h.Text}"));

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are an internal knowledge assistant. Answer ONLY from the context. If the context does not contain the answer, say you do not know. Be concise. Cite the source file names in square brackets."),
        new(ChatRole.User, $"Context:\n{context}\n\nQuestion: {question}")
    };


    await foreach (var update in chat.GetStreamingResponseAsync(messages))
        Console.Write(update.Text);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n   sources: {string.Join(", ", hits.Select(h => h.Source).Distinct())}\n");
    Console.ResetColor();
}
