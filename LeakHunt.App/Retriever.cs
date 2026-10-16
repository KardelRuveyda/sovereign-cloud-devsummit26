using System.Text.RegularExpressions;

record Chunk(string Source, string Text);

// Tiny in-memory retriever (IDF-weighted keyword match). Production path: Microsoft.Extensions.VectorData + embeddings.
sealed class Retriever
{
    readonly List<Chunk> chunks = new();
    readonly Dictionary<string, int> docFreq = new();

    public int Count => chunks.Count;

    public Retriever(string dir)
    {
        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            var name = Path.GetFileName(file);
            var buffer = "";
            foreach (var block in File.ReadAllText(file).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (buffer.Length + block.Length > 700 && buffer.Length > 0) { Add(name, buffer); buffer = ""; }
                buffer += block.Trim() + "\n\n";
            }
            if (buffer.Length > 0) Add(name, buffer);
        }
    }

    void Add(string source, string text)
    {
        chunks.Add(new Chunk(source, text.Trim()));
        foreach (var t in Tokens(text).Distinct())
            docFreq[t] = docFreq.GetValueOrDefault(t) + 1;
    }

    public IReadOnlyList<Chunk> Search(string question, int k = 2)
    {
        var q = Tokens(question).Distinct().ToList();
        return chunks
            .Select(c =>
            {
                var terms = Tokens(c.Text).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                var score = q.Where(terms.ContainsKey).Sum(t => Math.Log(1.0 + chunks.Count / (double)docFreq[t]) * (1 + Math.Log(terms[t])));
                return (c, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(k)
            .Select(x => x.c)
            .ToList();
    }

    static readonly HashSet<string> Stop = new() { "the", "and", "for", "with", "what", "how", "who", "are", "was", "does", "did", "this", "that", "from", "has", "have", "its", "his", "her", "per", "can", "will" };

    static IEnumerable<string> Tokens(string s) =>
        Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(t => t.Length > 2 && !Stop.Contains(t));
}
