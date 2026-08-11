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
        injectables.Select(e => new[] { e.Type, e.Assembly, e.InjectionType, e.TypePriority.ToString() }),
        [
            "Registration is mechanical, so this table implies the registered service types: each",
            "`[Injectable]` type is registered as itself, plus every interface it implements whose",
            "namespace does not start with `System`, plus every type in its base-type chain. Types are",
            "registered sorted ascending by `TypePriority`, which is the load order mods observe via",
            "`GetServices<T>()`. Under `Singleton` all of a type's registrations resolve to the same",
            "instance; under `Transient` each resolution constructs a new one.",
            "The executable pin for this rule is `tests/BehavioralTests/DependencyInjectionHandlerTests.cs`.",
        ]
    )
);

// --- Mod lifecycle interface implementors ---
// Discovered, not hardcoded: every public interface in SPTarkov.Server.Core.DI is a
// lifecycle contract a mod may implement, siblings included.
var core = assemblies.Single(a => a.GetName().Name == "SPTarkov.Server.Core");
var lifecycleInterfaces = core.GetExportedTypes()
    .Where(t => t.IsInterface && t.Namespace == "SPTarkov.Server.Core.DI")
    .OrderBy(t => t.FullName, StringComparer.Ordinal)
    .ToList();

foreach (var required in new[]
         {
             "SPTarkov.Server.Core.DI.IOnLoad",
             "SPTarkov.Server.Core.DI.IOnUpdate",
             "SPTarkov.Server.Core.DI.IOnDIConstruct",
         })
{
    if (!lifecycleInterfaces.Any(t => t.FullName == required))
    {
        Console.Error.WriteLine($"FATAL: {required} not discovered in SPTarkov.Server.Core.DI — reflection scan is broken.");
        return 1;
    }
}

var lifecycle = new List<LifecycleEntry>();
foreach (var iface in lifecycleInterfaces)
{
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
        lifecycle.Select(e => new[] { e.Interface, e.Type, e.Assembly }),
        [
            "Scanned interfaces — every public interface in `SPTarkov.Server.Core.DI`, discovered by",
            "reflection. Zero-implementor interfaces are listed here because they are still contracts a",
            "mod may implement; the table below carries implementor rows only.",
            "",
            .. lifecycleInterfaces.Select(i =>
                $"- `{i.FullName}` — {lifecycle.Count(e => e.Interface == i.FullName)} implementors"),
        ]
    )
);

