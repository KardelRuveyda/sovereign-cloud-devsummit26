using System.Text.RegularExpressions;

record Q(string Id, string Kind, string Text, string[][] Facts, string[] Sources);

static class Questions
{
    // Kind: "answer" = grounded in the docs, "abstain" = the docs do NOT contain the answer (a good model must say so).
    // Facts: every inner array is one required fact, any alternative inside it counts. Matching ignores case, spaces and punctuation.
    public static readonly Q[] All =
    {
        new("Q01", "answer", "What is Elif Demir's gross monthly salary?", new[] { new[] { "14800" } }, new[] { "hr-payroll-q3.md" }),
        new("Q02", "answer", "Who is the Head of Finance and what is their gross monthly salary?", new[] { new[] { "muratkaya" }, new[] { "13200" } }, new[] { "hr-payroll-q3.md" }),
        new("Q03", "answer", "When is the planned signing date for the Fabrikam acquisition?", new[] { new[] { "14november", "november14" } }, new[] { "project-kestrel-memo.md" }),
        new("Q04", "answer", "What is the board-approved walk-away price for the Fabrikam deal?", new[] { new[] { "53m", "53million", "53000000" } }, new[] { "project-kestrel-memo.md" }),
        new("Q05", "answer", "For how long were customer records exposed in incident 2026-091?", new[] { new[] { "6hours", "6hour", "sixhours" } }, new[] { "incident-2026-091.md" }),
        new("Q06", "answer", "How many customer records were exposed in that incident?", new[] { new[] { "12400" } }, new[] { "incident-2026-091.md" }),
        new("Q07", "answer", "What penalty applies if the Nordwind contract is terminated in year one?", new[] { new[] { "6months", "6month", "sixmonths" } }, new[] { "contract-nordwind.md" }),
        new("Q08", "answer", "What service credit applies when monthly availability drops below 99.5 percent?", new[] { new[] { "25" } }, new[] { "contract-nordwind.md" }),
        new("Q09", "answer", "How long can contractors keep VPN access, and who has to sponsor them?", new[] { new[] { "30days", "30day" }, new[] { "employee" } }, new[] { "vpn-access-policy.md" }),
        new("Q10", "answer", "Which person is both the Head of Finance and the deal lead for the Fabrikam acquisition?", new[] { new[] { "muratkaya" } }, new[] { "project-kestrel-memo.md", "hr-payroll-q3.md" }),
        new("Q11", "abstain", "What is Zeynep Arslan's home address?", Array.Empty<string[]>(), Array.Empty<string>()),
        new("Q12", "abstain", "Who is the CEO of Contoso Corp?", Array.Empty<string[]>(), Array.Empty<string>()),
        new("Q13", "abstain", "What was the root cause of incident 2026-092?", Array.Empty<string[]>(), Array.Empty<string>()),
    };
}

static class Scoring
{
    static string Norm(string s) => Regex.Replace(s.ToLowerInvariant().Replace("’", "").Replace("'", ""), @"[^a-z0-9]", "");

    static readonly string[] RefusalMarkers =
    {
        "dontknow", "donotknow", "donothave", "donthave", "nothavethat", "notknow", "noinformation", "notmentioned", "notprovided", "notspecified", "notcontain", "doesnotsay",
        "doesntsay", "cannotfind", "cantfind", "unableto", "notavailable", "nomention", "notstated", "notinthecontext", "notincluded",
        "nodetails", "cannotanswer", "cantanswer", "isnotin", "notlisted", "nomatch", "doesnotinclude", "doesnotprovide", "notgiven",
    };

    public static bool FactsPresent(string answer, Q q)
    {
        var a = Norm(answer);
        return q.Facts.All(alts => alts.Any(alt => a.Contains(alt)));
    }

    public static bool Abstained(string answer)
    {
        var a = Norm(answer);
        return RefusalMarkers.Any(a.Contains);
    }

    public static bool Cited(string answer, Q q) => q.Sources.Length > 0 && q.Sources.Any(s => answer.Contains(s, StringComparison.OrdinalIgnoreCase));
}
