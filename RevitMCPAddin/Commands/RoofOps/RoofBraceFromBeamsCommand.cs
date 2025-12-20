// File: Commands/RoofOps/RoofBraceFromBeamsCommand.cs
// Purpose: Place roof braces from beams using a bay grid + WPF UI.
// Target : Revit 2024 / .NET Framework 4.8
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.UI.RoofBrace;

namespace RevitMCPAddin.Commands.RoofOps
{
    /// <summary>
    /// MCP entry point: place_roof_brace_from_prompt
    /// </summary>
    public class PlaceRoofBraceFromPromptCommand : IRevitCommandHandler
    {
        public string CommandName => "place_roof_brace_from_prompt";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = cmd.Params;
            var options = BracePromptOptions.FromParams(p);

            if (string.IsNullOrWhiteSpace(options.LevelName))
            {
                return new { ok = false, msg = "params.levelName が必要です。" };
            }

            var level = FindLevelByName(doc, options.LevelName);
            if (level == null)
            {
                return new { ok = false, msg = $"Level が見つかりません: {options.LevelName}" };
            }

            var filter = new BraceBayFilter
            {
                Level = level,
                UseGBeams = options.UseGBeams,
                UseBBeams = options.UseBBeams,
                IgnoreChars = options.IgnoreChars.ToList(),
                MarkParamSpec = options.MarkParamSpec ?? new JObject(),
                MarkContains = options.MarkContains?.ToList() ?? new List<string>()
            };

            var bayBuilder = new BeamBayBuilder(doc);
            List<double> verticalOffsets;
            List<double> horizontalOffsets;
            var bayModels = bayBuilder.BuildBaysFromBeams(filter, out verticalOffsets, out horizontalOffsets);

            if (bayModels.Count == 0)
            {
                return new { ok = false, msg = "対象となるベイが検出できませんでした。" };
            }

            int rowCount = bayModels.Max(b => b.Row) + 1;
            int colCount = bayModels.Max(b => b.Column) + 1;

            // Grid names for headers:
            // - If caller provides xGrids/yGrids, use them as-is (order = left->right / bottom->top).
            // - Otherwise, try to resolve actual Revit grid names by matching grid line positions.
            IList<string> xNames;
            IList<string> yNames;
            if (options.XGridNames != null && options.XGridNames.Count > 0)
            {
                xNames = BuildGridNames(options.XGridNames, colCount + 1, "X");
            }
            else
            {
                xNames = GridNameResolver.ResolveXGridNames(doc, verticalOffsets, colCount + 1, "X");
            }

            if (options.YGridNames != null && options.YGridNames.Count > 0)
            {
                yNames = BuildGridNames(options.YGridNames, rowCount + 1, "Y");
            }
            else
            {
                yNames = GridNameResolver.ResolveYGridNames(doc, horizontalOffsets, rowCount + 1, "Y");
            }

            // Detect existing braces in each bay (for red-line visualization and deletion).
            var existingInfo = ExistingBraceDetector.Analyze(doc, level, bayModels);
            var existingPatterns = existingInfo.ToDictionary(kv => kv.Key, kv => kv.Value.Pattern);

            // Build WPF view model
            var vm = new BraceGridViewModel(rowCount, colCount, xNames, yNames, existingPatterns)
            {
                PromptText = options.RawPromptText,
                PromptSummary = BracePromptOptions.BuildSummary(options, level)
            };

            // Axis labels: show actual Revit grid names at their positions, independent from bay boundaries.
            try
            {
                var xLabels = GridNameResolver.BuildXAxisLabels(doc, verticalOffsets, options.XGridNames);
                var yLabels = GridNameResolver.BuildYAxisLabels(doc, horizontalOffsets, options.YGridNames);
                vm.SetAxisGridLabels(xLabels, yLabels);
            }
            catch
            {
                // best-effort: keep legacy per-boundary labels when anything fails
            }

            // Map braceTypes (from MCP) to UI items
            var braceTypeItems = options.BraceTypes.Select(bt => new BraceTypeItem
            {
                Code = bt.Code,
                Symbol = bt.Symbol,
                TypeName = bt.TypeName,
                FamilyName = bt.FamilyName
            });
            vm.SetBraceTypes(braceTypeItems, options.DefaultBraceTypeCode);

            // Show WPF window (modal, owned by Revit main window)
            var window = new BraceGridWindow(vm);
            var helper = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = uiapp.MainWindowHandle
            };

            // Modeless window + nested dispatcher loop (pseudo-modal):
            // - Allows Revit UI interaction (zoom/pan) while the grid UI is open.
            // - Still waits for user OK/Cancel so MCP job returns a final result.
            var frame = new DispatcherFrame();
            window.Closed += (_, __) => frame.Continue = false;
            window.Show();
            Dispatcher.PushFrame(frame);

            if (vm.DialogResult != true)
            {
                return new { ok = false, cancelled = true, msg = "ユーザーがキャンセルしました。" };
            }

            // Build plan from the final ViewModel state
            var plan = BracePlanBuilder.BuildPlan(doc, level, options, rowCount, colCount, vm);

            // Save plan JSON under %LOCALAPPDATA%\RevitMCP\logs
            string planPath = BracePlanLogger.SavePlan(doc, cmd, plan);

            if (options.DryRun)
            {
                var drySummary = BracePlanExecutor.Summarize(plan);
                return new
                {
                    ok = true,
                    dryRun = true,
                    summary = drySummary,
                    planLogPath = planPath
                };
            }

