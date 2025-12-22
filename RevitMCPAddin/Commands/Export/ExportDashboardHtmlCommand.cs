#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Export
{
    /// <summary>
    /// export_dashboard_html: 現在のプロジェクトの概要・レベル・部屋集計・カテゴリ集計を HTML に書き出す
    /// params: { outDir?: string }
    /// result: { ok: true, path: string }
    /// </summary>
    public class ExportDashboardHtmlCommand : IRevitCommandHandler
    {
        public string CommandName => "export_dashboard_html";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            string outDir = ResolveOutDir(cmd) ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitMCP_Dashboard");
            try { System.IO.Directory.CreateDirectory(outDir); }
            catch (Exception ex) { return new { ok = false, msg = "Create outDir failed: " + ex.Message }; }

            // Project summary
            var app = uiapp.Application;
            string projectName = doc.Title ?? string.Empty;
            string revitVersion = $"{app.VersionName} ({app.VersionNumber})";
            string docPath = doc.PathName ?? string.Empty;
            bool isCloud = SafeBool(() => doc.IsModelInCloud);
            string unitSystem = SafeStr(() => doc.DisplayUnitSystem.ToString());
            int levelCount = new FilteredElementCollector(doc).OfClass(typeof(Level)).Count();
            int views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate);
            int categories = SafeInt(() => doc.Settings.Categories.Size);
            var phases = SafeList(() => doc.Phases?.Cast<Phase>().Select(p => p.Name));
            bool isWorkshared = SafeBool(() => doc.IsWorkshared);
            int worksets = SafeInt(() => new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).Count());
            int warnings = SafeInt(() => doc.GetWarnings()?.Count ?? 0);

            // Levels simple
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => new
                {
                    id = l.Id.IntValue(),
                    name = l.Name,
                    elevation = Math.Round(UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters), 6)
                })
                .OrderBy(x => x.elevation)
                .ToList();

            // Rooms by level
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r != null && r.Area > 1e-6)
                .ToList();

            var roomsByLevel = new Dictionary<string, (int count, double areaM2)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rooms)
            {
                string ln = r.Level != null ? r.Level.Name : "(No level)";
                double a = UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters);

                if (!roomsByLevel.TryGetValue(ln, out var cur))
                {
                    cur = (count: 0, areaM2: 0.0);          // ★ 名前付きで初期化
                }
                cur.count += 1;
                cur.areaM2 += a;
                roomsByLevel[ln] = cur;
            }

            var roomsItems = roomsByLevel
                .Select(kv => new
                {
                    levelName = kv.Key,
                    rooms = kv.Value.count,
                    totalAreaM2 = Math.Round(kv.Value.areaM2, 2)
                })
                .OrderBy(x => x.levelName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Elements by category + used types
            var dictElem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var dictType = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                var c = e.Category; if (c == null) continue;
                var name = c.Name ?? "(No category)";

                dictElem[name] = dictElem.TryGetValue(name, out var cur) ? (cur + 1) : 1;

                var tid = e.GetTypeId();
                if (tid != ElementId.InvalidElementId)
                {
                    if (!dictType.TryGetValue(name, out var set))
                    {
                        set = new HashSet<ElementId>();
                        dictType[name] = set;
                    }
                    set.Add(tid);
                }
            }

            var catsItems = dictElem
                .Select(kv => new
                {
                    categoryName = kv.Key,
                    count = kv.Value,
                    typeCount = dictType.TryGetValue(kv.Key, out var set) ? set.Count : 0
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // HTML render
            var html = BuildHtml(
                projectName, revitVersion, docPath, isCloud, unitSystem,
                levelCount, views, categories, phases, isWorkshared, worksets, warnings,
                levels.Cast<object>(),            // ★ List<匿名型> → IEnumerable<object>
                roomsItems.Cast<object>(),        // ★ 同上
                catsItems.Cast<object>()          // ★ 同上
            );

            var outPath = System.IO.Path.Combine(outDir, "index.html");
            System.IO.File.WriteAllText(outPath, html, Encoding.UTF8);
            return new { ok = true, path = outPath };
        }

        private static string? ResolveOutDir(RequestCommand cmd)
        {
            try
            {
                var p = cmd.Params as JObject;
                var s = p?.Value<string>("outDir");          // ★ ここを素直に
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { }
            return null;
        }

        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
        private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
        private static string SafeStr(Func<string?> f) { try { return f() ?? string.Empty; } catch { return string.Empty; } }
        private static List<string> SafeList(Func<IEnumerable<string>?> f)
        {
            try { return f()?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static string BuildHtml(
            string projectName, string revitVersion, string docPath, bool isCloud, string unitSystem,
            int levelCount, int views, int categories, List<string> phases, bool isWorkshared, int worksets, int warnings,
            IEnumerable<object> levels, IEnumerable<object> rooms, IEnumerable<object> cats)  // ★ 受け口を IEnumerable<object> に
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Revit MCP Dashboard (Export)</title><style>");
            sb.Append("body{font-family:system-ui,sans-serif;margin:24px;} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px;} .card{border:1px solid #ddd;border-radius:8px;padding:12px;background:#fafafa;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid #ddd;padding:6px 8px;text-align:left;} .right{text-align:right}");
            sb.Append("</style></head><body><h1>Revit MCP Dashboard (Export)</h1>");
            sb.Append("<section class=\"grid\">");

            // Project
            sb.Append("<div class=\"card\"><h2>Project</h2>");
            sb.Append("<div><strong>").Append(E(projectName)).Append("</strong></div>");
            sb.Append("<div><small>").Append(E(revitVersion)).Append("</small></div>");
            if (!string.IsNullOrWhiteSpace(docPath)) sb.Append("<div><small>").Append(E(docPath)).Append("</small></div>");
            sb.Append("<ul>");
            sb.Append("<li>Levels: ").Append(levelCount).Append("; Views: ").Append(views).Append("; Categories: ").Append(categories).Append("</li>");
            sb.Append("<li>Worksets: ").Append(worksets).Append("; Warnings: ").Append(warnings).Append("</li>");
            if (phases.Count > 0) sb.Append("<li>Phases: ").Append(E(string.Join(", ", phases))).Append("</li>");
            sb.Append("</ul></div>");

            // Levels
            sb.Append("<div class=\"card\"><h2>Levels</h2><table><thead><tr><th>Name</th><th class=\"right\">Elevation (m)</th><th class=\"right\">ID</th></tr></thead><tbody>");
            foreach (dynamic l in levels)
            {
                sb.Append("<tr><td>").Append(E((string)l.name)).Append("</td><td class=\"right\">")
                  .Append(E(string.Format("{0:0.###}", (double)l.elevation)))
                  .Append("</td><td class=\"right\">").Append(E(((int)l.id).ToString())).Append("</td></tr>");
            }
            sb.Append("</tbody></table></div>");

            // Rooms
            sb.Append("<div class=\"card\"><h2>Rooms by Level</h2><table><thead><tr><th>Level</th><th class=\"right\">Rooms</th><th class=\"right\">Total Area (m2)</th></tr></thead><tbody>");
            foreach (dynamic r in rooms)
            {
                sb.Append("<tr><td>").Append(E((string)r.levelName)).Append("</td><td class=\"right\">")
                  .Append(((int)r.rooms).ToString())
                  .Append("</td><td class=\"right\">")
                  .Append(E(string.Format("{0:0.##}", (double)r.totalAreaM2)))
                  .Append("</td></tr>");
            }
            sb.Append("</tbody></table></div>");

            // Categories
            sb.Append("<div class=\"card\"><h2>Elements by Category</h2><table><thead><tr><th>Category</th><th class=\"right\">Elements</th><th class=\"right\">Used Types</th></tr></thead><tbody>");
            foreach (dynamic c in cats)
            {
                sb.Append("<tr><td>").Append(E((string)c.categoryName)).Append("</td><td class=\"right\">")
                  .Append(((int)c.count).ToString())
                  .Append("</td><td class=\"right\">")
                  .Append(((int)c.typeCount).ToString())
                  .Append("</td></tr>");
            }
            sb.Append("</tbody></table></div>");

            sb.Append("</section></body></html>");
            return sb.ToString();
        }

        private static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
    }
}

