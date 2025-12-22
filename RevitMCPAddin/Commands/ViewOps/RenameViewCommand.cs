// ================================================================
// File   : Commands/ViewOps/RenameViewCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: Safely rename a view by setting View.Name (NOT a Parameter)
// Notes  : - View Template は名前変更不可 (IsTemplate=true は拒否)
//          - 同名衝突は ifExists で制御 ("error"|"suffix"|"replace")
//          - "viewId"(int/string) / "viewUniqueId"(string) のどちらでも指定可
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public sealed class RenameViewCommand : IRevitCommandHandler
    {
        // ルーターが参照するコマンド名
        public string CommandName => "rename_view";

        // ルーターの期待シグネチャ: 戻り値は object
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return Err("NO_ACTIVE_DOC", "No active document.");

            var p = cmd.Params as JObject ?? new JObject();

            // ---- params ----
            int? viewId = ReadInt(p, "viewId");
            string? viewUid = ReadString(p, "viewUniqueId");
            string? newNameRaw = ReadString(p, "newName");
            if (string.IsNullOrWhiteSpace(newNameRaw))
                return Err("INVALID_PARAM", "newName is required and must be non-empty.");

            string ifExists = (ReadString(p, "ifExists") ?? "error").Trim().ToLowerInvariant();

            // ---- resolve target view ----
            View? v = null;
            if (viewId.HasValue)
                v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            else if (!string.IsNullOrEmpty(viewUid))
                v = doc.GetElement(viewUid) as View;

            if (v == null) return Err("NOT_FOUND", "View not found.");
            if (v.IsTemplate) return Err("VIEW_TEMPLATE", "Target view is a View Template; renaming is not allowed.");

            // ---- sanitize new name ----
            string newName = SanitizeName(newNameRaw!);

            // ---- duplicate handling ----
            bool NameExists(string name) =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(x => !x.IsTemplate &&
                              string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                              x.Id.IntValue() != v.Id.IntValue());

            string EnsureUnique(string baseName)
            {
                if (!NameExists(baseName)) return baseName;
                int n = 2;
                while (n < 1000)
                {
                    var candidate = $"{baseName} ({n})";
                    if (!NameExists(candidate)) return candidate;
                    n++;
                }
                throw new InvalidOperationException("Failed to generate unique name.");
            }

            string? replacedOldName = null;

            if (NameExists(newName))
            {
                switch (ifExists)
                {
                    case "suffix":
                        newName = EnsureUnique(newName);
                        break;

                    case "replace":
                        // 既存の同名ビューを退避名へ
                        var conflict = new FilteredElementCollector(doc)
                            .OfClass(typeof(View))
                            .Cast<View>()
                            .First(x => !x.IsTemplate &&
                                        string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase));

                        replacedOldName = EnsureUnique(newName + " - old");
                        using (var t0 = new Transaction(doc, "Rename conflicting view"))
                        {
                            t0.Start();
                            conflict.Name = replacedOldName;
                            t0.Commit();
                        }
                        break;

                    case "error":
                    default:
                        return Err("DUPLICATE_NAME", $"A view named '{newName}' already exists.");
                }
            }

            // ---- rename ----
            var oldName = v.Name;
            using (var t = new Transaction(doc, "Rename View"))
            {
                t.Start();
                v.Name = newName; // ★ Parameter ではなく View.Name を直接書き換える
                t.Commit();
            }

            return new
            {
                ok = true,
                viewId = v.Id.IntValue(),
                oldName,
                newName,
                conflictResolvedBy = replacedOldName != null ? "replace"
                                   : (ifExists == "suffix" ? "suffix" : null),
                replacedOldName
            };
        }

        // ---------- helpers ----------

        private static object Err(string code, string msg)
            => new { ok = false, code, msg };

        private static int? ReadInt(JObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var tok) || tok == null || tok.Type == JTokenType.Null)
                return null;
            if (tok.Type == JTokenType.Integer) return (int)tok;
            if (tok.Type == JTokenType.Float) return (int)Math.Round((double)tok);
            if (tok.Type == JTokenType.String && int.TryParse((string)tok, out var v)) return v;
            return null;
        }

        private static string? ReadString(JObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var tok) || tok == null || tok.Type == JTokenType.Null)
                return null;
            return tok.Type == JTokenType.String ? (string)tok : tok.ToString();
        }

        private static string SanitizeName(string s)
        {
            s = s.Trim();
            // 制御文字を除去
            s = Regex.Replace(s, @"[\u0000-\u001F]", "");
            // Revitが嫌う可能性のある記号の多くを全角へ
            s = s.Replace('\\', '／').Replace('/', '／')
                 .Replace('\"', '”').Replace(':', '：')
                 .Replace('*', '＊').Replace('?', '？')
                 .Replace('<', '＜').Replace('>', '＞')
                 .Replace('|', '｜');
            // 連続空白を1つに
            s = Regex.Replace(s, @"\s{2,}", " ");
            return s;
        }
    }
}


