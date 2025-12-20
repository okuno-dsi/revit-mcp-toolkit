using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IfcCore;

namespace IfcCli;

internal static class Program
{
    private sealed class AnalysisReport
    {
        public List<string> SourceFiles { get; set; } = new();
        public Dictionary<string, EntityReport> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static AnalysisReport From(AnalysisResult analysis)
        {
            var report = new AnalysisReport
            {
                SourceFiles = new List<string>(analysis.SourceFiles)
            };

            foreach (var ek in analysis.Entities)
            {
                var entity = new EntityReport
                {
                    InstanceCount = ek.Value.InstanceCount
                };

                foreach (var pk in ek.Value.Properties)
                {
                    var key = pk.Key;
                    var stats = pk.Value;
                    entity.Properties.Add(new PropertyReport
                    {
                        Pset = key.Pset,
                        Name = key.Prop,
                        EntityCount = stats.EntityCount,
                        ValueCount = stats.ValueCount,
                        FillRate = stats.FillRate
                    });
                }

                entity.Properties.Sort((a, b) =>
                {
                    var psetCmp = StringComparer.OrdinalIgnoreCase.Compare(a.Pset, b.Pset);
                    return psetCmp != 0 ? psetCmp : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                });

                report.Entities[ek.Key] = entity;
            }

            return report;
        }
    }

    private sealed class EntityReport
    {
        public int InstanceCount { get; set; }
        public List<PropertyReport> Properties { get; set; } = new();
    }

    private sealed class PropertyReport
    {
        public string Pset { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int EntityCount { get; set; }
        public int ValueCount { get; set; }
        public double FillRate { get; set; }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args[1..];

        var logPath = $"IfcProfileAnalyzer_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        using var log = new FileLog(logPath);

        try
        {
            return cmd switch
            {
                "analyze-sample" => RunAnalyze(rest, log),
                "check-ifc" => RunCheck(rest, log),
                "stats" => RunStats(rest, log),
                "list-by-storey" => RunListByStorey(rest, log),
                "dump-spaces" => RunDumpSpaces(rest, log),
                "export-ids" => RunExportIds(rest, log),
                _ => Unknown(cmd)
            };
        }
        catch (Exception ex)
        {
            log.Error("Unexpected error", ex);
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 3;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintHelp();
        return 1;
    }

    private static int RunAnalyze(string[] args, ILog log)
    {
        var inputs = new List<string>();
        string? outPath = null;
        double minFill = 0.9;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        inputs.Add(args[++i]);
                    }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out requires a value");
                    outPath = args[++i];
                    break;
                case "--min-fill-rate":
                    if (i + 1 >= args.Length) throw new ArgumentException("--min-fill-rate requires a value");
                    if (!double.TryParse(args[++i], out minFill))
                        throw new ArgumentException("Invalid --min-fill-rate value.");
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("ERROR: analyze-sample requires at least one --input IFC file.");
            return 1;
        }

        outPath ??= "profile.json";
        log.Info($"analyze-sample: inputs={string.Join(", ", inputs)}; out={outPath}; minFill={minFill}");

        var loader = new IfcLoader();
        var analyzer = new Analyzer(loader);
        var gen = new ProfileGen();

        var analysis = analyzer.Analyze(inputs);
        var profile = gen.Generate(analysis, minFill);

        var opt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outPath, JsonSerializer.Serialize(profile, opt));

        log.Info($"Profile written to {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static int RunCheck(string[] args, ILog log)
    {
        string? ifc = null;
        string? profilePath = null;
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ifc":
                    if (i + 1 >= args.Length) throw new ArgumentException("--ifc requires a value");
                    ifc = args[++i];
                    break;
                case "--profile":
                    if (i + 1 >= args.Length) throw new ArgumentException("--profile requires a value");
                    profilePath = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out requires a value");
                    outPath = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ifc) || string.IsNullOrWhiteSpace(profilePath))
        {
            Console.Error.WriteLine("ERROR: check-ifc requires --ifc <file> and --profile <profile.json>.");
            return 1;
        }

        outPath ??= "check.json";
        log.Info($"check-ifc: ifc={ifc}; profile={profilePath}; out={outPath}");

        var loader = new IfcLoader();
        var checker = new ProfileCheck();

        var profileJson = File.ReadAllText(profilePath);
        var profile = JsonSerializer.Deserialize<ProfileDefinition>(profileJson) ??
                      throw new InvalidOperationException("Failed to deserialize profile JSON.");

        var model = loader.Load(ifc);
        var result = checker.Check(model, profile, ifc);