            var execSummary = BracePlanExecutor.Execute(doc, level, plan, bayModels, existingInfo, options);

            return new
            {
                ok = true,
                dryRun = false,
                summary = execSummary,
                planLogPath = planPath,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }

        private static Level FindLevelByName(Document doc, string levelName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
        }

        private static IList<string> BuildGridNames(IList<string> source, int count, string prefix)
        {
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string name = null;
                if (source != null && i < source.Count)
                {
                    name = source[i];
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"{prefix}{i + 1}";
                }
                result.Add(name);
            }
            return result;
        }
    }

    #region Options / Models

    public class BraceTypeDefinition
    {
        /// <summary>Internal code (from MCP params). Example: "RB01".</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Structural symbol / mark (符号). Example: "RB-1".</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Revit family type name. Example: "H-BRACE_75x5".</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Optional Revit family name filter.</summary>
        public string FamilyName { get; set; } = string.Empty;

        [JsonIgnore]
        public string DisplayLabel
        {
            get
            {
                var hasSymbol = !string.IsNullOrEmpty(Symbol);
                var hasType = !string.IsNullOrEmpty(TypeName);
                if (hasSymbol || hasType)
                {
                    var sym = Symbol ?? string.Empty;
                    var type = TypeName ?? string.Empty;
                    if (string.IsNullOrEmpty(sym)) return type;
                    if (string.IsNullOrEmpty(type)) return sym;
                    return $"{sym}:{type}";
                }
                return Code;
            }
        }
    }

    public class BracePromptOptions
    {
        public string RawPromptText { get; set; } = string.Empty;

        public string LevelName { get; set; } = string.Empty;

        public bool UseGBeams { get; set; } = true;
        public bool UseBBeams { get; set; } = true;

        public IList<char> IgnoreChars { get; set; } = new List<char>();

        public IList<string> XGridNames { get; set; } = new List<string>();
        public IList<string> YGridNames { get; set; } = new List<string>();

        /// <summary>
        /// Specification of the type parameter used to classify beams (G/B など).
        /// Follows ParamResolver convention: paramName / builtInName / builtInId / guid etc.
        /// Example:
        ///   "markParam": { "paramName": "符号" }
        ///   "markParam": { "guid": "..." }
        /// </summary>
        public JObject MarkParamSpec { get; set; } = new JObject();

        /// <summary>
        /// Optional: include only beams whose mark source contains any of these tokens (case-insensitive).
        /// When specified, this filter is used instead of the legacy G/B classification.
        /// Example: "markContains": ["SG"]
        /// </summary>
        public IList<string> MarkContains { get; set; } = new List<string>();

        public IList<BraceTypeDefinition> BraceTypes { get; set; } = new List<BraceTypeDefinition>();
        public string DefaultBraceTypeCode { get; set; } = string.Empty;

        public string AutoPattern { get; set; } = "none";

        /// <summary>Z-offset (mm) from level elevation.</summary>
        public double ZOffsetMm { get; set; } = 0.0;

        public string Note { get; set; } = string.Empty;

        public bool DryRun { get; set; } = false;

        public static BracePromptOptions FromParams(JObject p)
        {
            var opt = new BracePromptOptions
            {
                RawPromptText = p.Value<string>("promptText") ?? string.Empty,
                LevelName = p.Value<string>("levelName") ?? string.Empty,
                UseGBeams = p.Value<bool?>("useG") ?? true,
                UseBBeams = p.Value<bool?>("useB") ?? true,
                AutoPattern = p.Value<string>("autoPattern") ?? "none",
                ZOffsetMm = p.Value<double?>("zOffsetMm") ?? 0.0,
                Note = p.Value<string>("note") ?? string.Empty,
                DryRun = p.Value<bool?>("dryRun") ?? false
            };

            // ignore chars (string like "Z,#" or array)
            var ignoreToken = p["ignore"];
            if (ignoreToken != null)
            {
                if (ignoreToken.Type == JTokenType.String)
                {
                    var s = ignoreToken.Value<string>() ?? string.Empty;
                    opt.IgnoreChars = s.Replace(",", string.Empty)
                        .Where(c => !char.IsWhiteSpace(c))
                        .Distinct()
                        .ToList();
                }
                else if (ignoreToken is JArray arr)
                {
                    opt.IgnoreChars = arr
                        .Values<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .SelectMany(x => x)
                        .Distinct()
                        .ToList();
                }
            }

            // grid names: arrays of strings
            if (p["xGrids"] is JArray xArr)
            {
                opt.XGridNames = xArr.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            if (p["yGrids"] is JArray yArr)
            {
                opt.YGridNames = yArr.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }

            // braceTypes: [{code,symbol,typeName,familyName}]
            if (p["braceTypes"] is JArray btArr)
            {
                foreach (var jt in btArr.OfType<JObject>())
                {
                    var bt = new BraceTypeDefinition
                    {
                        Code = jt.Value<string>("code") ?? string.Empty,
                        Symbol = jt.Value<string>("symbol") ?? string.Empty,
                        TypeName = jt.Value<string>("typeName") ?? string.Empty,
                        FamilyName = jt.Value<string>("familyName") ?? string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(bt.Code) || !string.IsNullOrWhiteSpace(bt.TypeName))
                    {
                        opt.BraceTypes.Add(bt);
                    }
                }
            }

            opt.DefaultBraceTypeCode = p.Value<string>("defaultBraceTypeCode")
                ?? opt.BraceTypes.FirstOrDefault()?.Code
                ?? string.Empty;

            // markParam specification (optional)
            var markParam = p["markParam"] as JObject;
            if (markParam != null)
            {
                // clone so caller's JObject is not mutated
                opt.MarkParamSpec = (JObject)markParam.DeepClone();

                // If caller passed paramId (from get_parameter_identity) and it's negative,
                // treat it as builtInId for compatibility with ParamResolver.
                if (opt.MarkParamSpec["paramId"] != null && opt.MarkParamSpec["builtInId"] == null)
                {
                    try
                    {
                        int pid = opt.MarkParamSpec.Value<int>("paramId");
                        if (pid < 0)
                        {
                            opt.MarkParamSpec["builtInId"] = pid;
                        }
                    }
                    catch
                    {
                        // ignore parse failure, fall back to other keys
                    }
                }
            }

            // markContains (optional): string or array of strings
            var mcTok = p["markContains"];
            if (mcTok != null)
            {
                if (mcTok.Type == JTokenType.String)
                {
                    var s = (mcTok.Value<string>() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        opt.MarkContains = new List<string> { s };
                    }
                }
                else if (mcTok is JArray mcArr)
                {
                    opt.MarkContains = mcArr
                        .Values<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            return opt;
        }

        public static string BuildSummary(BracePromptOptions opt, Level level)
        {
            var parts = new List<string>();
            parts.Add($"レベル: {level?.Name ?? opt.LevelName}");

            if (opt.MarkParamSpec != null && opt.MarkParamSpec.HasValues)
            {
                var name = opt.MarkParamSpec.Value<string>("paramName") ?? opt.MarkParamSpec.Value<string>("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    parts.Add($"markParam: {name}");
                }
                else if (opt.MarkParamSpec["paramId"] != null)
                {
                    parts.Add($"markParamId: {opt.MarkParamSpec.Value<int>("paramId")}");
                }
                else if (opt.MarkParamSpec["builtInId"] != null)
                {
                    parts.Add($"markBuiltInId: {opt.MarkParamSpec.Value<int>("builtInId")}");
                }
                else if (opt.MarkParamSpec["builtInName"] != null)
                {
                    parts.Add($"markBuiltInName: {opt.MarkParamSpec.Value<string>("builtInName")}");
                }
                else if (opt.MarkParamSpec["guid"] != null)
                {
                    parts.Add($"markGuid: {opt.MarkParamSpec.Value<string>("guid")}");
                }
            }

            if (opt.MarkContains != null && opt.MarkContains.Count > 0)
            {
                parts.Add($"符号 contains: {string.Join(",", opt.MarkContains)}");
            }
            else
            {
                parts.Add($"G梁: {(opt.UseGBeams ? "使用" : "不使用")}");
                parts.Add($"B梁: {(opt.UseBBeams ? "使用" : "不使用")}");
            }
            if (opt.IgnoreChars != null && opt.IgnoreChars.Count > 0)
            {
                parts.Add($"除外記号: {new string(opt.IgnoreChars.ToArray())}");
            }
            if (opt.BraceTypes != null && opt.BraceTypes.Count > 0)
            {
                var label = opt.BraceTypes.FirstOrDefault(bt =>
                                string.Equals(bt.Code, opt.DefaultBraceTypeCode, StringComparison.OrdinalIgnoreCase))
                            ?? opt.BraceTypes.First();
                parts.Add($"既定ブレースタイプ: {label.DisplayLabel}");
            }
            parts.Add($"Zオフセット: {opt.ZOffsetMm:0.###} mm");

            if (!string.IsNullOrWhiteSpace(opt.Note))
            {
                parts.Add($"ノート: {opt.Note}");
            }

            return string.Join(" / ", parts);
        }
    }

    public class BraceBayFilter
    {
        public Level Level { get; set; }
        public bool UseGBeams { get; set; } = true;
        public bool UseBBeams { get; set; } = true;
        public IList<char> IgnoreChars { get; set; } = new List<char>();
        public JObject MarkParamSpec { get; set; } = new JObject();
        public IList<string> MarkContains { get; set; } = new List<string>();
    }

    public class BayModel
    {
        public int Row { get; set; }
        public int Column { get; set; }

        public XYZ P00 { get; set; } // left-bottom
        public XYZ P10 { get; set; } // right-bottom
        public XYZ P01 { get; set; } // left-top
        public XYZ P11 { get; set; } // right-top
    }

    public class BracePlan
    {
        public string ProjectName { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;

        public BracePromptOptions PromptOptions { get; set; } = new BracePromptOptions();

        public int RowCount { get; set; }
        public int ColumnCount { get; set; }

        public IList<BayPlan> Bays { get; set; } = new List<BayPlan>();
    }

    public class BayPlan
    {
        public int Row { get; set; }
        public int Column { get; set; }

        /// <summary>None/Slash/BackSlash/X</summary>
        public string Pattern { get; set; } = "None";

        /// <summary>Brace type code (matches BraceTypeDefinition.Code).</summary>
        public string BraceTypeCode { get; set; } = string.Empty;

        /// <summary>
        /// If true, existing braces mapped to this bay will be deleted before creating new ones.
        /// If false and Pattern == "None", existing braces are left untouched.
        /// </summary>
        public bool OverrideExisting { get; set; } = false;
    }

    #endregion

    #region BeamBayBuilder

    public class BeamBayBuilder
    {
        private readonly Document _doc;

        public BeamBayBuilder(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<BayModel> BuildBaysFromBeams(
            BraceBayFilter filter,
            out List<double> verticalOffsets,
            out List<double> horizontalOffsets)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (filter.Level == null) throw new ArgumentException("Level が未指定です。", nameof(filter));

            const double tol = 0.01; // 約 3mm

            var beams = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => fi.StructuralType == StructuralType.Beam)
                .Where(fi => fi.LevelId == filter.Level.Id)
                .ToList();

            // Filter by mark (configurable type parameter via markParam) or, if not available, by type name.
            var sourceBeams = beams;

            // LevelId で 1 本も梁が見つからない場合は、
            // 梁の中点 Z がレベル高さ付近にあるものをフォールバックで採用する。
            if (sourceBeams.Count == 0)
            {
                var allBeams = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.StructuralType == StructuralType.Beam)
                    .ToList();

                double levelZ = filter.Level.Elevation;
                double zTol = UnitHelper.MmToInternalLength(1000.0); // ±1000mm 以内を「同じレベル」とみなす

                // 既定の検索幅は ±1000mm だが、隣接レベル間隔が大きいモデルでは足りないため、
                // 最寄り上下レベルの 1/2 間隔まで自動で拡張する。
                try
                {
                    var levels = new FilteredElementCollector(_doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();
                    int idx = levels.FindIndex(l => l.Id == filter.Level.Id);
                    if (idx >= 0)
                    {
                        double? lower = (idx > 0) ? (double?)levels[idx - 1].Elevation : null;
                        double? upper = (idx < levels.Count - 1) ? (double?)levels[idx + 1].Elevation : null;
                        double? halfSpan = null;
                        if (lower.HasValue && upper.HasValue)
                        {
                            halfSpan = 0.5 * Math.Min(levelZ - lower.Value, upper.Value - levelZ);
                        }
                        else if (lower.HasValue)
                        {
                            halfSpan = 0.5 * (levelZ - lower.Value);
                        }
                        else if (upper.HasValue)
                        {
                            halfSpan = 0.5 * (upper.Value - levelZ);
                        }
                        if (halfSpan.HasValue && halfSpan.Value > zTol)
                        {
                            zTol = halfSpan.Value;
                        }
                    }
                }
                catch
                {
                    // ignore and keep default tolerance
                }
                sourceBeams = allBeams
                    .Where(fi =>
                    {
                        if (!(fi.Location is LocationCurve lc)) return false;
                        var curve = lc.Curve;
                        var p0 = curve.GetEndPoint(0);
                        var p1 = curve.GetEndPoint(1);
                        double zMid = 0.5 * (p0.Z + p1.Z);
                        return Math.Abs(zMid - levelZ) <= zTol;
                    })
                    .ToList();
            }

            var filtered = new List<FamilyInstance>();

            foreach (var fi in sourceBeams)
            {
                var type = _doc.GetElement(fi.GetTypeId()) as FamilySymbol;
                if (type == null) continue;

                string source = null;

                // 1) try markParamSpec (paramName / builtInName / builtInId / guid ...)
                if (filter.MarkParamSpec != null && filter.MarkParamSpec.HasValues)
                {
                    try
                    {
                        string resolvedBy;
                        var prm = ParamResolver.ResolveByPayload(type, filter.MarkParamSpec, out resolvedBy);
                        if (prm != null && prm.StorageType == StorageType.String)
                        {
                            try { source = prm.AsString(); } catch { source = null; }
                        }
                    }
                    catch
                    {
                        source = null;
                    }
                }

                // 2) fallback: legacy behavior – use type parameter "ХДНЖ" if present
                if (string.IsNullOrWhiteSpace(source))
                {
                    try
                    {
                        var prm = type.LookupParameter("ХДНЖ");
                        if (prm != null && prm.StorageType == StorageType.String)
                        {
                            source = prm.AsString();
                        }
                    }
                    catch
                    {
                        source = null;
                    }
                }

                // 3) final fallback: use type name (ユーザー要望: タイプ名に G が入るものだけ採用)
                if (string.IsNullOrWhiteSpace(source))
                {
                    source = type.Name ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(source))
                    continue;

                if (filter.IgnoreChars != null && filter.IgnoreChars.Count > 0 &&
                    filter.IgnoreChars.Any(ch => source.Contains(ch)))
                {
                    continue;
                }

                bool include;

                // If markContains is specified, use it as the inclusion filter (instead of legacy G/B classification).
                if (filter.MarkContains != null && filter.MarkContains.Count > 0)
                {
                    include = filter.MarkContains.Any(tok =>
                        !string.IsNullOrWhiteSpace(tok) &&
                        source.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else
                {
                    bool hasG = source.IndexOf("G", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasB = source.IndexOf("B", StringComparison.OrdinalIgnoreCase) >= 0;
                    include = (hasG && filter.UseGBeams) || (hasB && filter.UseBBeams);
                }
                if (include)
                {
                    filtered.Add(fi);
                }
            }

            verticalOffsets = new List<double>();
            horizontalOffsets = new List<double>();

            foreach (var fi in filtered)
            {
                if (!(fi.Location is LocationCurve lc)) continue;
                var curve = lc.Curve;
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                var v = p1 - p0;
                v = new XYZ(v.X, v.Y, 0);
                if (v.GetLength() < 1e-6) continue;

                var d = v.Normalize();
                double ax = Math.Abs(d.X);
                double ay = Math.Abs(d.Y);
                var mid = (p0 + p1) * 0.5;

                // Only use near-axis members for bay boundary detection.
                // This prevents diagonal braces from polluting the bay grid when they are modeled as beams.
                const double axisCos = 0.90;
                if (ax >= axisCos)
                {
                    // horizontal (X-direction) -> group by Y
                    AddWithTolerance(horizontalOffsets, mid.Y, tol);
                }
                else if (ay >= axisCos)
                {
                    // vertical (Y-direction) -> group by X
                    AddWithTolerance(verticalOffsets, mid.X, tol);
                }
            }

            verticalOffsets.Sort();
            horizontalOffsets.Sort();

            var bays = new List<BayModel>();
            if (verticalOffsets.Count < 2 || horizontalOffsets.Count < 2)
            {
                return bays;
            }

            double z = filter.Level.Elevation;

            // Row index 0 at top (largest Y) to match Revit's +Y up and UniformGrid's top-to-bottom layout.
            int rowCount = horizontalOffsets.Count - 1;
            for (int row = 0; row < rowCount; row++)
            {
                int idx = (rowCount - 1) - row; // descend by Y interval
                double bottomY = horizontalOffsets[idx];
                double topY = horizontalOffsets[idx + 1];

                for (int col = 0; col < verticalOffsets.Count - 1; col++)
                {
                    double leftX = verticalOffsets[col];
                    double rightX = verticalOffsets[col + 1];

                    bays.Add(new BayModel
                    {
                        Row = row,
                        Column = col,
                        P00 = new XYZ(leftX, bottomY, z),
                        P10 = new XYZ(rightX, bottomY, z),
                        P01 = new XYZ(leftX, topY, z),
                        P11 = new XYZ(rightX, topY, z)
                    });
                }
            }

            return bays;
        }

        private static void AddWithTolerance(List<double> list, double value, double tol)
        {
            foreach (var v in list)
            {
                if (Math.Abs(v - value) <= tol) return;
            }
            list.Add(value);
        }
    }

    #endregion

    #region Grid name resolver

    /// <summary>
    /// Resolve header grid names from actual Revit Grid elements by matching X/Y offsets.
    /// Falls back to sequential names when no match is found.
    /// </summary>
    public static class GridNameResolver
    {
        public static IList<AxisGridLabel> BuildXAxisLabels(Document doc, IList<double> bayXOffsets, IList<string> explicitNames = null)
        {
            return BuildAxisLabels(doc, bayXOffsets, explicitNames, wantVertical: true, prefix: "X");
        }

        public static IList<AxisGridLabel> BuildYAxisLabels(Document doc, IList<double> bayYOffsets, IList<string> explicitNames = null)
        {
            return BuildAxisLabels(doc, bayYOffsets, explicitNames, wantVertical: false, prefix: "Y");
        }

        public static IList<string> ResolveXGridNames(Document doc, IList<double> xOffsets, int count, string prefix)
        {
            return ResolveNames(doc, xOffsets, count, prefix, wantVertical: true);
        }

        public static IList<string> ResolveYGridNames(Document doc, IList<double> yOffsets, int count, string prefix)
        {
            return ResolveNames(doc, yOffsets, count, prefix, wantVertical: false);
        }

        private static IList<string> ResolveNames(Document doc, IList<double> offsets, int count, string prefix, bool wantVertical)
        {
            var result = new List<string>(count);
            if (doc == null || offsets == null || offsets.Count == 0)
            {
                for (int i = 0; i < count; i++) result.Add($"{prefix}{i + 1}");
                return result;
            }

            var candidates = CollectGridCandidates(doc, wantVertical);
            var remaining = new List<(double coord, string name)>(candidates);

            var sortedOffsets = offsets.OrderBy(v => v).ToList();

            for (int i = 0; i < count; i++)
            {
                string name = null;

                if (i < sortedOffsets.Count && remaining.Count > 0)
                {
                    double target = sortedOffsets[i];
                    int bestIdx = -1;
                    double bestDist = double.MaxValue;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        double dist = Math.Abs(remaining[j].coord - target);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestIdx = j;
                        }
                    }

                    // Grids and beams do not always coincide (offset modeling, eccentricities, etc).
                    // Use the nearest Revit grid name even when distance is not small.
                    if (bestIdx >= 0)
                    {
                        name = remaining[bestIdx].name;
                        remaining.RemoveAt(bestIdx);
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"{prefix}{i + 1}";
                }

                result.Add(name);
            }

            return result;
        }

        private static List<(double coord, string name)> CollectGridCandidates(Document doc, bool wantVertical)
        {
            var list = new List<(double coord, string name)>();
            if (doc == null) return list;

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            foreach (var g in grids)
            {
                try
                {
                    var c = g.Curve;
                    if (c == null) continue;

                    XYZ s = c.GetEndPoint(0);
                    XYZ e = c.GetEndPoint(1);
                    var d = e - s;
                    d = new XYZ(d.X, d.Y, 0);
                    if (d.GetLength() < 1e-6) continue;
                    d = d.Normalize();

                    double ax = Math.Abs(d.X);
                    double ay = Math.Abs(d.Y);
                    bool isVertical = ax < ay; // Y direction dominant

                    if (wantVertical != isVertical) continue;

                    double coord = isVertical ? 0.5 * (s.X + e.X) : 0.5 * (s.Y + e.Y);
                    list.Add((coord, g.Name ?? string.Empty));
                }
                catch
                {
                    // ignore malformed grids
                }
            }

            list.Sort((a, b) => a.coord.CompareTo(b.coord));
            return list;
        }

        private static IList<AxisGridLabel> BuildAxisLabels(
            Document doc,
            IList<double> bayOffsets,
            IList<string> explicitNames,
            bool wantVertical,
            string prefix)
        {
            var labels = new List<AxisGridLabel>();

            if (doc == null || bayOffsets == null || bayOffsets.Count < 2)
            {
                return labels;
            }

            double min = bayOffsets.Min();
            double max = bayOffsets.Max();
            double span = max - min;
            if (Math.Abs(span) < 1e-9)
            {
                // Degenerate span; fall back to evenly spaced labels only when explicit names exist.
                if (explicitNames != null && explicitNames.Count > 0)
                {
                    int n = explicitNames.Count;
                    for (int i = 0; i < n; i++)
                    {
                        double pos = (n <= 1) ? 0.5 : (double)i / (n - 1);
                        labels.Add(new AxisGridLabel { Name = explicitNames[i], Position01 = pos });
                    }
                }
                return labels;
            }

            // Collect Revit grids and unify by name (segment grids share the same name).
            var candidates = CollectGridCandidates(doc, wantVertical);
            var byName = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.name))
                .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (coord: g.Average(x => x.coord), name: g.First().name))
                .OrderBy(x => x.coord)
                .ToList();

            if (explicitNames != null && explicitNames.Count > 0)
            {
                int n = explicitNames.Count;
                for (int i = 0; i < n; i++)
                {
                    string desired = explicitNames[i] ?? string.Empty;
                    double posDefault = (n <= 1) ? 0.5 : (double)i / (n - 1);

                    var match = byName.FirstOrDefault(x => string.Equals(x.name, desired, StringComparison.OrdinalIgnoreCase));
                    double pos = posDefault;
                    if (!string.IsNullOrWhiteSpace(match.name))
                    {
                        pos = (match.coord - min) / span;
                    }

                    labels.Add(new AxisGridLabel { Name = string.IsNullOrWhiteSpace(desired) ? $"{prefix}{i + 1}" : desired, Position01 = pos });
                }
                return labels;
            }

            // Auto: include grids inside the bay extent, plus nearest outside on both sides (if any).
            var inside = byName.Where(x => x.coord >= min && x.coord <= max).ToList();
            if (inside.Count > 0)
            {
                var below = byName.Where(x => x.coord < min).OrderByDescending(x => x.coord).FirstOrDefault();
                var above = byName.Where(x => x.coord > max).OrderBy(x => x.coord).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(below.name)) inside.Insert(0, below);
                if (!string.IsNullOrWhiteSpace(above.name)) inside.Add(above);
            }
            else
            {
                var nearestMin = byName.OrderBy(x => Math.Abs(x.coord - min)).FirstOrDefault();
                var nearestMax = byName.OrderBy(x => Math.Abs(x.coord - max)).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(nearestMin.name)) inside.Add(nearestMin);
                if (!string.IsNullOrWhiteSpace(nearestMax.name) &&
                    !string.Equals(nearestMax.name, nearestMin.name, StringComparison.OrdinalIgnoreCase))
                {
                    inside.Add(nearestMax);
                }
            }

            foreach (var x in inside)
            {
                if (string.IsNullOrWhiteSpace(x.name)) continue;
                double pos = (x.coord - min) / span;
                labels.Add(new AxisGridLabel { Name = x.name, Position01 = pos });
            }

            return labels;
        }
    }

    #endregion

    #region Existing braces mapping

    public class ExistingBayInfo
    {
        public BayPattern Pattern { get; set; } = BayPattern.None;
        public List<ElementId> BraceIds { get; } = new List<ElementId>();
    }

    public static class ExistingBraceDetector
    {
        public static Dictionary<(int row, int col), ExistingBayInfo> Analyze(
            Document doc,
            Level level,
            IList<BayModel> bays)
        {
            var result = new Dictionary<(int, int), ExistingBayInfo>();
            if (doc == null || level == null || bays == null || bays.Count == 0)
                return result;

            double cornerTol = UnitHelper.MmToInternalLength(100.0); // ~100mm (XY). Z is ignored.
            double zTol = UnitHelper.MmToInternalLength(1500.0);     // ~1500mm

            var candidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            foreach (var fi in candidates)
            {
                if (fi == null) continue;

                // Allow both Brace and Beam because some models (and/or families) represent horizontal bracing as beams.
                if (fi.StructuralType != StructuralType.Brace && fi.StructuralType != StructuralType.Beam)
                    continue;

                if (!(fi.Location is LocationCurve lc)) continue;
                var curve = lc.Curve;
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                // Filter by level more robustly than LevelId (many framing instances have invalid LevelId).
                if (!IsOnLevel(fi, level, zTol))
                    continue;

                // Skip near-axis members; existing braces are expected to be diagonal in plan.
                var v = p1 - p0;
                v = new XYZ(v.X, v.Y, 0);
                if (v.GetLength() < 1e-6) continue;
                var d = v.Normalize();
                double ax = Math.Abs(d.X);
                double ay = Math.Abs(d.Y);
                if (ax < 0.15 || ay < 0.15) continue;

                foreach (var bay in bays)
                {
                    var key = (bay.Row, bay.Column);

                    if (IsPairNear(p0, p1, bay.P00, bay.P11, cornerTol))
                    {
                        AddExisting(result, key, BayPattern.Slash, fi.Id);
                        break;
                    }
                    if (IsPairNear(p0, p1, bay.P01, bay.P10, cornerTol))
                    {
                        AddExisting(result, key, BayPattern.BackSlash, fi.Id);
                        break;
                    }
                }
            }

            return result;
        }

        private static bool IsOnLevel(FamilyInstance fi, Level level, double zTol)
        {
            if (fi == null || level == null) return false;

            try
            {
                if (fi.LevelId != ElementId.InvalidElementId && fi.LevelId == level.Id)
                    return true;
            }
            catch { }

            // Try common level parameters (ElementId)
            try
            {
                var pLevel =
                    fi.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ??
                    fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) ??
                    fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM) ??
                    fi.get_Parameter(BuiltInParameter.LEVEL_PARAM);

                if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                {
                    var eid = pLevel.AsElementId();
                    if (eid != null && eid != ElementId.InvalidElementId && eid == level.Id)
                        return true;
                }
            }
            catch { }

            // Final fallback: compare Z midpoint to level elevation.
            try
            {
                if (fi.Location is LocationCurve lc && lc.Curve != null)
                {
                    var a = lc.Curve.GetEndPoint(0);
                    var b = lc.Curve.GetEndPoint(1);
                    double zMid = 0.5 * (a.Z + b.Z);
                    return Math.Abs(zMid - level.Elevation) <= zTol;
                }

                if (fi.Location is LocationPoint lp && lp.Point != null)
                {
                    return Math.Abs(lp.Point.Z - level.Elevation) <= zTol;
                }
            }
            catch { }

            return false;
        }

        private static bool IsPairNear(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double tol)
        {
            return (IsNearXY(a0, b0, tol) && IsNearXY(a1, b1, tol)) ||
                   (IsNearXY(a0, b1, tol) && IsNearXY(a1, b0, tol));
        }

        private static bool IsNearXY(XYZ p, XYZ q, double tol)
        {
            var dx = p.X - q.X;
            var dy = p.Y - q.Y;
            return dx * dx + dy * dy <= tol * tol;
        }

        private static void AddExisting(
            Dictionary<(int, int), ExistingBayInfo> map,
            (int row, int col) key,
            BayPattern pattern,
            ElementId braceId)
        {
            if (!map.TryGetValue(key, out var info))
            {
                info = new ExistingBayInfo();
                map[key] = info;
            }

            if (!info.BraceIds.Contains(braceId))
            {
                info.BraceIds.Add(braceId);
            }

            if (info.Pattern == BayPattern.None)
            {
                info.Pattern = pattern;
            }
            else if (info.Pattern != pattern)
            {
                info.Pattern = BayPattern.X;
            }
        }
    }

    #endregion

    #region Plan builder / logger / executor

    public static class BracePlanBuilder
    {
        public static BracePlan BuildPlan(
            Document doc,
            Level level,
            BracePromptOptions opt,
            int rowCount,
            int colCount,
            BraceGridViewModel vm)
        {
            var plan = new BracePlan
            {
                ProjectName = doc?.Title ?? string.Empty,
                DocumentTitle = doc?.Title ?? string.Empty,
                LevelName = level?.Name ?? opt.LevelName ?? string.Empty,
                PromptOptions = opt,
                RowCount = rowCount,
                ColumnCount = colCount
            };

            foreach (var bay in vm.Bays)
            {
                var bp = new BayPlan
                {
                    Row = bay.Row,
                    Column = bay.Column,
                    Pattern = bay.Pattern.ToString(),
                    BraceTypeCode = string.IsNullOrWhiteSpace(bay.BraceTypeCode)
                        ? opt.DefaultBraceTypeCode ?? string.Empty
                        : bay.BraceTypeCode,
                    OverrideExisting = bay.OverrideExisting
                };
                plan.Bays.Add(bp);
            }

            return plan;
        }
    }

    public static class BracePlanLogger
    {
        public static string SavePlan(Document doc, RequestCommand cmd, BracePlan plan)
        {
            var logsRoot = Paths.EnsureLocalLogs();
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var projectSafe = MakeSafeFileName(doc?.Title ?? "Project");
            var fileName = $"roof_brace_plan_{projectSafe}_{stamp}.json";
            var path = Path.Combine(logsRoot, fileName);

            var envelope = new
            {
                createdAtUtc = DateTime.UtcNow,
                requestId = cmd.Id?.ToString(),
                projectName = doc?.Title,
                levelName = plan.LevelName,
                plan
            };

            var json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
            File.WriteAllText(path, json);
            return path;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }

    public static class BracePlanExecutor
    {
        public static object Summarize(BracePlan plan)
        {
            int baysWith = plan.Bays.Count(b => !string.Equals(b.Pattern, "None", StringComparison.OrdinalIgnoreCase));
            return new
            {
                baysWithBraces = baysWith,
                braceInstancesCreated = 0
            };
        }

        public static object Execute(
            Document doc,
            Level level,
            BracePlan plan,
            IList<BayModel> bayModels,
            IDictionary<(int row, int col), ExistingBayInfo> existing,
            BracePromptOptions options)
        {
            var bayMap = bayModels.ToDictionary(b => (b.Row, b.Column));

            // Resolve brace types (code -> FamilySymbol)
            var symbolMap = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var bt in options.BraceTypes)
            {
                var sym = ResolveSymbol(doc, bt);
                if (sym != null)
                {
                    symbolMap[bt.Code] = sym;
                }
            }

            int created = 0;
            int baysWith = 0;
            var createdIds = new List<int>();
            double offsetFt = UnitHelper.MmToInternalLength(options.ZOffsetMm);

            using (var tx = new Transaction(doc, "Place Roof Braces From Beams"))
            {
                tx.Start();

                foreach (var bayPlan in plan.Bays)
                {
                    if (!bayMap.TryGetValue((bayPlan.Row, bayPlan.Column), out var bayModel))
                    {
                        continue;
                    }

                    existing.TryGetValue((bayPlan.Row, bayPlan.Column), out var existingInfo);

                    bool hasPattern =
                        !string.Equals(bayPlan.Pattern, "None", StringComparison.OrdinalIgnoreCase);

                    if (bayPlan.OverrideExisting && existingInfo != null && existingInfo.BraceIds.Count > 0)
                    {
                        foreach (var id in existingInfo.BraceIds)
                        {
                            try { doc.Delete(id); } catch { /* ignore */ }
                        }
                    }

                    if (!hasPattern)
                    {
                        // No new braces; if not overriding, existing braces remain.
                        continue;
                    }

                    baysWith++;

                    var code = bayPlan.BraceTypeCode;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        code = options.DefaultBraceTypeCode;
                    }

                    if (string.IsNullOrWhiteSpace(code) || !symbolMap.TryGetValue(code, out var symbol))
                    {
                        // Skip if we cannot resolve type.
                        continue;
                    }

                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                    }

                    // Build brace lines according to pattern
                    var endpoints = new List<Tuple<XYZ, XYZ>>();
                    var p00 = bayModel.P00;
                    var p10 = bayModel.P10;
                    var p01 = bayModel.P01;
                    var p11 = bayModel.P11;

                    if (string.Equals(bayPlan.Pattern, "Slash", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(bayPlan.Pattern, "X", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoints.Add(Tuple.Create(
                            new XYZ(p00.X, p00.Y, p00.Z + offsetFt),
                            new XYZ(p11.X, p11.Y, p11.Z + offsetFt)));
                    }
                    if (string.Equals(bayPlan.Pattern, "BackSlash", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(bayPlan.Pattern, "X", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoints.Add(Tuple.Create(
                            new XYZ(p01.X, p01.Y, p01.Z + offsetFt),
                            new XYZ(p10.X, p10.Y, p10.Z + offsetFt)));
                    }

                    foreach (var tuple in endpoints)
                    {
                        var start = tuple.Item1;
                        var end = tuple.Item2;
                        var line = Line.CreateBound(start, end);
                        // This command is for horizontal bracing. In many projects, horizontal bracing is modeled
                        // as structural framing "Beam" (not "Brace"), even when it is conceptually a brace.
                        var inst = doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                        if (inst != null)
                        {
                            created++;
                            createdIds.Add(inst.Id.IntegerValue);
                            TrySetReferenceLevel(inst, level);
                            TrySetStructuralUsageBrace(inst);
                        }
                    }
                }

                tx.Commit();
            }

            return new
            {
                baysWithBraces = baysWith,
                braceInstancesCreated = created,
                braceElementIds = createdIds
            };
        }

        private static FamilySymbol ResolveSymbol(Document doc, BraceTypeDefinition def)
        {
            if (doc == null) return null;

            var col = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>();

            if (!string.IsNullOrWhiteSpace(def.TypeName))
            {
                col = col.Where(s => string.Equals(s.Name, def.TypeName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(def.FamilyName))
            {
                col = col.Where(s => string.Equals(s.Family?.Name, def.FamilyName, StringComparison.OrdinalIgnoreCase));
            }

            return col.OrderBy(s => s.Family?.Name ?? string.Empty)
                      .ThenBy(s => s.Name ?? string.Empty)
                      .FirstOrDefault();
        }

        /// <summary>
        /// Some structural brace families do not populate LevelId even when created with a level.
        /// Try to set the reference/host level parameter explicitly so view filters can recognize it.
        /// </summary>
        private static void TrySetReferenceLevel(FamilyInstance inst, Level level)
        {
            if (inst == null || level == null) return;
            try
            {
                Parameter p =
                    inst.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ??
                    inst.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) ??
                    inst.LookupParameter("参照レベル") ??
                    inst.LookupParameter("レベル") ??
                    inst.LookupParameter("Reference Level") ??
                    inst.LookupParameter("Level");

                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                {
                    p.Set(level.Id);
                }
            }
            catch
            {
                // best-effort; ignore if family doesn't support level assignment
            }
        }

        private static void TrySetStructuralUsageBrace(FamilyInstance inst)
        {
            if (inst == null) return;
            try
            {
                var p =
                    inst.LookupParameter("構造用途") ??
                    inst.LookupParameter("Structural Usage") ??
                    inst.LookupParameter("Usage");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                {
                    // 7 is "Brace" in Revit's Structural Usage enumeration in typical templates.
                    // If the project uses a different mapping, leaving it unchanged is acceptable.
                    p.Set(7);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    #endregion
}
