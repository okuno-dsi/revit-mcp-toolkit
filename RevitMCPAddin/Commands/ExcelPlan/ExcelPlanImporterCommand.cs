// ================================================================
// ================================================================
// File: Commands/ExcelPlan/ExcelPlanImporterCommand.cs
// Desc: Excelの罫線から水平/垂直の最大線分を抽出し、原点=左下に正規化して
//       Revitに Walls または ModelLines として再現。さらに、セル文字列を
//       部屋名ラベルとして抽出し座標を返す。線分には罫線スタイルを付与。
// JSON-RPC method: "excel_plan_import"
// Params:
//   excelPath        : string (必須)
//   cellSizeMeters   : double (既定=1.0)
//   mode             : "Walls" | "ModelLines" (既定="Walls")
//   wallTypeName     : string (Walls時推奨, 例 "RC150")
//   levelName        : string (必須, Revit側レベル名)
//   baseOffsetMm     : double (既定=0) 壁ベースオフセット(mm)
//   flip             : bool (既定=false) 壁方向反転
//   toleranceCell    : double (既定=0.001)
// Return (例):
//   {
//     ok: true,
//     created: 42,
//     totalLengthMeters: 123.45,
//     widthCells: 30,
//     heightCells: 20,
//     cellSizeMeters: 1.0,
//     mode: "Walls",
//     segmentsDetailed: [ { kind, x1,y1,x2,y2,length_m, style } ],
//     labels: [ { text, x, y } ]
//   }
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

// ★ 追加: ClosedXML
using ClosedXML.Excel;                    // XLWorkbook, IXLCell, IXLBorder など

