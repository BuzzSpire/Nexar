using System.Text.RegularExpressions;

namespace Nexar.Test;

/// <summary>
/// Every README code block marked <c>&lt;!-- snippet: name --&gt;</c> must match the code between
/// <c>// snippet:name</c> and <c>// end-snippet</c> in samples/Nexar.Examples, which actually runs.
/// </summary>
public partial class ReadmeSnippetTests
{
    private static readonly string Root = FindRoot();

    [GeneratedRegex(@"<!-- snippet: (?<name>[\w-]+) -->\s*```csharp\n(?<code>.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex ReadmeSnippet();

    [Fact]
    public void ReadmeSnippetsMatchTheRunnableExamples()
    {
        var examples = ExampleSnippets();
        var readme = File.ReadAllText(Path.Combine(Root, "README.md")).ReplaceLineEndings("\n");
        var documented = ReadmeSnippet().Matches(readme).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["code"].Value);

        Assert.NotEmpty(examples);
        Assert.Empty(documented.Keys.Except(examples.Keys));   // README blocks without an example
        Assert.Empty(examples.Keys.Except(documented.Keys));   // examples missing from the README
        foreach (var (name, code) in examples)
        {
            Assert.True(documented[name] == code, $"README snippet '{name}' differs from samples/Nexar.Examples:\n--- example ---\n{code}\n--- readme ---\n{documented[name]}");
        }
    }

    private static Dictionary<string, string> ExampleSnippets()
    {
        var snippets = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "samples", "Nexar.Examples"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var lines = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var start = lines[i].Trim();
                if (!start.StartsWith("// snippet:", StringComparison.Ordinal))
                {
                    continue;
                }
                var name = start["// snippet:".Length..].Trim();
                var body = lines.Skip(i + 1).TakeWhile(l => l.Trim() != "// end-snippet").ToList();
                var indent = body.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
                snippets.Add(name, string.Join("\n", body.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())));
            }
        }
        return snippets;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nexar.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
