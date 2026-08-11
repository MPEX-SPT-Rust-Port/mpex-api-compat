using System.Reflection;
using System.Text;
using System.Text.Json;

var opts = new Dictionary<string, string>();
for (var i = 0; i + 1 < args.Length; i += 2)
{
    opts[args[i]] = args[i + 1];
}

if (!opts.TryGetValue("--baseline", out var baselineDir) ||
    !opts.TryGetValue("--refs", out var refsDir) ||
    !opts.TryGetValue("--output", out var outputDir))
{
    Console.Error.WriteLine("Usage: ClientInventoryDumper --baseline <dir> --refs <dir> --output <dir>");
    return 1;
}

string[] contractNames =
[
    "spt-common", "spt-core", "spt-custom", "spt-debugging",
    "spt-prepatch", "spt-reflection", "spt-singleplayer",
];

// Freeze guard. Unlike the server side (AssemblyVersion 4.1.0.0 on every 4.1.x
// patch), the client DLLs are stamped per-patch: 4.1.2.0 IS the baseline marker.
foreach (var name in contractNames)
{
    var path = Path.Combine(baselineDir, name + ".dll");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"FATAL: {path} missing");
        return 1;
    }

    var v = AssemblyName.GetAssemblyName(path).Version?.ToString();
    if (v != "4.1.2.0")
    {
        Console.Error.WriteLine($"FATAL: {name} has AssemblyVersion {v}, expected 4.1.2.0");
        return 1;
    }
}

// The game-derived closure cannot live in git; hard-fail rather than let
// MetadataLoadContext produce partial (silently wrong) output.
var refDlls = Directory.Exists(refsDir)
    ? Directory.GetFiles(refsDir, "*.dll", SearchOption.AllDirectories)
    : [];
if (refDlls.Length == 0)
{
    Console.Error.WriteLine($"FATAL: {refsDir} has no DLLs — populate it per client/refs/README.md");
    return 1;
}

if (!refDlls.Any(p => Path.GetFileName(p).Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine($"FATAL: {refsDir} lacks mscorlib.dll — copy the game's whole Managed directory, not a subset");
    return 1;
}

// A refs dir sourced from a live install contains its own spt-*.dll; the frozen
// baseline copies must win, so contract names are excluded from the refs set.
var contractSet = contractNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
var resolverPaths = contractNames
    .Select(n => Path.Combine(baselineDir, n + ".dll"))
    .Concat(refDlls.Where(p => !contractSet.Contains(Path.GetFileNameWithoutExtension(p))));

using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths), "mscorlib");
var assemblies = contractNames
    .Select(n => mlc.LoadFromAssemblyPath(Path.Combine(baselineDir, n + ".dll")))
    .ToList();

Directory.CreateDirectory(outputDir);
var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

// --- Assembly identities + references ---
var assemblyEntries = assemblies
    .Select(a => new AssemblyEntry(
        a.GetName().Name!,
        a.GetName().Version!.ToString(),
        a.GetExportedTypes().Length,
        a.GetReferencedAssemblies().Select(r => r.Name!).OrderBy(n => n, StringComparer.Ordinal).ToArray()
    ))
    .OrderBy(e => e.Name, StringComparer.Ordinal)
    .ToList();

File.WriteAllText(Path.Combine(outputDir, "assemblies.json"), JsonSerializer.Serialize(assemblyEntries, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "assemblies.md"),
    MdTable(
        "Client baseline assemblies (SPT 4.1.2)",
        ["Assembly", "AssemblyVersion", "Public types", "References"],
        assemblyEntries.Select(e => new[] { e.Name, e.Version, e.PublicTypes.ToString(), string.Join(", ", e.References) })
    )
);

// --- BepInEx plugin metadata ---
// Mods declare load order against these GUIDs via [BepInDependency]; this is the
// client-side equivalent of the server's DI registry.
var plugins = new List<PluginEntry>();
foreach (var a in assemblies)
{
    foreach (var t in a.GetTypes())
    {
        var attrs = t.GetCustomAttributesData();
        var plugin = attrs.FirstOrDefault(x => x.AttributeType.FullName == "BepInEx.BepInPlugin");
        if (plugin is null)
        {
            continue;
        }

        var deps = attrs
            .Where(x => x.AttributeType.FullName == "BepInEx.BepInDependency")
            .Select(x => (string)x.ConstructorArguments[0].Value!)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();
        plugins.Add(new PluginEntry(
            a.GetName().Name!,
            t.FullName!,
            (string)plugin.ConstructorArguments[0].Value!,
            (string)plugin.ConstructorArguments[1].Value!,
            (string)plugin.ConstructorArguments[2].Value!,
            deps
        ));
    }
}

plugins = plugins.OrderBy(p => p.Guid, StringComparer.Ordinal).ToList();
if (plugins.Count == 0)
{
    Console.Error.WriteLine("FATAL: no [BepInPlugin] types found — metadata scan is broken.");
    return 1;
}

File.WriteAllText(Path.Combine(outputDir, "plugins.json"), JsonSerializer.Serialize(plugins, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "plugins.md"),
    MdTable(
        "BepInEx plugins — mods declare [BepInDependency] load order against these GUIDs",
        ["GUID", "Name", "Version", "Type", "Assembly", "Dependencies"],
        plugins.Select(p => new[] { p.Guid, p.Name, p.Version, p.Type, p.Assembly, string.Join(", ", p.Dependencies) })
    )
);

// --- ModulePatch subclasses ---
// SPT patches derive from SPT.Reflection.Patching.ModulePatch and select their
// target dynamically (GetTargetMethod override), so the inventory listable from
// metadata is the patch class list, not the targets.
var patches = new List<PatchEntry>();
foreach (var a in assemblies)
{
    foreach (var t in a.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
        {
            if (b.FullName == "SPT.Reflection.Patching.ModulePatch")
            {
                patches.Add(new PatchEntry(a.GetName().Name!, t.FullName!));
                break;
            }
        }
    }
}

patches = patches
    .OrderBy(p => p.Assembly, StringComparer.Ordinal)
    .ThenBy(p => p.Type, StringComparer.Ordinal)
    .ToList();
if (patches.Count == 0)
{
    Console.Error.WriteLine("FATAL: no ModulePatch subclasses found — metadata scan is broken.");
    return 1;
}

File.WriteAllText(Path.Combine(outputDir, "patches.json"), JsonSerializer.Serialize(patches, jsonOpts));
File.WriteAllText(
    Path.Combine(outputDir, "patches.md"),
    MdTable(
        "ModulePatch subclasses — SPT's own Harmony patches (targets are chosen at runtime)",
        ["Type", "Assembly"],
        patches.Select(p => new[] { p.Type, p.Assembly })
    )
);

Console.WriteLine($"OK: {assemblyEntries.Count} assemblies, {plugins.Count} plugins, {patches.Count} patch classes");
return 0;

static string MdTable(string title, string[] header, IEnumerable<string[]> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# {title}");
    sb.AppendLine();
    sb.AppendLine("<!-- GENERATED by tools/ClientInventoryDumper — do not edit by hand. -->");
    sb.AppendLine();
    sb.AppendLine("| " + string.Join(" | ", header) + " |");
    sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", header.Length)));
    foreach (var row in rows)
    {
        sb.AppendLine("| " + string.Join(" | ", row.Select(c => c.Replace("|", "\\|"))) + " |");
    }

    return sb.ToString();
}

record AssemblyEntry(string Name, string Version, int PublicTypes, string[] References);

record PluginEntry(string Assembly, string Type, string Guid, string Name, string Version, string[] Dependencies);

record PatchEntry(string Assembly, string Type);