namespace RevitMCPAddin.Commands.ExcelPlan
{
    public class ExcelPlanImporterCommand : IRevitCommandHandler
    {
        private static bool IsColoredFill(ClosedXML.Excel.IXLFill fill)
        {
            if (fill == null) return false;
            try
            {
                // Any explicit pattern is treated as colored (intended scan region)
                if (fill.PatternType != ClosedXML.Excel.XLFillPatternValues.None)
                    return true;
                // BackgroundColor textual fallback (avoid System.Drawing)
                var txt = (fill.BackgroundColor != null ? fill.BackgroundColor.ToString() : string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(txt)) return false;
                if (txt.IndexOf("White", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (txt.IndexOf("NoColor", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return true;
            }
            catch { return false; }
        }

        public string CommandName => "excel_plan_import";

        private const double FEET_PER_METER = 3.28083989501312;

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            var excelPath = p.Value<string>("excelPath");
            var sheetName = p.Value<string>("sheetName");
            var cellSizeMeters = p.Value<double?>("cellSizeMeters") ?? 1.0;
            var mode = (p.Value<string>("mode") ?? "Walls").Trim();
            var wallTypeName = p.Value<string>("wallTypeName") ?? "RC150";
            var levelName = p.Value<string>("levelName");
            var baseOffsetMm = p.Value<double?>("baseOffsetMm") ?? 0.0;
            var flip = p.Value<bool?>("flip") ?? false;
            var tolCell = p.Value<double?>("toleranceCell") ?? 0.001;
            var topLevelName = p.Value<string>("topLevelName");
            var heightMm = p.Value<double?>("heightMm");
            var placeRooms = p.Value<bool?>("placeRooms") ?? false;
            var roomLevelName = p.Value<string>("roomLevelName") ?? levelName;
            var roomPhaseName = p.Value<string>("roomPhaseName");
            var setRoomNameFromLabel = p.Value<bool?>("setRoomNameFromLabel") ?? true;
            var ensurePerimeter = p.Value<bool?>("ensurePerimeter") ?? false;
            var exportOnly = p.Value<bool?>("exportOnly") ?? false;
            var placeGrids = p.Value<bool?>("placeGrids") ?? false;
            var debugWriteBack = p.Value<bool?>("debugWriteBack") ?? false;
            var debugSheetName = p.Value<string>("debugSheetName") ?? "Recreated";
            var useColorMask = p.Value<bool?>("useColorMask") ?? true;

            if (string.IsNullOrWhiteSpace(excelPath))
                return new { ok = false, msg = "excelPath が未指定です。" };
            if (string.IsNullOrWhiteSpace(levelName))
                return new { ok = false, msg = "levelName が未指定です。" };

            try
            {
                // 1) Excel解析（ExcelMCPサービス優先, 失敗時ローカルClosedXMLでフォールバック）
                var meters = cellSizeMeters;
                var baseOffsetFt = (baseOffsetMm / 1000.0) * FEET_PER_METER;
                int width = 0, height = 0;
                List<SegStyled> normSegments = null;
                List<LabelOut> normLabels = null;

                // 外部サービスURL（param > env）
                string serviceUrl = (p.Value<string>("serviceUrl") ?? Environment.GetEnvironmentVariable("EXCEL_MCP_URL"))?.Trim();
                if (!string.IsNullOrWhiteSpace(serviceUrl))
                {
                    try
                    {
                        if (!serviceUrl.EndsWith("/")) serviceUrl += "/";
                        var url = serviceUrl + "parse_plan";
                        using (var hc = new System.Net.Http.HttpClient())
                        {
                             var reqObj = new JObject
                             {
                                 ["excelPath"] = excelPath,
                                 ["sheetName"] = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName,
                                 ["cellSizeMeters"] = meters,
                                 ["useColorMask"] = useColorMask
                             };
                            var content = new System.Net.Http.StringContent(JsonNetCompat.ToCompactJson(reqObj), System.Text.Encoding.UTF8, "application/json");
                            var resp = hc.PostAsync(url, content).GetAwaiter().GetResult();
                            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            var json = JObject.Parse(body);
                            if (json.Value<bool?>("ok") == true || (json["ok"] == null && json["segmentsDetailed"] != null))
                            {
                                width = json.Value<int?>("widthCells") ?? 0;
                                height = json.Value<int?>("heightCells") ?? 0;
                                var segsArr = (JArray)json["segmentsDetailed"] ?? new JArray();
                                normSegments = segsArr.Select(s => new SegStyled(
                                    s.Value<double>("x1"), s.Value<double>("y1"),
                                    s.Value<double>("x2"), s.Value<double>("y2"),
                                    s.Value<string>("style") ?? ""
                                )).ToList();

                                var labelsArr = (JArray)json["labels"] ?? new JArray();
                                normLabels = labelsArr.Select(l => new LabelOut
                                {
                                    Text = l.Value<string>("text") ?? string.Empty,
                                    X = l.Value<double?>("x") ?? 0.0,
                                    Y = l.Value<double?>("y") ?? 0.0
                                }).ToList();
                            }
                        }
                    }
                    catch { /* サービス失敗時はローカル解析へフォールバック */ }
                }

                if (normSegments == null)
                {
                    // ローカル解析: 単位エッジ(+スタイル) と ラベル（部屋名）
                    var parsed = useColorMask
                        ? ExtractFromExcel(excelPath, sheetName, out width, out height)
                        : ExtractFromExcelNoColor(excelPath, sheetName, out width, out height);

                    if (parsed.UnitEdges.Count == 0 && parsed.Labels.Count == 0)
                        return new { ok = false, msg = "Excelに罫線や文字が見つかりませんでした。", widthCells = width, heightCells = height };

                    // 最大線分へ連結 → 左下原点へ正規化 + mスケール
                    var segments = CollapseToMaxSegmentsWithStyle(parsed.UnitEdges);
                    int H = height; // Excelは上原点 → 反転
                    normSegments = segments.Select(s =>
                    {
                        double nx1 = s.X1;
                        double ny1 = H - s.Y1;
                        double nx2 = s.X2;
                        double ny2 = H - s.Y2;
                        return new SegStyled(nx1 * meters, ny1 * meters, nx2 * meters, ny2 * meters, s.StyleKey);
                    }).ToList();

                    normLabels = parsed.Labels.Select(l =>
                    {
                        double nx = (l.CellX + 0.5);
                        double ny = H - (l.CellY + 0.5);
                        return new LabelOut { Text = l.Text, X = nx * meters, Y = ny * meters };
                    }).ToList();
                }

                if (ensurePerimeter)
                {
                    double maxX = width * meters;
                    double maxY = height * meters;
                    normSegments.Add(new SegStyled(0.0, 0.0, maxX, 0.0, "Perimeter"));
                    normSegments.Add(new SegStyled(maxX, 0.0, maxX, maxY, "Perimeter"));
                    normSegments.Add(new SegStyled(maxX, maxY, 0.0, maxY, "Perimeter"));
                    normSegments.Add(new SegStyled(0.0, maxY, 0.0, 0.0, "Perimeter"));
                }

                // 4) Revitへ投影（座標のみ使用）
                double totalLenMetersPre = 0.0;
                foreach (var s in normSegments)
                {
                    totalLenMetersPre += Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1));
                }
                dynamic result =
                    exportOnly
                    ? new { ok = true, created = 0, totalLengthMeters = totalLenMetersPre }
                    : (mode.Equals("Walls", StringComparison.OrdinalIgnoreCase)
                    ? CreateWalls(doc, normSegments, wallTypeName, levelName, baseOffsetFt, flip, topLevelName, heightMm)
                    : CreateModelLines(uiapp, doc, normSegments));

                // 5) 詳細（スタイル・ラベル）を応答へ添付
                var segmentsDetailed = normSegments.Select(s => new
                {
                    kind = (Math.Abs(s.Y1 - s.Y2) < 1e-9) ? "H" : (Math.Abs(s.X1 - s.X2) < 1e-9 ? "V" : "Other"),
                    x1 = s.X1,
                    y1 = s.Y1,
                    x2 = s.X2,
                    y2 = s.Y2,
                    length_m = Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1)),
                    style = s.StyleKey
                }).ToList();

                int roomsCreated = 0; int roomsFailed = 0; int gridsCreated = 0;
                if (placeRooms && !exportOnly && normLabels.Count > 0)
                {
                    var targetLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name.Equals(roomLevelName, StringComparison.OrdinalIgnoreCase)) ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                    using (var tx = new Transaction(doc, "Excel Plan Import (Rooms)"))
                    {
                        tx.Start();
                        foreach (var lab in normLabels)
                        {
                            try
                            {
                                var uv = new UV(lab.X * FEET_PER_METER, lab.Y * FEET_PER_METER);
                                Autodesk.Revit.DB.Architecture.Room rm = doc.Create.NewRoom(targetLevel, uv);
                                if (rm != null)
                                {
                                    roomsCreated++;
                                    if (setRoomNameFromLabel && !string.IsNullOrWhiteSpace(lab.Text))
                                    {
                                        var pName = rm.get_Parameter(BuiltInParameter.ROOM_NAME);
                                        if (pName != null && !pName.IsReadOnly) pName.Set(lab.Text);
                                    }
                                }
                                else
                                {
                                    roomsFailed++;
                                }
                            }
                            catch
                            {
                                roomsFailed++;
                            }
                        }
                        tx.Commit();
                    }
                }
                                // Create Grids from Excel rows (28+) (optional)
                if (placeGrids)
                {
                    try
                    {
                        using (var wb2 = new ClosedXML.Excel.XLWorkbook(excelPath))
                        {
                            var ws2 = wb2.Worksheets.First();
                            var used2 = ws2.RangeUsed();
                            if (used2 != null)
                            {
                                int rStart = 28;
                                int cStart = used2.FirstColumn().ColumnNumber();
                                int lastRow = used2.LastRow().RowNumber();
                                double maxX = width * meters;
                                double maxY = height * meters;
                                // use outer gridsCreated counter
                                for (int r = rStart; r <= lastRow; r++)
                                {
                                    var nameCell = ws2.Cell(r, cStart);
                                    var axisCell = ws2.Cell(r, cStart + 1);
                                    var posCell = ws2.Cell(r, cStart + 2);
                                    string gname = (nameCell?.GetFormattedString() ?? string.Empty).Trim();
                                    string axis = (axisCell?.GetFormattedString() ?? string.Empty).Trim().ToUpperInvariant();
                                    if (string.IsNullOrWhiteSpace(gname) || string.IsNullOrWhiteSpace(axis)) continue;
                                    double posVal;
                                    bool okNum = double.TryParse((posCell?.GetFormattedString() ?? string.Empty).Trim(), out posVal);
                                    if (!okNum) { if (posCell != null && posCell.TryGetValue<double>(out var v)) { posVal = v; okNum = true; } }
                                    if (!okNum) continue;
                                    double posM = posVal;
                                    if (Math.Abs(posVal) > 100.0) posM = posVal / 1000.0; // assume mm if big
                                    using (var txg = new Transaction(doc, "Excel Plan Import (Grids)"))
                                    {
                                        txg.Start();
                                        try
                                        {
                                            Line gl;
                                            if (axis == "X")
                                            {
                                                var x = posM * FEET_PER_METER;
                                                gl = Line.CreateBound(new XYZ(x, 0, 0), new XYZ(x, maxY * FEET_PER_METER, 0));
                                            }
                                            else if (axis == "Y")
                                            {
                                                var y = posM * FEET_PER_METER;
                                                gl = Line.CreateBound(new XYZ(0, y, 0), new XYZ(maxX * FEET_PER_METER, y, 0));
                                            }
                                            else { continue; }
                                            var grid = Grid.Create(doc, gl);
                                            try { if (grid != null && !string.IsNullOrWhiteSpace(gname)) grid.Name = gname; } catch { }
                                            gridsCreated++;
                                        }
                                        catch {}
                                        txg.Commit();
                                    }
                                }
                            }
                        }
                    }
                    catch {}
                }