// --- Route table (source scan of the frozen 4.1.2 tag) ---
// Route URLs are string literals passed to RouteAction constructors; scanning the
// tagged source is reliable and avoids standing up the full DI container.
// ponytail: regex scan assumes one router class per file and RouteAction-based
// routes only (ItemEvent routers use a different mechanism and are already listed
// in injectables.md); upgrade to a Roslyn walker if the convention breaks.
List<RouteEntry> routes = [];
if (opts.TryGetValue("--routes-source", out var sptRoot))
{
    var routersDir = Path.Combine(sptRoot, "Libraries", "SPTarkov.Server.Core", "Routers");
    if (!Directory.Exists(routersDir))
    {
        Console.Error.WriteLine($"FATAL: {routersDir} does not exist — wrong --routes-source?");
        return 1;
    }

    var classRx = new System.Text.RegularExpressions.Regex(@"class\s+(\w+)");
    var baseRx = new System.Text.RegularExpressions.Regex(@":\s*(StaticRouter|DynamicRouter|SaveLoadRouter)\b");
    var urlRx = new System.Text.RegularExpressions.Regex(@"new\s+RouteAction(?:<[^>]+>)?\s*\(\s*""([^""]+)""");

    foreach (var file in Directory.EnumerateFiles(routersDir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var text = File.ReadAllText(file);
        var cls = classRx.Match(text);
        var baseMatch = baseRx.Match(text);
        foreach (System.Text.RegularExpressions.Match m in urlRx.Matches(text))
        {
            routes.Add(new RouteEntry(
                m.Groups[1].Value,
                cls.Success ? cls.Groups[1].Value : Path.GetFileNameWithoutExtension(file),
                baseMatch.Success ? baseMatch.Groups[1].Value : "Unknown",
                Path.GetRelativePath(sptRoot, file)
            ));
        }
    }

    routes = routes
        .OrderBy(r => r.Url, StringComparer.Ordinal)
        .ThenBy(r => r.Router, StringComparer.Ordinal)
        .ToList();

    if (routes.Count == 0)
    {
        Console.Error.WriteLine("FATAL: no routes found — regex or source layout changed.");
        return 1;
    }

    File.WriteAllText(Path.Combine(outputDir, "routes.json"), JsonSerializer.Serialize(routes, jsonOpts));
    File.WriteAllText(
        Path.Combine(outputDir, "routes.md"),
        MdTable(
            "Route table (RouteAction-based routers, scanned from the 4.1.2 tag)",
            ["URL", "Router", "Kind", "Source file"],
            routes.Select(r => new[] { r.Url, r.Router, r.Kind, r.File })
        )
    );
}

// --- README index ---
if (opts.TryGetValue("--readme", out var readmePath))
{
    var sb = new StringBuilder();
    sb.AppendLine("# mpex-api-compat");
    sb.AppendLine();
    sb.AppendLine("<!-- GENERATED by tools/InventoryDumper — do not edit by hand. Regenerate with tools/generate-baseline.sh -->");
    sb.AppendLine();
    sb.AppendLine("Frozen SPT **4.1.2** modding API baseline for the MPEX (MultiPlayer eXtraction) C#-to-Rust port.");
    sb.AppendLine("Compiled C# mods must keep loading against Rust-backed shim assemblies; this repo holds the contract they load against.");
    sb.AppendLine();
    sb.AppendLine("- `baseline-dlls/` — frozen 4.1.2 assemblies (immutable; safe to copy into consuming repos)");
    sb.AppendLine("- `api-surface/` — generated C# listings of every public type/member");
    sb.AppendLine("- `inventory/` — DI registry, lifecycle implementors, route table, assembly identities");
    sb.AppendLine("- `ci/` — ApiCompat PR check for consuming repos (see `ci/ADOPTION.md`)");
    sb.AppendLine("- `tests/BehavioralTests/` — characterization tests runnable against baseline or shim assemblies");
    sb.AppendLine();
    sb.AppendLine("| Assembly | Version | Public types | [Injectable] types | API listing |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var e in assemblyEntries)
    {
        var injCount = injectables.Count(i => i.Assembly == e.Name);
        sb.AppendLine($"| {e.Name} | {e.PackageVersion} | {e.PublicTypes} | {injCount} | [api-surface/{e.Name}.cs](api-surface/{e.Name}.cs) |");
    }

    sb.AppendLine();
    sb.AppendLine($"Routes documented: {routes.Count} (see [inventory/routes.md](inventory/routes.md)).");
    sb.AppendLine();
    sb.AppendLine("## Regenerating");
    sb.AppendLine();
    sb.AppendLine("`tools/generate-baseline.sh` rebuilds `README.md`, `inventory/`, `api-surface/` and `baseline-dlls/`.");
    sb.AppendLine("It needs a source worktree of the 4.1.2 tag to scan the route table:");
    sb.AppendLine();
    sb.AppendLine("```sh");
    sb.AppendLine("git clone https://github.com/sp-tarkov/server-csharp ~/git/TEMP/server-csharp   # or reuse an existing clone");
    sb.AppendLine("git -C ~/git/TEMP/server-csharp worktree add ~/git/TEMP/spt-412-src 4.1.2");
    sb.AppendLine("SPT_SRC=~/git/TEMP/spt-412-src tools/generate-baseline.sh");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("`SPT_SRC` defaults to `$HOME/git/TEMP/spt-412-src`, so the override is only needed when the worktree lives elsewhere.");
    sb.AppendLine("Rerunning the script must produce a zero git diff.");
    sb.AppendLine();
    sb.AppendLine("## Running the behavioral suite");
    sb.AppendLine();
    sb.AppendLine("```sh");
    sb.AppendLine("dotnet test tests/BehavioralTests                                    # against the frozen baseline");
    sb.AppendLine("dotnet test tests/BehavioralTests -p:MpexAssemblyDir=/path/to/shims  # against a Rust-backed shim build");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("Warning: wipe `tests/BehavioralTests/{bin,obj}` when switching `MpexAssemblyDir` between directories.");
    sb.AppendLine("The csproj copies the assembly closure with `PreserveNewest`, and the frozen baseline DLLs have old mtimes, so stale DLLs from the previous run can survive the switch.");
    File.WriteAllText(readmePath, sb.ToString());
}

Console.WriteLine($"OK: {assemblyEntries.Count} assemblies, {injectables.Count} injectables, {lifecycle.Count} lifecycle implementors");
return 0;

static string MdTable(string title, string[] header, IEnumerable<string[]> rows, IEnumerable<string>? preamble = null)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# {title}");
    sb.AppendLine();
    sb.AppendLine("<!-- GENERATED by tools/InventoryDumper — do not edit by hand. -->");
    sb.AppendLine();
    foreach (var line in preamble ?? [])
    {
        sb.AppendLine(line);
    }

    if (preamble is not null)
    {
        sb.AppendLine();
    }

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

record RouteEntry(string Url, string Router, string Kind, string File);
