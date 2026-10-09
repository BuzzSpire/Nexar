using System.Diagnostics;
using Nexar.Examples;

// Runs the README examples against a local demo API. Pass example names to run only some of them.
var examples = new (string Name, Func<string, Task> Run)[]
{
    ("quick-start", BasicsExamples.QuickStart),
    ("requests", BasicsExamples.Requests),
    ("responses", BasicsExamples.Responses),
    ("errors", BasicsExamples.Errors),
    ("retries", BasicsExamples.Retries),
    ("build-and-sign", BasicsExamples.BuildAndSign),
    ("authentication", AdvancedExamples.Authentication),
    ("streaming", AdvancedExamples.Streaming),
    ("files", AdvancedExamples.Files),
    ("caching", AdvancedExamples.Caching),
    ("observability", AdvancedExamples.Observability),
    ("dependency-injection", AdvancedExamples.DependencyInjection),
    ("testing", AdvancedExamples.Testing),
};

await using var api = await DemoApi.StartAsync();
Console.WriteLine($"Demo API listening on {api.BaseUrl}");

var selected = args.Length == 0 ? examples : examples.Where(e => args.Contains(e.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
var failures = 0;
foreach (var (name, run) in selected)
{
    Console.WriteLine();
    Console.WriteLine($"== {name}");
    var watch = Stopwatch.StartNew();
    try
    {
        await run(api.BaseUrl);
        Console.WriteLine($"-- ok ({watch.ElapsedMilliseconds} ms)");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"-- FAILED: {ex}");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All {selected.Length} examples passed." : $"{failures} of {selected.Length} examples failed.");
return failures == 0 ? 0 : 1;