                // Debug write-back: create a new sheet and render unit edges and labels
                if (debugWriteBack)
                {
                    try
                    {
                        using (var wbw = new XLWorkbook(excelPath))
                        {
                            if (string.IsNullOrWhiteSpace(debugSheetName)) debugSheetName = "Recreated";
                            if (wbw.Worksheets.Contains(debugSheetName))
                            {
                                wbw.Worksheet(debugSheetName).Delete();
                            }
                            var wsOut = wbw.Worksheets.Add(debugSheetName);
                            // Fallback: render from normalized segments/labels using cell size
                            foreach (var s in normSegments)
                            {
                                // convert meters to cell indices (round to nearest cell)
                                int cx1 = (int)System.Math.Round(s.X1 / meters);
                                int cy1 = (int)System.Math.Round(s.Y1 / meters);
                                int cx2 = (int)System.Math.Round(s.X2 / meters);
                                int cy2 = (int)System.Math.Round(s.Y2 / meters);
                                if (cy1 == cy2)
                                {
                                    int row = cy1 + 1; int col = System.Math.Min(cx1, cx2) + 1;
                                    wsOut.Cell(row, col).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                                }
                                else if (cx1 == cx2)
                                {
                                    int row = System.Math.Min(cy1, cy2) + 1; int col = cx1 + 1;
                                    wsOut.Cell(row, col).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
                                }
                            }
                            foreach (var l in normLabels)
                            {
                                // convert to cell indices
                                int cx = (int)System.Math.Round(l.X / meters);
                                int cy = (int)System.Math.Round(l.Y / meters);
                                wsOut.Cell(cy + 1, cx + 1).Value = l.Text ?? string.Empty;
                            }
                            wbw.Save();
                        }
                    }
                    catch { }
                }

