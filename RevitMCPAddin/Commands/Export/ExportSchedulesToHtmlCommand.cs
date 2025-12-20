#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Export
{
    /// <summary>
    /// export_schedules_html: 指定した集計表(ViewSchedule)をHTMLで出力します（params: { outDir?: string, names?: string[], ids?: int[], include?: string[], exclude?: string[], maxRows?: int }）。
    /// </summary>
    public class ExportSchedulesToHtmlCommand : IRevitCommandHandler
    {
        public string CommandName => "export_schedules_html";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = cmd.Params;
            string outDir = SafeStr(() => p.Value<string>("outDir"));
            if (string.IsNullOrWhiteSpace(outDir))
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitMCP_Schedules");
            try { Directory.CreateDirectory(outDir); } catch (Exception ex) { return new { ok = false, msg = "Create outDir failed: " + ex.Message }; }

            var nameList = SafeList(() => p?["names"]?.ToObject<string[]>());
            var include = SafeList(() => p?["include"]?.ToObject<string[]>());
            var exclude = SafeList(() => p?["exclude"]?.ToObject<string[]>());
            var idList = SafeList(() => p?["ids"]?.ToObject<int[]>());
            int maxRows = SafeInt(() =>
            {
                try { var v = p.Value<int?>("maxRows"); return v ?? 0; } catch { return 0; }
            });

            // Collect schedules
            var all = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate)
                .ToList();

            IEnumerable<ViewSchedule> targets = all;
            if (idList.Count > 0)
            {
                var idset = new HashSet<int>(idList);
                targets = targets.Where(vs => idset.Contains(vs.Id.IntegerValue));
            }
            else if (nameList.Count > 0)
            {
                var set = new HashSet<string>(nameList, StringComparer.OrdinalIgnoreCase);
                targets = targets.Where(vs => set.Contains(vs.Name));
            }
            else if (include.Count > 0 || exclude.Count > 0)
            {
                targets = targets.Where(vs => Matches(vs.Name, include, exclude));
            }

            var files = new List<object>();
            foreach (var vs in targets)
            {
                try
                {
                    var html = RenderScheduleHtml(vs, maxRows);
                    var fname = SanitizeFileName(vs.Name);
                    var path = Path.Combine(outDir, $"schedule_{fname}.html");
                    File.WriteAllText(path, html, Encoding.UTF8);
                    files.Add(new { id = vs.Id.IntegerValue, name = vs.Name, path });
                }
                catch (Exception ex)
                {
                    files.Add(new { id = vs.Id.IntegerValue, name = vs.Name, error = ex.Message });
                }
            }

            return new { ok = true, count = files.Count, outputs = files };
        }

        private static string RenderScheduleHtml(ViewSchedule vs, int maxRows)
        {
            var td = vs.GetTableData();
            var headerSec = SafeSection(td, SectionType.Header);
            var bodySec = SafeSection(td, SectionType.Body);

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>")
              .Append(E(vs.Name))
              .Append("</title><style>body{font-family:system-ui,sans-serif;margin:24px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid #ddd;padding:6px 8px;text-align:left;} .right{text-align:right}</style></head><body>");

            // Title (use schedule name; Title section is not reliably available)
            sb.Append("<h1>").Append(E(vs.Name)).Append("</h1>");

            // Header + Body
            sb.Append("<table>");
            // Header rows
            if (headerSec != null && headerSec.NumberOfRows > 0)
            {
                sb.Append("<thead>");
                for (int r = 0; r < headerSec.NumberOfRows; r++)
                {
                    sb.Append("<tr>");
                    for (int c = 0; c < headerSec.NumberOfColumns; c++)
                    {
                        var text = SafeText(vs, SectionType.Header, r, c);
                        sb.Append("<th>").Append(E(text)).Append("</th>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</thead>");
            }

            // Body rows
            sb.Append("<tbody>");
            if (bodySec != null)
            {
                int rows = bodySec.NumberOfRows;
                int cols = bodySec.NumberOfColumns;
                if (maxRows > 0) rows = Math.Min(rows, maxRows);
                for (int r = 0; r < rows; r++)
                {
                    sb.Append("<tr>");
                    for (int c = 0; c < cols; c++)
                    {
                        var text = SafeText(vs, SectionType.Body, r, c);
                        sb.Append("<td>").Append(E(text)).Append("</td>");
                    }
                    sb.Append("</tr>");
                }
            }
            sb.Append("</tbody></table>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static TableSectionData? SafeSection(TableData td, SectionType sec)
        {
            try { return td.GetSectionData(sec); } catch { return null; }
        }

        private static string SafeText(ViewSchedule vs, SectionType sec, int r, int c)
        {
            try { return vs.GetCellText(sec, r, c) ?? string.Empty; } catch { return string.Empty; }
        }

        private static bool Matches(string name, List<string> include, List<string> exclude)
        {
            bool ok = include.Count == 0 || include.Any(p => NameLike(name, p));
            if (!ok) return false;
            if (exclude.Count > 0 && exclude.Any(p => NameLike(name, p))) return false;
            return true;
        }

        private static bool NameLike(string name, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            // very simple wildcard: '*' -> contains
            if (pattern == "*") return true;
            if (pattern.IndexOf("*", StringComparison.Ordinal) >= 0)
            {
                var tok = pattern.Replace("*", string.Empty);
                return name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "schedule";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (Array.IndexOf(invalid, ch) >= 0) sb.Append('_');
                else sb.Append(ch);
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "schedule" : s;
        }
        private static string SafeStr(Func<string?> f) { try { return f() ?? string.Empty; } catch { return string.Empty; } }
        private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
        private static List<string> SafeList(Func<string[]?> f)
        {
            try { var a = f(); return a != null ? a.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }
        private static List<int> SafeList(Func<int[]?> f)
        {
            try { var a = f(); return a != null ? a.ToList() : new List<int>(); }
            catch { return new List<int>(); }
        }
    }
}
