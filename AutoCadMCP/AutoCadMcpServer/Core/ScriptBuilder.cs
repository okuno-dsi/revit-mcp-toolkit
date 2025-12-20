// ================================================================
// File: ScriptBuilder.cs
// Purpose: Build robust AutoCAD Core Console .scr scripts
// Notes  : Focused on "per-file layer rename" merge flow with safe prompts.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoCadMcpServer.Core
{
    public static class ScriptBuilder
    {
        /// <summary>
        /// Build a script that INSERT/EXPLODEs each input DWG, then renames layers
        /// that match include/exclude using AutoLISP (wcmatch). New name is:
        ///     new = prefix + old + suffix
        /// where "suffix" can include the source file stem (passed as argument).
        /// Finally, optional -OVERKILL / PURGE / AUDIT and SAVEAS to <saveAsPath>.
        /// </summary>
        public static string BuildPerFileRenameScript(
            IReadOnlyList<string> inputs,
            string includeWildcard,
            string? excludeWildcard,
            string prefix,
            string suffixFormat, // may contain "{stem}"
            string saveAsPath,
            bool doPurge,
            bool doAudit,
            int overkillPasses,
            string? layTransDwsPath // null to skip
        )
        {
            if (inputs == null || inputs.Count == 0) throw new ArgumentException("inputs empty");
            var sb = new StringBuilder();

            // --- prologue: system variables & quiet mode ---
            sb.AppendLine("._CMDECHO 0");
            sb.AppendLine("._FILEDIA 0");
            sb.AppendLine("._CMDDIA 0");
            sb.AppendLine("._ATTDIA 0");
            sb.AppendLine("._EXPERT 5");

            // --- helper LISP: rename layers by wildcard ---
            sb.AppendLine(BuildRelayerLisp());

            // --- per file insert / explode / rename ---
            foreach (var inPath in inputs)
            {
                var norm = inPath.Replace("\\", "/");
                var stem = Path.GetFileNameWithoutExtension(inPath);
                // INSERT (as block) then EXPLODE last created block reference (L)
                sb.AppendLine($".__-INSERT \"{norm}\" 0,0,0 1 1 0");
                sb.AppendLine("._EXPLODE L");
                // call relayer: (relayer <stem> <include> <excludeOrEmpty> <prefix> <suffix>)
                var suffix = suffixFormat.Replace("{stem}", stem);
                var include = string.IsNullOrWhiteSpace(includeWildcard) ? "*" : includeWildcard;
                var exclude = string.IsNullOrWhiteSpace(excludeWildcard) ? "" : excludeWildcard!;
                sb.AppendLine($"(relayer \"{EscapeLisp(stem)}\" \"{EscapeLisp(include)}\" \"{EscapeLisp(exclude)}\" \"{EscapeLisp(prefix)}\" \"{EscapeLisp(suffix)}\")");
                // Note: previously we tried to thaw/unlock/on all layers via -LAYER,
                // but that sequence is easy to mis-sync with prompts and can hang CoreConsole.
                // To keep the script fully non-interactive, we skip the extra -LAYER calls here.
            }

            // --- optional LAYTRANS (DWS mapping) ---
            if (!string.IsNullOrWhiteSpace(layTransDwsPath))
            {
                var dws = layTransDwsPath!.Replace("\\", "/");
                // minimal, robust sequence: load DWS, map all by standards, apply
                // Some environments require a couple of Enters to accept defaults.
                sb.AppendLine("._-LAYTRANS");
                sb.AppendLine("_?"); // list
                sb.AppendLine("");   // (continue)
                sb.AppendLine("_STANDARDS"); // choose standards (DWS) file
                sb.AppendLine($"\"{dws}\"");
                sb.AppendLine("");   // accept
                sb.AppendLine("_TRANSLATE"); // translate now
                sb.AppendLine("");   // all layers
                sb.AppendLine("");   // accept
                sb.AppendLine("_APPLY");
                sb.AppendLine("");   // accept
                sb.AppendLine("_EXIT");
            }

            // --- geometry cleanups ---
            for (int i = 0; i < Math.Max(0, overkillPasses); i++)
            {
                sb.AppendLine("._-OVERKILL _All _ _Y _Y _Y _Y _Y"); // tolerate prompts variance across versions
            }
            if (doPurge)
            {
                // run purge twice to dig deeper references
                sb.AppendLine("._-PURGE A * N");
                sb.AppendLine("._-PURGE R * N");
                sb.AppendLine("._-PURGE A * N");
            }
            if (doAudit)
            {
                // AUDIT does not need a dialog variant; use plain AUDIT
                sb.AppendLine("._AUDIT Y");
            }

            // --- SAVEAS & quit ---
            var saveDir = Path.GetDirectoryName(saveAsPath)!.Replace("\\", "/");
            var saveName = Path.GetFileName(saveAsPath);
            Directory.CreateDirectory(saveDir);
            // Use plain SAVEAS (no dash); CoreConsole 2026 reports "-SAVEAS" as unknown
            sb.AppendLine($".__SAVEAS 2018 \"{Path.Combine(saveDir, saveName).Replace("\\", "/")}\"");
            sb.AppendLine("._QUIT Y");
            sb.AppendLine("(princ)");

            return sb.ToString();
        }

        private static string EscapeLisp(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string BuildRelayerLisp()
        {
            // relayer: iterate layer table; if name matches include & not exclude, rename to prefix + old + suffix
            // Special-case: never rename layer "0" or "DEFPOINTS"
            return @"
(defun relayer (stem include exclude prefix suffix / tbl rec name newname up)
  (setq tbl (tblnext ""LAYER"" T))
  (while tbl
    (setq name (cdr (assoc 2 tbl)))
    (setq up (if name (strcase name) """" ))
    (if (and name
             (not (= up ""0""))
             (not (= up ""DEFPOINTS""))
             (or (= include """") (wcmatch name include))
             (or (= exclude """") (not (wcmatch name exclude))))
      (progn
        (setq newname (strcat prefix name suffix))
        (if (not (= name newname))
          (command ""_.-RENAME"" ""Layer"" name newname)
        )
      )
    )
    (setq tbl (tblnext ""LAYER""))
  )
  (princ)
)
";
        }
    }
}