                var response = new JObject
                {
                    ["ok"] = (bool)result.ok,
                    ["created"] = (int)result.created,
                    ["totalLengthMeters"] = (double)result.totalLengthMeters,
                    ["widthCells"] = width,
                    ["heightCells"] = height,
                    ["cellSizeMeters"] = meters,
                    ["mode"] = mode,
                    ["gridsCreated"] = gridsCreated,
                    ["segmentsDetailed"] = JArray.FromObject(segmentsDetailed),
                    ["roomsCreated"] = roomsCreated,
                    ["roomsFailed"] = roomsFailed,
                    ["labels"] = JArray.FromObject(normLabels.Select(l => new { text = l.Text, x = l.X, y = l.Y }))
                };

                return response;
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"excel_plan_import failed: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        // ---------------- 補助型 ----------------

        private class UnitEdge // 単位エッジ（1セル分の辺, 格子座標）
        {
            public int X1, Y1, X2, Y2;
            public string StyleKey; // 罫線スタイル名（"Thin"など）

            public bool IsHoriz => (Y1 == Y2) && (Math.Abs(X2 - X1) == 1);
            public bool IsVert => (X1 == X2) && (Math.Abs(Y2 - Y1) == 1);
        }

        private class SegStyled // 連結後の線分（m, 左下原点）＋スタイル
        {
            public double X1, Y1, X2, Y2;
            public string StyleKey;
            public SegStyled(double x1, double y1, double x2, double y2, string style)
            { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; StyleKey = style ?? "None"; }
        }

