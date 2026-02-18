// ============================================================================
// File: MergeDwgsGuiCommand.cs  (GUIp: acmgd + acdbmgd)
// Purpose: GUIŕDWG𓝍At@CCɕtĎ荞
// Target : .NET 8, AutoCAD GUI
// Notes  : accoremgd/Core ͈؎QƂȂ
//          DuplicateRecordCloning  Replace gp
//          ValidateSymbolName ͗OŔi߂l voidj
//          V DoMergePublic ̗VOl`
// ============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application; //  GUI Application (acmgd)
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;   //  GUI Document (acmgd)
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace MergeDwgsPlugin
{
    public static class MergeDwgsGuiCommand
    {
        // ------------------------------
        // Public entry (new signature)
        // ------------------------------
        public static bool DoMergePublic(JsonObject p, Action<string>? progress, out string? message)
        {
            message = null;

            // Params
            string seed = p?["seed"]?.GetValue<string>() ?? string.Empty;
            string output = p?["output"]?.GetValue<string>() ?? string.Empty;

            var inputsArr = p?["inputs"] as JsonArray ?? new JsonArray();
            var inputPaths = inputsArr
                .Select(n => n?["path"]?.GetValue<string>() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            // rename.{format, include[], exclude[]}
            string fmt = "{old}_{stem}";
            var rename = p?["rename"] as JsonObject;
            if (rename != null)
                fmt = rename?["format"]?.GetValue<string>() ?? fmt;

            var includes = GetStringList(rename, "include", defaultIfEmptyStar: true);
            var excludes = new HashSet<string>(
                GetStringList(rename, "exclude", defaultIfEmptyStar: false)
                    .DefaultIfEmpty().Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            // 펞O
            excludes.Add("0");
            excludes.Add("DEFPOINTS");

            try
            {
                progress?.Invoke("Preparing target drawing...");
                using var targetDocScope = EnsureTargetDrawing(seed, progress, out AcDoc targetDoc);
                var ed = targetDoc.Editor;
                var tdb = targetDoc.Database;

                // Merge each input
                int count = 0;
                foreach (var inPath in inputPaths)
                {
                    count++;
                    progress?.Invoke($"[{count}/{inputPaths.Length}] Reading: {inPath}");
                    if (!File.Exists(inPath))
                        throw new FileNotFoundException("Input DWG not found.", inPath);

                    var stem = Path.GetFileNameWithoutExtension(inPath);

                    using (var srcDb = new Database(false, true))
                    {
                        srcDb.ReadDwgFile(inPath, FileShare.Read, true, "");
                        srcDb.CloseInput(true);

                        // \[XDBŃC  N[ɂ͖ړĨCœĂ
                        PreRenameLayersInSource(srcDb, fmt, stem, includes, excludes, progress);

                        // fԑŜ^[QbgփN[
                        CloneModelSpaceToTarget(srcDb, tdb, progress);
                    }
                }

                progress?.Invoke("Purging unused items...");
                PurgeUnused(tdb);

                // Save
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var outDir = Path.GetDirectoryName(output);
                    if (!string.IsNullOrWhiteSpace(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    progress?.Invoke($"Saving to: {output}");
                    tdb.SaveAs(output, DwgVersion.Current);
                }

                progress?.Invoke("Done.");
                return true;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        // ------------------------------
        // Public entry (legacy signature)
        // ------------------------------
        public static bool DoMergePublic(
            object rawParams,
            string[] inputs,
            string seed,
            string output,
            IEnumerable<string> include,
            HashSet<string> exclude)
        {
            //   VJson ֋lߑւ
            var p = new JsonObject
            {
                ["seed"] = seed ?? string.Empty,
                ["output"] = output ?? string.Empty,
                ["inputs"] = new JsonArray()
            };

            var incArr = new JsonArray();
            foreach (var s in include ?? Array.Empty<string>()) incArr.Add(s);
            var excArr = new JsonArray();
            foreach (var s in (exclude ?? new HashSet<string>())) excArr.Add(s);

            p["rename"] = new JsonObject
            {
                ["format"] = "{old}_{stem}",
                ["include"] = incArr,
                ["exclude"] = excArr
            };

            var inputsJa = (JsonArray)p["inputs"]!;
            foreach (var s in inputs ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(s))
                    inputsJa.Add(new JsonObject { ["path"] = s });
            }

            return DoMergePublic(p, progress: null, out _);
        }

        // =====================================================================
        // Internals
        // =====================================================================

        /// <summary>^[Qbg}ʂpӁiseed ΊJAΌp/VKjB</summary>
        private static TargetDocScope EnsureTargetDrawing(string seed, Action<string>? progress, out AcDoc targetDoc)
        {
            var dm = AcAp.DocumentManager;

            if (!string.IsNullOrWhiteSpace(seed) && File.Exists(seed))
            {
                progress?.Invoke($"Opening seed drawing: {seed}");

                // ɊJĂ΂g
                var opened = dm.Cast<AcDoc>().FirstOrDefault(d =>
                    string.Equals(d.Name, seed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Database.Filename, seed, StringComparison.OrdinalIgnoreCase));

                if (opened is null)
                {
                    // VKɊJ
                    var doc = dm.Open(seed, forReadOnly: false);
                    dm.MdiActiveDocument = doc;
                    targetDoc = doc;
                    return new TargetDocScope(docActivated: true);
                }
                else
                {
                    dm.MdiActiveDocument = opened;
                    targetDoc = opened;
                    return new TargetDocScope(docActivated: false);
                }
            }
            else
            {
                // MDI΂gAΐVKev[g
                var cur = dm.MdiActiveDocument;
                if (cur is null)
                {
                    progress?.Invoke("Creating a new drawing...");
                    var doc = dm.Add(""); // ev[g
                    dm.MdiActiveDocument = doc;
                    targetDoc = doc;
                    return new TargetDocScope(docActivated: true);
                }
                targetDoc = cur;
                return new TargetDocScope(docActivated: false);
            }
        }

        private readonly struct TargetDocScope : IDisposable
        {
            private readonly bool _docActivated;
            public TargetDocScope(bool docActivated) => _docActivated = docActivated;
            public void Dispose()
            {
                // KvȂ猳MDIɖ߂̌Еt
            }
        }

        /// <summary>
        /// \[XDB̃C {old}/{stem} Ńl[ĂN[B
        /// </summary>
        private static void PreRenameLayersInSource(
            Database srcDb,
            string format,
            string stem,
            IReadOnlyList<string> includes,
            HashSet<string> excludes,
            Action<string>? progress)
        {
            using var tr = srcDb.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(srcDb.LayerTableId, OpenMode.ForRead);

            // ChJ[h  Regex
            var incMatchers = includes?.Select(WildcardToRegex).ToArray() ?? Array.Empty<Regex>();
            var excMatchers = excludes?.Select(WildcardToRegex).ToArray() ?? Array.Empty<Regex>();

            foreach (ObjectId lid in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                var oldName = ltr.Name;

                // O
                if (string.Equals(oldName, "0", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(oldName, "DEFPOINTS", StringComparison.OrdinalIgnoreCase)) continue;
                if (MatchesAny(oldName, excMatchers)) continue;

                // include `FbNi["*"] Ȃ펞truej
                if (incMatchers.Length > 0 && !MatchesAny(oldName, incMatchers))
                    continue;

                var newName = format
                    .Replace("{old}", oldName, StringComparison.Ordinal)
                    .Replace("{stem}", stem, StringComparison.Ordinal);

                if (string.Equals(newName, oldName, StringComparison.Ordinal))
                    continue; // ύXȂ

                // LC؁iValidateSymbolName  voidFOŔj
                var repaired = SymbolUtilityServices.RepairSymbolName(newName, false);
                try
                {
                    SymbolUtilityServices.ValidateSymbolName(repaired, false); //  Oo catch
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    throw new Autodesk.AutoCAD.Runtime.Exception(
                        ErrorStatus.InvalidInput,
                        $"Invalid layer name after rename: '{newName}' ({ex.Message})");
                }

                // Rename {
                ltr.UpgradeOpen();
                ltr.Name = repaired;
            }

            tr.Commit();
            progress?.Invoke($"Renamed layers in source ({stem}).");
        }

        /// <summary>\[XModelSpaceSGeBeB^[QbgփN[B</summary>
        private static void CloneModelSpaceToTarget(Database srcDb, Database targetDb, Action<string>? progress)
        {
            // src: ModelSpace ̑SIDW
            using var trSrc = srcDb.TransactionManager.StartTransaction();
            var btSrc = (BlockTable)trSrc.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
            var msId = btSrc[BlockTableRecord.ModelSpace];
            var ms = (BlockTableRecord)trSrc.GetObject(msId, OpenMode.ForRead);

            var ids = new ObjectIdCollection();
            foreach (ObjectId id in ms) ids.Add(id);
            trSrc.Commit();

            // tgt: ModelSpaceփN[
            using var trTgt = targetDb.TransactionManager.StartTransaction();
            var btTgt = (BlockTable)trTgt.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
            var msTgt = (BlockTableRecord)trTgt.GetObject(btTgt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var map = new IdMapping();
            srcDb.WblockCloneObjects(
                ids,
                msTgt.ObjectId,
                map,
                DuplicateRecordCloning.Replace, //  Merge ݂͑Ȃ
                deferTranslation: false);

            trTgt.Commit();

            progress?.Invoke("Cloned ModelSpace entities.");
        }

        /// <summary>gpV{p[WB</summary>
        private static void PurgeUnused(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var toPurge = new ObjectIdCollection();

            void Collect<T>(ObjectId tableId) where T : SymbolTable
            {
                var table = (T)tr.GetObject(tableId, OpenMode.ForRead);
                foreach (ObjectId id in table) toPurge.Add(id);
            }

            Collect<LayerTable>(db.LayerTableId);
            Collect<LinetypeTable>(db.LinetypeTableId);
            Collect<TextStyleTable>(db.TextStyleTableId);
            Collect<DimStyleTable>(db.DimStyleTableId);
            Collect<BlockTable>(db.BlockTableId);

            tr.Commit();

            db.Purge(toPurge);
            // KvȂ炱ōXɌʂ̕svBlock폜Aǉ
        }

        // ---------- Helpers ----------

        private static bool MatchesAny(string s, Regex[] matchers)
        {
            for (int i = 0; i < matchers.Length; i++)
                if (matchers[i].IsMatch(s)) return true;
            return false;
        }

        private static Regex WildcardToRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";
            var esc = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
            return new Regex("^" + esc + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static List<string> GetStringList(JsonObject? obj, string prop, bool defaultIfEmptyStar)
        {
            var list = new List<string>();
            if (obj != null && obj[prop] is JsonArray arr)
            {
                foreach (var n in arr)
                {
                    var s = n?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            if (defaultIfEmptyStar && list.Count == 0)
                list.Add("*");
            return list;
        }
    }
}