        var opt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outPath, JsonSerializer.Serialize(result, opt));

        log.Info($"Check result written to {Path.GetFullPath(outPath)}; ok={result.Ok}; errors={result.Summary.ErrorCount}");
        return 0; // always 0; callers inspect JSON for severity
    }

    private static int RunStats(string[] args, ILog log)
    {
        string? ifc = null;
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ifc":
                    if (i + 1 >= args.Length) throw new ArgumentException("--ifc requires a value");
                    ifc = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out requires a value");
                    outPath = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ifc))
        {
            Console.Error.WriteLine("ERROR: stats requires --ifc <file>.");
            return 1;
        }

        outPath ??= "stats.json";
        log.Info($"stats: ifc={ifc}; out={outPath}");

        var loader = new IfcLoader();
        var analyzer = new Analyzer(loader);
        var analysis = analyzer.Analyze(new[] { ifc });

        var report = AnalysisReport.From(analysis);
        var opt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, opt));

        log.Info($"Stats written to {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static int RunListByStorey(string[] args, ILog log)
    {
        string? ifc = null;
        string? storey = null;
        string? typeFilter = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ifc":
                    if (i + 1 >= args.Length) throw new ArgumentException("--ifc requires a value");
                    ifc = args[++i];
                    break;
                case "--storey":
                case "--level":
                    if (i + 1 >= args.Length) throw new ArgumentException("--storey requires a value");
                    storey = args[++i];
                    break;
                case "--type":
                    if (i + 1 >= args.Length) throw new ArgumentException("--type requires a value");
                    typeFilter = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ifc) || string.IsNullOrWhiteSpace(storey))
        {
            Console.Error.WriteLine("ERROR: list-by-storey requires --ifc <file> and --storey <name> (e.g. 3FL).");
            return 1;
        }

        var typeLabel = string.IsNullOrWhiteSpace(typeFilter) ? "(any)" : typeFilter;
        log.Info($"list-by-storey: ifc={ifc}; storey={storey}; type={typeLabel}");

        var loader = new IfcLoader();
        var model = loader.Load(ifc);

        var targetStorey = storey.Trim();
        var matches = new List<IfcEntity>();

        foreach (var ent in model.EntitiesById.Values)
        {
            if (!string.Equals(ent.StoreyName, targetStorey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(typeFilter) &&
                !string.Equals(ent.IfcType, typeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            matches.Add(ent);
        }

        matches.Sort((a, b) => a.Id.CompareTo(b.Id));

        Console.WriteLine($"Elements on storey '{targetStorey}'" +
                          (string.IsNullOrWhiteSpace(typeFilter) ? "" : $" of type {typeFilter}") +
                          $": count={matches.Count}");

        foreach (var ent in matches)
        {
            var namePart = string.IsNullOrWhiteSpace(ent.Name) ? "" : $"  Name={ent.Name}";
            Console.WriteLine($"  #{ent.Id}  type={ent.IfcType}{namePart}  GlobalId={ent.GlobalId}");
        }

        return 0;
    }

    private static int RunDumpSpaces(string[] args, ILog log)
    {
        string? ifc = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ifc":
                    if (i + 1 >= args.Length) throw new ArgumentException("--ifc requires a value");
                    ifc = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ifc))
        {
            Console.Error.WriteLine("ERROR: dump-spaces requires --ifc <file>.");
            return 1;
        }

        log.Info($"dump-spaces: ifc={ifc}");

        var loader = new IfcLoader();
        var model = loader.Load(ifc);

        int count = 0;
        foreach (var ent in model.EntitiesById.Values)
        {
            if (!ent.IfcType.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase)) continue;
            count++;
        }

        Console.WriteLine($"IFCSPACE count={count}");
        foreach (var ent in model.EntitiesById.Values)
        {
            if (!ent.IfcType.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"  #{ent.Id}  Name={ent.Name}  Storey={ent.StoreyName}");
        }

        return 0;
    }

    private static int RunExportIds(string[] args, ILog log)
    {
        string? profilePath = null;
        string? outPath = null;
        bool includeComments = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile":
                    if (i + 1 >= args.Length) throw new ArgumentException("--profile requires a value");
                    profilePath = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out requires a value");
                    outPath = args[++i];
                    break;
                case "--include-comments":
                    includeComments = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(profilePath) || string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("ERROR: export-ids requires --profile <profile.json> and --out <file.ids.xml>.");
            return 1;
        }

        log.Info($"export-ids: profile={profilePath}; out={outPath}; includeComments={includeComments}");

        ProfileDefinition profile;
        try
        {
            var json = File.ReadAllText(profilePath);
            profile = JsonSerializer.Deserialize<ProfileDefinition>(json) ??
                      throw new InvalidOperationException("Failed to deserialize profile JSON.");
        }
        catch (Exception ex)
        {
            log.Error("Failed to load or parse profile JSON.", ex);
            Console.Error.WriteLine($"ERROR: Failed to read profile '{profilePath}': {ex.Message}");
            return 1;
        }

        try
        {
            var exporter = new IdsExporter();
            exporter.Export(profile, outPath, includeComments);
        }
        catch (IOException ex)
        {
            log.Error("Failed to write IDS file.", ex);
            Console.Error.WriteLine($"ERROR: Failed to write IDS file '{outPath}': {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            log.Error("Unexpected error during IDS export.", ex);
            Console.Error.WriteLine($"ERROR: IDS export failed: {ex.Message}");
            return 3;
        }

        log.Info($"IDS file created: {Path.GetFullPath(outPath)}");
        Console.WriteLine($"IDS file created: {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("IfcProfileAnalyzer CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ifc-cli analyze-sample --input sample1.ifc [sample2.ifc ...] --out profile.json --min-fill-rate 0.9");
        Console.WriteLine("  ifc-cli check-ifc --ifc model.ifc --profile profile.json --out check.json");
        Console.WriteLine("  ifc-cli stats --ifc model.ifc --out stats.json");
        Console.WriteLine("  ifc-cli list-by-storey --ifc model.ifc --storey 3FL [--type IFCBEAM]");
        Console.WriteLine("  ifc-cli dump-spaces --ifc model.ifc");
        Console.WriteLine("  ifc-cli export-ids --profile profile.json --out requirements.ids.xml [--include-comments]");
        Console.WriteLine();
    }
}