        // ★ 追加: LabelOut の定義
        private class LabelOut
        {
            public string Text;
            public double X;
            public double Y;
        }

        private class LabelCell
        {
            public string Text;
            public int CellX; // 左上原点の列オフセット
            public int CellY; // 左上原点の行オフセット
        }

        private class ParseResult
        {
            public List<UnitEdge> UnitEdges = new List<UnitEdge>();
            public List<LabelCell> Labels = new List<LabelCell>();
        }

        // ---------------- Excel抽出（罫線スタイル＋ラベル） ----------------

        private static int StyleWeight(XLBorderStyleValues s)
        {
            // 強弱の簡易スコア（両側セルでスタイル違いのとき強い方を採用）
            switch (s)
            {
                case XLBorderStyleValues.None: return 0;
                case XLBorderStyleValues.Hair: return 1;
                case XLBorderStyleValues.Dotted: return 2;
                case XLBorderStyleValues.Dashed: return 3;
                case XLBorderStyleValues.Thin: return 4;
                case XLBorderStyleValues.MediumDashed: return 5;
                case XLBorderStyleValues.Medium: return 6;
                case XLBorderStyleValues.Thick: return 7;
                case XLBorderStyleValues.Double: return 8;
                // 他は中庸
                default: return 5;
            }
        }

        private static string StyleKey(XLBorderStyleValues s)
        {
            return s.ToString();
        }

