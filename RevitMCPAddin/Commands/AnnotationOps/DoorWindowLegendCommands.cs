// ================================================================
// File: Commands/AnnotationOps/DoorWindowLegendCommands.cs
// Purpose : Door/Window schedule support
//           - get_door_window_types_for_schedule
//           - create_door_window_legend_by_mark
// Target  : .NET Framework 4.8 / Revit 2023+
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    /// <summary>
    /// get_door_window_types_for_schedule
    /// Doors / Windows の FamilySymbol を走査し、Type Mark ごとのタイプ情報を返す。
    /// </summary>
    public class GetDoorWindowTypesForScheduleCommand : IRevitCommandHandler
    {
        public string CommandName => "get_door_window_types_for_schedule";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            // categories: ["Doors","Windows"] など
            var catsToken = p["categories"] as JArray;
            bool includeDoors = true;
            bool includeWindows = true;
            if (catsToken != null && catsToken.HasValues)
            {
                var list = catsToken.Values<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                includeDoors = list.Contains("Doors");
                includeWindows = list.Contains("Windows");
            }

            string markParamName = p.Value<string>("markParameter") ?? "Type Mark";
            bool onlyUsed = p.Value<bool?>("onlyUsedInModel") ?? false;

            var types = new List<object>();

            // 先に Door/Window の FamilyInstance を集計しておき、SymbolId ごとのインスタンス数を計算する
            var instanceCountBySymbol = new Dictionary<ElementId, int>();

            if (includeDoors)
            {
                var doorInsts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
                foreach (var fi in doorInsts)
                {
                    var sid = fi.Symbol?.Id;
                    if (sid == null) continue;
                    instanceCountBySymbol.TryGetValue(sid, out var c);
                    instanceCountBySymbol[sid] = c + 1;
                }
            }

            if (includeWindows)
            {
                var winInsts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
                foreach (var fi in winInsts)
                {
                    var sid = fi.Symbol?.Id;
                    if (sid == null) continue;
                    instanceCountBySymbol.TryGetValue(sid, out var c);
                    instanceCountBySymbol[sid] = c + 1;
                }
            }

            void CollectFromCategory(BuiltInCategory bic, string categoryLabel)
            {
                var symbols = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();

                foreach (var sym in symbols)
                {
                    int count = 0;
                    instanceCountBySymbol.TryGetValue(sym.Id, out count);
                    if (onlyUsed && count == 0) continue;

                    // Type Mark 等のマークパラメータ（言語に依存しないよう BuiltInParameter も併用）
                    string mark = ResolveMark(sym, markParamName);

                    // 幅・高さ（mm）。見つからなければ 0。
                    double widthMm = 0.0;
                    double heightMm = 0.0;

                    BuiltInParameter bipWidth = (bic == BuiltInCategory.OST_Doors)
                        ? BuiltInParameter.DOOR_WIDTH
                        : BuiltInParameter.WINDOW_WIDTH;
                    BuiltInParameter bipHeight = (bic == BuiltInCategory.OST_Doors)
                        ? BuiltInParameter.DOOR_HEIGHT
                        : BuiltInParameter.WINDOW_HEIGHT;

                    try
                    {
                        var pw = sym.get_Parameter(bipWidth);
                        if (pw != null && pw.StorageType == StorageType.Double)
                        {
                            var feet = pw.AsDouble();
                            widthMm = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
                        }
                    }
                    catch { /* ignore */ }

                    try
                    {
                        var ph = sym.get_Parameter(bipHeight);
                        if (ph != null && ph.StorageType == StorageType.Double)
                        {
                            var feet = ph.AsDouble();
                            heightMm = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
                        }
                    }
                    catch { /* ignore */ }

                    types.Add(new
                    {
                        category = categoryLabel,
                        familyName = sym.FamilyName ?? string.Empty,
                        typeName = sym.Name ?? string.Empty,
                        mark,
                        typeId = sym.Id.IntValue(),
                        width_mm = widthMm,
                        height_mm = heightMm,
                        instanceCount = count
                    });
                }
            }

            if (includeDoors)
            {
                CollectFromCategory(BuiltInCategory.OST_Doors, "Doors");
            }

            if (includeWindows)
            {
                CollectFromCategory(BuiltInCategory.OST_Windows, "Windows");
            }

            // Type Mark パラメータが見つからない場合の簡易チェック（全タイプ空）
            if (types.Count > 0 && types.All(t =>
            {
                var mt = t.GetType();
                var piMark = mt.GetProperty("mark");
                var v = piMark?.GetValue(t) as string;
                return string.IsNullOrWhiteSpace(v);
            }))
            {
                return new
                {
                    ok = false,
                    msg = $"Parameter '{markParamName}' not found or empty on door/window types."
                };
            }

            return new
            {
                ok = true,
                types
            };
        }

        /// <summary>
        /// Type Mark 等のマークを言語非依存で取得するヘルパ。
        /// 優先順位:
        /// 1) LookupParameter(markParamName)
        /// 2) BuiltInParameter.ALL_MODEL_TYPE_MARK (ローカライズ名でも取得可能)
        /// 3) 日本語等の代表的な別名 ("マーク(タイプ)" など) を Lookup
        /// </summary>
        internal static string ResolveMark(FamilySymbol sym, string markParamName)
        {
            try
            {
                Parameter pr = null;

                // 1) ユーザー指定名で探索
                if (!string.IsNullOrWhiteSpace(markParamName))
                {
                    pr = sym.LookupParameter(markParamName);
                }

                // 2) Type Mark 用の BuiltInParameter を試す
                //    （日本語 UI では "マーク(タイプ)" と表示されることが多い）
                if (pr == null)
                {
                    try
                    {
                        pr = sym.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
                    }
                    catch
                    {
                        pr = null;
                    }
                }

                // 3) 代表的なローカライズ名で再探索（例: 日本語環境の "マーク(タイプ)"）
                if (pr == null)
                {
                    // 必要に応じて別名を追加可能
                    string[] aliases =
                    {
                        "マーク(タイプ)",
                        "タイプ マーク"
                    };

                    foreach (var alias in aliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) continue;
                        try
                        {
                            pr = sym.LookupParameter(alias);
                            if (pr != null) break;
                        }
                        catch
                        {
                            pr = null;
                        }
                    }
                }

                if (pr == null) return string.Empty;

                if (pr.StorageType == StorageType.String)
                {
                    return pr.AsString() ?? string.Empty;
                }

                return pr.AsValueString() ?? string.Empty;
            }
            catch
            {
                // 取得に失敗した場合は空文字を返す
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// create_door_window_legend_by_mark
    /// Legend View に Type Mark ごとの LegendComponent とラベルをグリッド配置する。
    /// </summary>
    public class CreateDoorWindowLegendByMarkCommand : IRevitCommandHandler
    {
        public string CommandName => "create_door_window_legend_by_mark";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            string legendViewName = p.Value<string>("legendViewName") ?? "DoorWindow_Legend_Elevations";
            string markParamName = p.Value<string>("markParameter") ?? "Type Mark";

            bool includeDoors = p.Value<bool?>("includeDoors") ?? true;
            bool includeWindows = p.Value<bool?>("includeWindows") ?? true;

            var layout = p["layout"] as JObject ?? new JObject();
            int maxColumns = layout.Value<int?>("maxColumns") ?? 5;
            double cellWidthMm = layout.Value<double?>("cellWidth_mm") ?? 2000.0;
            double cellHeightMm = layout.Value<double?>("cellHeight_mm") ?? 2000.0;

            var startPoint = layout["startPoint"] as JObject ?? new JObject();
            double startXmm = startPoint.Value<double?>("x_mm") ?? 0.0;
            double startYmm = startPoint.Value<double?>("y_mm") ?? 0.0;

            var labelOptions = p["labelOptions"] as JObject ?? new JObject();
            bool showMark = labelOptions.Value<bool?>("showMark") ?? true;
            bool showSize = labelOptions.Value<bool?>("showSize") ?? true;
            bool showFamilyType = labelOptions.Value<bool?>("showFamilyType") ?? true;

            bool clearExisting = p.Value<bool?>("clearExisting") ?? true;

            // Legend View の取得（作成は API 仕様が Revit バージョンにより異なるので、まずは既存ビュー前提とする）
            View legend = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.Legend &&
                                     string.Equals(v.Name ?? "", legendViewName, StringComparison.OrdinalIgnoreCase));

            using (var tx = new Transaction(doc, "Create Door/Window Legend by Mark"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    if (legend == null)
                    {
                        tx.RollBack();
                        return new { ok = false, msg = $"Legend ビュー '{legendViewName}' が見つかりません。先に Legend ビューを作成してください。" };
                    }

                    // 既存の LegendComponent / TextNote を削除
                    if (clearExisting)
                    {
                        var toDelete = new FilteredElementCollector(doc, legend.Id)
                            .WhereElementIsNotElementType()
                            .Where(e =>
                            {
                                if (e is TextNote) return true;
                                var t = e.GetType();
                                return string.Equals(t.Name, "LegendComponent", StringComparison.Ordinal);
                            })
                            .Select(e => e.Id)
                            .ToList();
                        if (toDelete.Count > 0)
                        {
                            doc.Delete(toDelete);
                        }
                    }

                    // レイアウト用スケール（mm → ft）
                    double cellWidthFt = UnitUtils.ConvertToInternalUnits(cellWidthMm, UnitTypeId.Millimeters);
                    double cellHeightFt = UnitUtils.ConvertToInternalUnits(cellHeightMm, UnitTypeId.Millimeters);
                    double startXft = UnitUtils.ConvertToInternalUnits(startXmm, UnitTypeId.Millimeters);
                    double startYft = UnitUtils.ConvertToInternalUnits(startYmm, UnitTypeId.Millimeters);

                    // TextNoteType（既定）を取得
                    var textNoteType = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType))
                        .Cast<TextNoteType>()
                        .FirstOrDefault();

                    // Door/Window タイプを集計（GetDoorWindowTypesForScheduleCommand のロジック簡易版）
                    var symbolsByMark = new List<(string mark, FamilySymbol symbol, string category, double widthMm, double heightMm)>();

                    void CollectSymbols(BuiltInCategory bic, string categoryLabel)
                    {
                        var symbols = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>();

                        foreach (var sym in symbols)
                        {
                            string mark = GetDoorWindowTypesForScheduleCommand.ResolveMark(sym, markParamName);

                            if (string.IsNullOrWhiteSpace(mark)) continue;

                            // サイズは mm で記録（Legend 上のラベル用）
                            double widthMmLocal = 0.0;
                            double heightMmLocal = 0.0;

                            BuiltInParameter bipWidth = (bic == BuiltInCategory.OST_Doors)
                                ? BuiltInParameter.DOOR_WIDTH
                                : BuiltInParameter.WINDOW_WIDTH;
                            BuiltInParameter bipHeight = (bic == BuiltInCategory.OST_Doors)
                                ? BuiltInParameter.DOOR_HEIGHT
                                : BuiltInParameter.WINDOW_HEIGHT;

                            try
                            {
                                var pw = sym.get_Parameter(bipWidth);
                                if (pw != null && pw.StorageType == StorageType.Double)
                                {
                                    var feet = pw.AsDouble();
                                    widthMmLocal = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
                                }
                            }
                            catch { }

                            try
                            {
                                var ph = sym.get_Parameter(bipHeight);
                                if (ph != null && ph.StorageType == StorageType.Double)
                                {
                                    var feet = ph.AsDouble();
                                    heightMmLocal = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
                                }
                            }
                            catch { }

                            symbolsByMark.Add((mark, sym, categoryLabel, widthMmLocal, heightMmLocal));
                        }
                    }

                    if (includeDoors)
                        CollectSymbols(BuiltInCategory.OST_Doors, "Doors");
                    if (includeWindows)
                        CollectSymbols(BuiltInCategory.OST_Windows, "Windows");

                    if (symbolsByMark.Count == 0)
                    {
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            viewId = legend.Id.IntValue(),
                            placedCount = 0,
                            msg = "ドア/窓タイプが見つかりませんでした。"
                        };
                    }

                    // mark ごとに代表タイプを 1 つ選択
                    var groups = symbolsByMark
                        .GroupBy(x => x.mark)
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .ToList();

                    int placed = 0;
                    int row = 0;
                    int col = 0;
                    bool legendApiUnavailable = false;

                    foreach (var g in groups)
                    {
                        var first = g.First();
                        var sym = first.symbol;

                        // 配置座標（Legend 上。Y 軸は上方向を正とし、行ごとに下方向へずらす）
                        if (placed > 0 && placed % maxColumns == 0)
                        {
                            row++;
                            col = 0;
                        }
                        else if (placed > 0)
                        {
                            col++;
                        }

                        double x = startXft + col * cellWidthFt;
                        double y = startYft - row * cellHeightFt;
                        var pt = new XYZ(x, y, 0);

                        // LegendComponent の作成
                        // 直接型参照できない環境もあるため、Reflection 経由で呼び出す
                        object compObj = null;
                        try
                        {
                            var asm = typeof(View).Assembly;
                            var lcType = asm.GetType("Autodesk.Revit.DB.LegendComponent");
                            if (lcType == null)
                            {
                                // この Revit API には LegendComponent が無い
                                legendApiUnavailable = true;
                                continue;
                            }

                            var create = lcType.GetMethod(
                                "Create",
                                new[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(XYZ) });
                            if (create == null)
                            {
                                legendApiUnavailable = true;
                                continue;
                            }

                            compObj = create.Invoke(null, new object[] { doc, legend.Id, sym.Id, pt });
                        }
                        catch
                        {
                            compObj = null;
                        }

                        if (compObj == null)
                        {
                            // このタイプは配置できなかったのでスキップ
                            continue;
                        }

                        placed++;

                        if (textNoteType != null && (showMark || showSize || showFamilyType))
                        {
                            var lines = new List<string>();
                            if (showMark)
                                lines.Add(first.mark);
                            if (showSize)
                            {
                                if (first.widthMm > 0 && first.heightMm > 0)
                                {
                                    lines.Add($"{Math.Round(first.widthMm)} x {Math.Round(first.heightMm)} mm");
                                }
                            }
                            if (showFamilyType)
                            {
                                var fam = sym.FamilyName ?? string.Empty;
                                var tname = sym.Name ?? string.Empty;
                                lines.Add($"{fam} : {tname}");
                            }

                            if (lines.Count > 0)
                            {
                                string text = string.Join("\n", lines);
                                // シンボルの少し下にラベルを配置
                                var labelPt = pt + new XYZ(0, -0.3 * cellHeightFt, 0);
                                TextNote.Create(doc, legend.Id, labelPt, text,
                                    new TextNoteOptions(textNoteType.Id));
                            }
                        }
                    }

                    tx.Commit();

                    if (placed == 0 && legendApiUnavailable)
                    {
                        return new
                        {
                            ok = false,
                            viewId = legend.Id.IntValue(),
                            viewName = legend.Name,
                            placedCount = 0,
                            msg = "LegendComponent API がこの Revit バージョンで利用できないため、凡例シンボルを配置できませんでした。"
                        };
                    }

                    return new
                    {
                        ok = true,
                        viewId = legend.Id.IntValue(),
                        viewName = legend.Name,
                        placedCount = placed
                    };
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }
}

