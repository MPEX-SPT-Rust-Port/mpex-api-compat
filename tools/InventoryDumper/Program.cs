using System.Reflection;
using System.Text;
using System.Text.Json;
using SPTarkov.DI.Annotations;

var opts = new Dictionary<string, string>();
for (var i = 0; i + 1 < args.Length; i += 2)
{
    opts[args[i]] = args[i + 1];
}

if (!opts.TryGetValue("--output", out var outputDir))
{
    Console.Error.WriteLine("Usage: InventoryDumper --output <dir> [--routes-source <sptCloneRoot>] [--readme <path>]");
    return 1;
}

string[] contractNames =
[
    "SPTarkov.Common", "SPTarkov.DI", "SPTarkov.Reflection",
    "SPTarkov.Server.Assets", "SPTarkov.Server.Core", "SPTarkov.Server.Web",
];
var assemblies = contractNames.Select(n => Assembly.Load(n)).ToList();

// SPT ships AssemblyVersion 4.1.0.0 for every 4.1.x patch release, so the loaded
// assemblies carry no 4.1.2 marker at all. The only runtime-visible proof of the
// baseline is the restored NuGet package version, which the build records for us.
var depsPath = Path.Combine(AppContext.BaseDirectory, "InventoryDumper.deps.json");
using var depsDoc = JsonDocument.Parse(File.ReadAllText(depsPath));
var packageVersions = depsDoc.RootElement.GetProperty("libraries")
    .EnumerateObject()
    .Select(p => p.Name.Split('/'))
    .Where(parts => parts.Length == 2)
    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

// Guard: this tool documents 4.1.2 and nothing else.
foreach (var a in assemblies)
{
    var name = a.GetName().Name!;
    var v = packageVersions.GetValueOrDefault(name, "<absent from deps.json>");
    if (v != "4.1.2")
    {
        Console.Error.WriteLine($"FATAL: {name} resolved to package version {v}, expected 4.1.2");
        return 1;
    }
}

Directory.CreateDirectory(outputDir);
var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

// --- Assembly identities ---
var assemblyEntries = assemblies
    .Select(a =>
    {
        var n = a.GetName();
        var pkt = n.GetPublicKeyToken();
        return new AssemblyEntry(
            n.Name!,
            n.Version!.ToString(),
            packageVersions[n.Name!],
            pkt is { Length: > 0 } ? Convert.ToHexStringLower(pkt) : "(none)",
            a.GetExportedTypes().Length
        );
    })
    .OrderBy(e => e.Name, StringComparer.Ordinal)
    .ToList();

File.WriteAllText(Path.Combine(outputDir, "assemblies.json"), JsonSerializer.Serialize(assemblyEntries, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "assemblies.md"),
    MdTable(
        "Baseline assemblies (SPT 4.1.2)",
        ["Assembly", "AssemblyVersion", "PackageVersion", "PublicKeyToken", "Public types"],
        assemblyEntries.Select(e => new[] { e.Name, e.Version, e.PackageVersion, e.PublicKeyToken, e.PublicTypes.ToString() })
    )
);

// --- [Injectable] registry ---
var injectables = assemblies
    .SelectMany(a =>
        a.GetTypes()
            .Where(t => Attribute.IsDefined(t, typeof(Injectable)))
            .Select(t =>
            {
                var attr = t.GetCustomAttribute<Injectable>()!;
                return new InjectableEntry(t.FullName!, a.GetName().Name!, attr.InjectionType.ToString(), attr.TypePriority);
            })
    )
    .OrderBy(e => e.Type, StringComparer.Ordinal)
    .ToList();

if (injectables.Count == 0)
{
    Console.Error.WriteLine("FATAL: no [Injectable] types found — reflection scan is broken.");
    return 1;
}

File.WriteAllText(Path.Combine(outputDir, "injectables.json"), JsonSerializer.Serialize(injectables, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "injectables.md"),
    MdTable(
        "[Injectable] DI registry — mods observe lifetimes and TypePriority load order",
        ["Type", "Assembly", "InjectionType", "TypePriority"],
        injectables.Select(e => new[] { e.Type, e.Assembly, e.InjectionType, e.TypePriority.ToString() })
    )
);

// --- Mod lifecycle interface implementors ---
var core = assemblies.Single(a => a.GetName().Name == "SPTarkov.Server.Core");
string[] lifecycleInterfaceNames =
[
    "SPTarkov.Server.Core.DI.IOnLoad",
    "SPTarkov.Server.Core.DI.IOnUpdate",
    "SPTarkov.Server.Core.DI.IOnDIConstruct",
];
var lifecycle = new List<LifecycleEntry>();
foreach (var name in lifecycleInterfaceNames)
{
    var iface = core.GetType(name) ?? throw new InvalidOperationException($"{name} not found in Server.Core");
    foreach (var a in assemblies)
    {
        foreach (var t in a.GetTypes().Where(t => t.IsClass && iface.IsAssignableFrom(t)))
        {
            lifecycle.Add(new LifecycleEntry(iface.FullName!, t.FullName!, a.GetName().Name!));
        }
    }
}

lifecycle = lifecycle
    .OrderBy(e => e.Interface, StringComparer.Ordinal)
    .ThenBy(e => e.Type, StringComparer.Ordinal)
    .ToList();

File.WriteAllText(Path.Combine(outputDir, "lifecycle.json"), JsonSerializer.Serialize(lifecycle, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "lifecycle.md"),
    MdTable(
        "Mod lifecycle interface implementors",
        ["Interface", "Type", "Assembly"],
        lifecycle.Select(e => new[] { e.Interface, e.Type, e.Assembly })
    )
);

Console.WriteLine($"OK: {assemblyEntries.Count} assemblies, {injectables.Count} injectables, {lifecycle.Count} lifecycle implementors");
return 0;

static string MdTable(string title, string[] header, IEnumerable<string[]> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# {title}");
    sb.AppendLine();
    sb.AppendLine("<!-- GENERATED by tools/InventoryDumper — do not edit by hand. -->");
    sb.AppendLine();
    sb.AppendLine("| " + string.Join(" | ", header) + " |");
    sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", header.Length)));
    foreach (var row in rows)
    {
        sb.AppendLine("| " + string.Join(" | ", row.Select(c => c.Replace("|", "\\|"))) + " |");
    }

    return sb.ToString();
}

record AssemblyEntry(string Name, string Version, string PackageVersion, string PublicKeyToken, int PublicTypes);

record InjectableEntry(string Type, string Assembly, string InjectionType, int TypePriority);

record LifecycleEntry(string Interface, string Type, string Assembly);