        private static ParseResult ExtractFromExcel(string path, string sheetName, out int width, out int height)
        {
            var pr = new ParseResult();

            using (var wb = new XLWorkbook(path))
            {
                var ws = (!string.IsNullOrWhiteSpace(sheetName) ? wb.Worksheet(sheetName) : wb.Worksheets.First());
                var used = ws.RangeUsed();
                if (used == null)
                {
                    width = height = 0; return pr;
                }
                int r1 = used.FirstRow().RowNumber();
                int c1 = used.FirstColumn().ColumnNumber();
                int r2 = used.LastRow().RowNumber();
                int c2 = used.LastColumn().ColumnNumber();
                // Bounding box of non-white filled cells (plan area)
                int rr1 = int.MaxValue, cc1 = int.MaxValue, rr2 = int.MinValue, cc2 = int.MinValue;
                for (int rr = r1; rr <= r2; rr++)
                {
                    for (int cc = c1; cc <= c2; cc++)
                    {
                        var cell = ws.Cell(rr, cc);
                        var fill = cell.Style.Fill;
                        if (IsColoredFill(fill))
                        {
                            if (rr < rr1) rr1 = rr; if (rr > rr2) rr2 = rr;
                            if (cc < cc1) cc1 = cc; if (cc > cc2) cc2 = cc;
                        }
                    }
                }
                if (rr1 != int.MaxValue && cc1 != int.MaxValue)
                {
                    // Use colored bounding box
                    r1 = rr1; c1 = cc1; r2 = rr2; c2 = cc2;
                }

                width = (c2 - c1 + 1);
                height = (r2 - r1 + 1);

                // 辺ごとの“最強スタイル”を保持
                var edgeBest = new Dictionary<(int, int, int, int), (int weight, string style)>();

                for (int r = r1; r <= r2; r++)
                {
                    for (int c = c1; c <= c2; c++)
                    {
                        var cell = ws.Cell(r, c);
                        var b = cell.Style.Border;

                        int cx = c - c1; // 左上原点の列オフセット
                        int cy = r - r1; // 左上原点の行オフセット

                        void AddEdgeIf(XLBorderStyleValues style, int x1, int y1, int x2, int y2)
                        {
                            if (style == XLBorderStyleValues.None) return;
                            var key = (x1 <= x2 || (x1 == x2 && y1 <= y2)) ? (x1, y1, x2, y2) : (x2, y2, x1, y1);
                            int w = StyleWeight(style);
                            string sk = StyleKey(style);
                            if (edgeBest.TryGetValue(key, out var cur))
                            {
                                if (w > cur.weight) edgeBest[key] = (w, sk);
                            }
                            else
                            {
                                edgeBest[key] = (w, sk);
                            }
                        }

                        AddEdgeIf(b.TopBorder, cx, cy, cx + 1, cy);
                        AddEdgeIf(b.BottomBorder, cx, cy + 1, cx + 1, cy + 1);
                        AddEdgeIf(b.LeftBorder, cx, cy, cx, cy + 1);
                        AddEdgeIf(b.RightBorder, cx + 1, cy, cx + 1, cy + 1);

                        // ラベル（空白でなければ採用）
                        var txt = cell.GetFormattedString();
                        if (!string.IsNullOrWhiteSpace(txt))
                        {
                            pr.Labels.Add(new LabelCell { Text = txt.Trim(), CellX = cx, CellY = cy });
                        }
                    }
                }

                foreach (var kv in edgeBest)
                {
                    var key = kv.Key;
                    var val = kv.Value;
                    pr.UnitEdges.Add(new UnitEdge
                    {
                        X1 = key.Item1,
                        Y1 = key.Item2,
                        X2 = key.Item3,
                        Y2 = key.Item4,
                        StyleKey = val.style
                    });
                }
            }

            return pr;
        }

        // Fallback extractor that ignores color mask and scans UsedRange as-is
        private static ParseResult ExtractFromExcelNoColor(string path, string sheetName, out int width, out int height)
        {
            var pr = new ParseResult();
            using (var wb = new XLWorkbook(path))
            {
                var ws = (!string.IsNullOrWhiteSpace(sheetName) ? wb.Worksheet(sheetName) : wb.Worksheets.First());
                var used = ws.RangeUsed();
                if (used == null)
                {
                    width = height = 0; return pr;
                }
                int r1 = used.FirstRow().RowNumber();
                int c1 = used.FirstColumn().ColumnNumber();
                int r2 = used.LastRow().RowNumber();
                int c2 = used.LastColumn().ColumnNumber();
                width = (c2 - c1 + 1);
                height = (r2 - r1 + 1);

                var edgeBest = new Dictionary<(int, int, int, int), (int weight, string style)>();

                for (int r = r1; r <= r2; r++)
                {
                    for (int c = c1; c <= c2; c++)
                    {
                        var cell = ws.Cell(r, c);
                        var b = cell.Style.Border;

                        int cx = c - c1;
                        int cy = r - r1;

                        void AddEdgeIf(XLBorderStyleValues style, int x1, int y1, int x2, int y2)
                        {
                            if (style == XLBorderStyleValues.None) return;
                            var key = (x1 <= x2 || (x1 == x2 && y1 <= y2)) ? (x1, y1, x2, y2) : (x2, y2, x1, y1);
                            int w = StyleWeight(style);
                            string sk = StyleKey(style);
                            if (edgeBest.TryGetValue(key, out var cur))
                            {
                                if (w > cur.weight) edgeBest[key] = (w, sk);
                            }
                            else
                            {
                                edgeBest[key] = (w, sk);
                            }
                        }

                        AddEdgeIf(b.TopBorder, cx, cy, cx + 1, cy);
                        AddEdgeIf(b.BottomBorder, cx, cy + 1, cx + 1, cy + 1);
                        AddEdgeIf(b.LeftBorder, cx, cy, cx, cy + 1);
                        AddEdgeIf(b.RightBorder, cx + 1, cy, cx + 1, cy + 1);

                        var txt = cell.GetFormattedString();
                        if (!string.IsNullOrWhiteSpace(txt))
                        {
                            pr.Labels.Add(new LabelCell { Text = txt.Trim(), CellX = cx, CellY = cy });
                        }
                    }
                }

                foreach (var kv in edgeBest)
                {
                    var key = kv.Key;
                    var val = kv.Value;
                    pr.UnitEdges.Add(new UnitEdge
                    {
                        X1 = key.Item1,
                        Y1 = key.Item2,
                        X2 = key.Item3,
                        Y2 = key.Item4,
                        StyleKey = val.style
                    });
                }
            }

            return pr;
        }

