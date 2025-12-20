// ================================================================
// File: Commands/DxfOps/GenerateDwgMergeScriptCommand.cs
// Purpose: Generate AutoCAD Core Console .scr to consolidate DWGs
// Notes  : Script only (no execution). Execution can be delegated to
//          AutoCadMCP or external runner.
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitMCPAddin.Commands.DxfOps
{
    public class GenerateDwgMergeScriptCommand : IRevitCommandHandler
    {
        public string CommandName => "generate_dwg_merge_script|gen_dwg_script";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) ?? new JObject();

                string inputDir = RequireDir(p.Value<string>("inputDir"), nameof(inputDir));
                string pattern = p.Value<string>("pattern") ?? "walls_*.dwg";
                string bindType = (p.Value<string>("bindType") ?? "Bind").Trim(); // Bind | Insert
                string refPathType = (p.Value<string>("refPathType") ?? "2").Trim(); // "0"|"1"|"2"
                string outputDwg = p.Value<string>("outputDwg") ?? "C:/temp/CadOut/merged.dwg";
                string saveAsVersion = (p.Value<string>("saveAsVersion") ?? "2018").Trim();
                string trustedPaths = p.Value<string>("trustedPaths") ?? "C:/Temp;C:/Temp/CadOut";
                string mergeMode = (p.Value<string>("mergeMode") ?? "None").Trim(); // None | ByFile | Map
                string? layerMapCsv = p.Value<string>("layerMapCsv");
                int purgeTimes = Math.Max(0, p.Value<int?>("purgeTimes") ?? 2);
                bool audit = p.Value<bool?>("audit") ?? false;
                string outScript = p.Value<string>("outScript") ?? Path.Combine(inputDir, "run_merge.scr");

                // Resolve files
                if (!Directory.Exists(inputDir))
                    return new { ok = false, error = "INPUT_DIR_NOT_FOUND", msg = $"InputDir not found: {inputDir}" };

                var dwgs = Directory.GetFiles(inputDir, pattern).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                if (dwgs.Length == 0)
                    return new { ok = false, error = "NO_DWGS", msg = $"No DWG matched pattern '{pattern}' under '{inputDir}'." };

                // Build script
                var sb = new StringBuilder(4096);
                void W(string s) => sb.AppendLine(s);

                // Header
                W("._SETVAR CMDECHO 0");
                W("._SETVAR FILEDIA 0");
                W("._SETVAR CMDDIA 0");
                W("._SETVAR ATTDIA 0");
                W("._SETVAR EXPERT 5");
                W("._SETVAR QAFLAGS 2");
                W("._SETVAR NOMUTT 1");
                W($"._SETVAR REFPATHTYPE {NormalizeRefPathType(refPathType)}");
                // ★ 修正: 比較式の \" を削除（C# の式として評価させる）
                W($"._SETVAR BINDTYPE {(NormalizeBindType(bindType) == "Insert" ? 1 : 0)}");
                if (!string.IsNullOrWhiteSpace(trustedPaths))
                {
                    W($"._SETVAR TRUSTEDPATHS \"{ToScrPath(trustedPaths)}\"");
                }

                // XREF attach all
                foreach (var f in dwgs)
                {
                    W("._-XREF");
                    W("_ATTACH");
                    W($"\"{ToScrPath(f)}\"");
                    W("0,0,0");
                    W("1");
                    W("1");
                    W("0");
                }

                // Reload and Bind
                W("._-XREF");
                W("_RELOAD");
                W("*");
                W("._-XREF");
                W("_BIND");
                W("*");

                // Merge Modes
                switch (NormalizeMergeMode(mergeMode))
                {
                    case "ByFile":
                        EmitMergeByFile(sb, dwgs);
                        break;
                    case "Map":
                        EmitMergeByMap(sb, layerMapCsv);
                        break;
                    default:
                        break;
                }

                // PURGE/AUDIT
                for (int i = 0; i < purgeTimes; i++)
                {
                    W("._-PURGE");
                    W("_A");
                    W("*");
                    W("_N");
                }
                if (audit)
                {
                    W("._AUDIT");
                    W("_Y");
                }

                // SAVEAS (multi-line, no overwrite confirmation Y)
                W("._SAVEAS");
                W(saveAsVersion);
                W($"\"{ToScrPath(outputDwg)}\"");
                W("_QUIT");

                // Write .scr (ANSI/Default, CRLF)
                var outDir = Path.GetDirectoryName(outScript);
                if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);
                File.WriteAllText(outScript, sb.ToString(), Encoding.Default);

                return new
                {
                    ok = true,
                    scriptPath = outScript,
                    matched = dwgs.Length,
                    pattern,
                    bindType = NormalizeBindType(bindType),
                    refPathType = NormalizeRefPathType(refPathType),
                    mergeMode = NormalizeMergeMode(mergeMode),
                    outputDwg,
                    saveAsVersion
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"GenerateDwgMergeScriptCommand failed: {ex.Message}");
                return new { ok = false, error = ex.GetType().Name, msg = ex.Message };
            }

            // フォールバック（到達しないが「値を返さないコードパス」警告を抑止）
            // ReSharper disable once HeuristicUnreachableCode
            // あるいは #pragma warning disable CS0161 を使う手もあります
            return new { ok = false, error = "UNREACHABLE", msg = "Unexpected flow." };
        }

        private static string RequireDir(string? dir, string name)
        {
            if (string.IsNullOrWhiteSpace(dir)) throw new ArgumentException($"Missing parameter: {name}");
            return dir;
        }

        private static string NormalizeBindType(string bt)
            => string.Equals(bt, "Insert", StringComparison.OrdinalIgnoreCase) ? "Insert" : "Bind";

        private static string NormalizeRefPathType(string r)
            => (r == "0" || r == "1" || r == "2") ? r : "2";

        private static string NormalizeMergeMode(string m)
        {
            if (string.Equals(m, "ByFile", StringComparison.OrdinalIgnoreCase)) return "ByFile";
            if (string.Equals(m, "Map", StringComparison.OrdinalIgnoreCase)) return "Map";
            return "None";
        }

        private static string ToScrPath(string p)
            => (p ?? string.Empty).Replace("\\", "/");

        private static void EmitMergeByFile(StringBuilder sb, IEnumerable<string> dwgs)
        {
            void W(string s) => sb.AppendLine(s);
            W("(vl-load-com)");
            W("(defun _ensure-layer (name /)(if (not (tblsearch \"LAYER\" name))(command \"-layer\" \"new\" name \"\"))(command \"-layer\" \"thaw\" name \"unlock\" name \"\") name)");
            W("(defun merge-xref-layers-to (xrefBase target / rec lname pat tgt)");
            W("  (setq tgt (_ensure-layer target))");
            W("  (setq pat (strcat xrefBase \"$0$*\"))");
            W("  (setq rec (tblnext \"LAYER\" T))");
            W("  (while rec");
            W("    (setq lname (cdr (assoc 2 rec)))");
            W("    (if (wcmatch lname pat) (command \"-layer\" \"merge\" lname tgt \"\"))");
            W("    (setq rec (tblnext \"LAYER\"))");
            W("  )");
            W("  (princ))");
            foreach (var f in dwgs)
            {
                var baseName = Path.GetFileNameWithoutExtension(f);
                if (string.IsNullOrWhiteSpace(baseName)) continue;
                W($"(merge-xref-layers-to \"{baseName}\" \"{baseName}\")");
            }
        }

        private static void EmitMergeByMap(StringBuilder sb, string? mapCsvPath)
        {
            void W(string s) => sb.AppendLine(s);
            if (string.IsNullOrWhiteSpace(mapCsvPath) || !File.Exists(mapCsvPath))
            {
                // Nothing to merge; emit a comment form (princ)
                W("(princ \"; Merge map not found\")");
                return;
            }
            var rows = new List<(string pat, string tgt)>();
            foreach (var line in File.ReadAllLines(mapCsvPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#") || line.StartsWith("pattern", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var pat = parts[0].Trim();
                var tgt = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(tgt))
                    rows.Add((pat, tgt));
            }
            if (rows.Count == 0) { W("(princ \"; Merge map empty\")"); return; }

            W("(vl-load-com)");
            W("(defun _ensure-layer (name /)(if (not (tblsearch \"LAYER\" name))(command \"-layer\" \"new\" name \"\"))(command \"-layer\" \"thaw\" name \"unlock\" name \"\") name)");
            W("(defun merge-pattern-to (pat target / rec lname tgt)");
            W("  (setq tgt (_ensure-layer target))");
            W("  (setq rec (tblnext \"LAYER\" T))");
            W("  (while rec");
            W("    (setq lname (cdr (assoc 2 rec)))");
            W("    (if (wcmatch lname pat) (command \"-layer\" \"merge\" lname tgt \"\"))");
            W("    (setq rec (tblnext \"LAYER\"))");
            W("  ) (princ))");
            foreach (var pair in rows)
            {
                var pat = pair.pat;
                var tgt = pair.tgt;
                W($"(merge-pattern-to \"{pat}\" \"{tgt}\")");
            }
        }
    }
}