        // -------------- 連結（同一スタイルで走査） --------------

        private static List<(int X1, int Y1, int X2, int Y2, string StyleKey)> CollapseToMaxSegmentsWithStyle(List<UnitEdge> unitEdges)
        {
            // 水平: y固定、(x,y)-(x+1,y) に対し style を持つ
            var horiz = new Dictionary<int, Dictionary<int, string>>(); // y -> (x -> style)
            var vert = new Dictionary<int, Dictionary<int, string>>(); // x -> (y -> style)

            foreach (var e in unitEdges)
            {
                if (e.IsHoriz)
                {
                    int y = e.Y1;
                    int x = Math.Min(e.X1, e.X2);
                    if (!horiz.TryGetValue(y, out var map)) { map = new Dictionary<int, string>(); horiz[y] = map; }
                    map[x] = e.StyleKey ?? "None";
                }
                else if (e.IsVert)
                {
                    int x = e.X1;
                    int y = Math.Min(e.Y1, e.Y2);
                    if (!vert.TryGetValue(x, out var map)) { map = new Dictionary<int, string>(); vert[x] = map; }
                    map[y] = e.StyleKey ?? "None";
                }
            }

            var segs = new List<(int, int, int, int, string)>();

            foreach (var kv in horiz)
            {
                int y = kv.Key;
                var xs = kv.Value.Keys.OrderBy(v => v).ToList();
                if (xs.Count == 0) continue;
                int start = xs[0], prev = start;
                string curStyle = "Mixed";

                for (int i = 1; i < xs.Count; i++)
                {
                    int x = xs[i];
                    string st = kv.Value[x];
                    bool contiguous = (x == prev + 1);
                    if (contiguous)
                    {
                        prev = x;
                    }
                    else
                    {
                        segs.Add((start, y, prev + 1, y, curStyle));
                        start = prev = x;
                        curStyle = "Mixed";
                    }
                }
                segs.Add((start, y, prev + 1, y, curStyle));
            }

            foreach (var kv in vert)
            {
                int x = kv.Key;
                var ys = kv.Value.Keys.OrderBy(v => v).ToList();
                if (ys.Count == 0) continue;
                int start = ys[0], prev = start;
                string curStyle = "Mixed";

                for (int i = 1; i < ys.Count; i++)
                {
                    int y = ys[i];
                    string st = kv.Value[y];
                    bool contiguous = (y == prev + 1);
                    if (contiguous)
                    {
                        prev = y;
                    }
                    else
                    {
                        segs.Add((x, start, x, prev + 1, curStyle));
                        start = prev = y;
                        curStyle = "Mixed";
                    }
                }
                segs.Add((x, start, x, prev + 1, curStyle));
            }

            return segs;
        }

        // ---------------- Revit 生成 ----------------

        private dynamic CreateWalls(Document doc, List<SegStyled> segs, string wallTypeName, string levelName, double baseOffsetFt, bool flip, string topLevelName, double? heightMm)
        {
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
            if (level == null)
                return new { ok = false, msg = $"レベル '{levelName}' が見つかりません。" };

            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType)).Cast<WallType>()
                .FirstOrDefault(t => t.Name.Equals(wallTypeName, StringComparison.OrdinalIgnoreCase));
            if (wallType == null)
                return new { ok = false, msg = $"壁タイプ '{wallTypeName}' が見つかりません。" };

            // determine wall height (heightMm or level-to-level or fallback)
            Level topLevel = null;
            double heightFt;
            if (heightMm.HasValue && heightMm.Value > 0)
            {
                heightFt = Math.Max(0.001, (heightMm.Value / 1000.0) * FEET_PER_METER);
            }
            else if (!string.IsNullOrWhiteSpace(topLevelName))
            {
                topLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => l.Name.Equals(topLevelName, StringComparison.OrdinalIgnoreCase));
                if (topLevel == null)
                    return new { ok = false, msg = $"トップレベル '{topLevelName}' が見つかりません。" };
                heightFt = topLevel.Elevation - level.Elevation;
                if (heightFt <= 0)
                    return new { ok = false, msg = "トップレベルがベースレベル以下（同一含む）のため高さが無効です。" };
            }
            else
            {
                // fallback height 3000mm
                heightFt = 3.0 * FEET_PER_METER;
            }
            if (heightFt <= 0 || heightFt > 30000.0)
                return new { ok = false, msg = "高さが無効です。0より大きく、30000ft以下にしてください。" };
            int n = 0;
            double totalLen = 0.0;

            using (var tx = new Transaction(doc, "Excel Plan Import (Walls)"))
            {
                tx.Start();

                foreach (var s in segs)
                {
                    bool horiz = Math.Abs(s.Y1 - s.Y2) < 1e-9;
                    bool vert = Math.Abs(s.X1 - s.X2) < 1e-9;
                    if (!(horiz || vert)) continue;

                    var p1 = new XYZ(s.X1 * FEET_PER_METER, s.Y1 * FEET_PER_METER, 0);
                    var p2 = new XYZ(s.X2 * FEET_PER_METER, s.Y2 * FEET_PER_METER, 0);
                    if (p1.DistanceTo(p2) < 1e-6) continue;

                    var line = Line.CreateBound(p1, p2);
                    var wall = Wall.Create(doc, line, wallType.Id, level.Id, /*height*/ heightFt, /*offset*/ baseOffsetFt, /*flip*/ flip, /*structural*/ false);
                    if (wall != null)
                    {
                        n++;
                        totalLen += p1.DistanceTo(p2) / FEET_PER_METER;
                    }
                }

                tx.Commit();
            }

            return new { ok = true, created = n, totalLengthMeters = totalLen };
        }

        private dynamic CreateModelLines(UIApplication uiapp, Document doc, List<SegStyled> segs)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var view = uidoc?.ActiveView;
            if (view == null || !(view is ViewPlan || view is View3D || view is ViewDrafting))
                return new { ok = false, msg = "モデル線は平面/3D/製図ビューで実行してください。" };

            int n = 0;
            double totalLen = 0.0;

            using (var tx = new Transaction(doc, "Excel Plan Import (ModelLines)"))
            {
                tx.Start();

                double originZ = 0.0;
                if (view is ViewPlan vp && vp.GenLevel != null) originZ = vp.GenLevel.Elevation;

                var sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, originZ)));

                foreach (var s in segs)
                {
                    bool horiz = Math.Abs(s.Y1 - s.Y2) < 1e-9;
                    bool vert = Math.Abs(s.X1 - s.X2) < 1e-9;
                    if (!(horiz || vert)) continue;

                    var p1 = new XYZ(s.X1 * FEET_PER_METER, s.Y1 * FEET_PER_METER, originZ);
                    var p2 = new XYZ(s.X2 * FEET_PER_METER, s.Y2 * FEET_PER_METER, originZ);
                    if (p1.DistanceTo(p2) < 1e-6) continue;

                    var line = Line.CreateBound(p1, p2);
                    doc.Create.NewModelCurve(line, sp);

                    n++;
                    totalLen += p1.DistanceTo(p2) / FEET_PER_METER;
                }

                tx.Commit();
            }

            return new { ok = true, created = n, totalLengthMeters = totalLen };
        }
    }
}



