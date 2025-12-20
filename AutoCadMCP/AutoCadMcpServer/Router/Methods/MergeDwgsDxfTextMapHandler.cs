#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using AutoCadMcpServer.Router;
using AutoCadMcpServer.Core;

namespace AutoCadMcpServer.Router.Methods
{
    /// <summary>
    /// DXF テキスト編集経由でレイヤ名を「{old} + 付け足し（{stem} 等）」に改名し、
    /// 置換済み DXF 群を DXFIN で 1 枚の DWG に統合する。
    /// accoreconsole は SAVEAS DXF と DXFIN にのみ使用。レイヤ改名はテキスト操作で完結。
    /// </summary>
    public static class MergeDwgsDxfTextMapHandler
    {
        public static async Task<object> Handle(JsonObject p, ILogger logger, IConfiguration config)
        {
            // ---- 入力検証 ----
            var inArr = p["inputs"] as JsonArray ?? throw new RpcError(400, "E_NO_INPUTS");
            var inputs = inArr.Select(x => x?.GetValue<string>() ?? "")
                              .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (inputs.Count == 0) throw new RpcError(400, "E_NO_INPUTS");

            var output = p["output"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(output)) throw new RpcError(400, "E_NO_OUTPUT");

            foreach (var f in inputs) PathGuard.EnsureAllowedDwg(f, config);
            PathGuard.EnsureAllowedOutput(output!, config);                     // 安全なパスのみ許可 :contentReference[oaicite:4]{index=4}

            // ---- 付け足し規則 ----
            var rename = p["rename"] as JsonObject ?? new();
            var include = (rename["include"] as JsonArray)?.Select(j => j?.GetValue<string>()?.Trim())
                               .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                               ?? new List<string>();
            // ワイルドカード可（例: "A-WALL-*"）
            var format = rename["format"]?.GetValue<string>() ?? "{old}_{stem}";
            if (!format.Contains("{old}") || !format.Contains("{stem}"))
                throw new RpcError(400, "E_BAD_FORMAT: format must contain {old} and {stem}");

            // ---- accore 設定 ----
            var acc = p["accore"] as JsonObject ?? new();
            var accorePath = acc["path"]?.GetValue<string>() ?? (config["Accore:Path"] ?? "");
            if (string.IsNullOrWhiteSpace(accorePath)) throw new RpcError(400, "E_NO_ACCORE");
            var seed = acc["seed"]?.GetValue<string>() ?? (config["Accore:Seed"] ?? "");
            if (string.IsNullOrWhiteSpace(seed)) throw new RpcError(400, "E_NO_SEED"); // DXFIN 開始用の空シードを要求
            var locale = acc["locale"]?.GetValue<string>() ?? (config["Accore:Locale"] ?? "ja-JP");
            var timeoutMs = acc["timeoutMs"]?.GetValue<int?>() ??
                            (int.TryParse(config["Accore:TimeoutMs"], out var t) ? t : 180000);
            if (timeoutMs > 180000) timeoutMs = 180000;

            // ---- ステージング ----
            var st = p["stagingPolicy"] as JsonObject ?? new();
            var stagingRoot = st["root"]?.GetValue<string>() ??
                              (config["Staging:Root"] ?? Path.Combine(Path.GetTempPath(), "CadJobs", "Staging"));
            var keepTemp = st["keepTempOnError"]?.GetValue<bool?>() ??
                           (bool.TryParse(config["Staging:KeepOnError"], out var ko) && ko);
            var atomicWrite = st["atomicWrite"]?.GetValue<bool?>() ??
                              (bool.TryParse(config["Staging:AtomicWrite"], out var aw) && aw);

            var jobId = Guid.NewGuid().ToString("N");
            var jobDir = Path.Combine(stagingRoot, jobId);
            var inDir = Path.Combine(jobDir, "in");
            var outDir = Path.Combine(jobDir, "out");
            var logDir = Path.Combine(jobDir, "logs");
            Directory.CreateDirectory(inDir);
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(logDir);

            // 入力を staging にコピー
            var staged = new List<(string src, string copy, string stem)>();
            foreach (var src in inputs)
            {
                var copy = Path.Combine(inDir, Path.GetFileName(src));
                File.Copy(src, copy, overwrite: true);
                staged.Add((src, copy, Path.GetFileNameWithoutExtension(src)));
            }

            // ---- (A) DXF 書き出しスクリプトを生成＆実行 ----
            var dxfDir = Path.Combine(outDir, "dxf_raw");
            Directory.CreateDirectory(dxfDir);
            var exportScr = BuildExportToDxfScript(staged.Select(s => s.copy), dxfDir);
            var exportScrPath = Path.Combine(jobDir, "export_dxf.scr");
            await File.WriteAllTextAsync(exportScrPath, exportScr, new UTF8Encoding(false));

            logger.LogInformation("[dxf_textmap] export DXF start: {Accore}", accorePath);
            // 最初の DWG を /i に渡してスクリプトで OPEN 切替
            var expSeed = staged[0].copy;
            var expRes = AccoreRunner.Run(accorePath, expSeed, exportScrPath, locale, timeoutMs); // 実行・ログ採取 :contentReference[oaicite:5]{index=5}
            if (!expRes.Ok)
            {
                if (!keepTemp) { try { Directory.Delete(jobDir, true); } catch { } }
                return new { ok = false, step = "export_dxf", error = expRes.Error, stdoutTail = expRes.StdoutTail, stderrTail = expRes.StderrTail, staging = jobDir, exitCode = expRes.ExitCode };
            }

            // ---- (B) DXF テキスト編集（レイヤ名付け足し）----
            var editedDir = Path.Combine(outDir, "dxf_edited");
            Directory.CreateDirectory(editedDir);

            foreach (var s in staged)
            {
                var srcDxf = Path.Combine(dxfDir, s.stem + ".dxf");
                var dstDxf = Path.Combine(editedDir, s.stem + ".dxf");
                if (!File.Exists(srcDxf)) throw new IOException("DXF not produced: " + srcDxf);

                // 置換マップを構築：include のワイルドカードを展開（LAYER テーブルの実名を拾う）
                var layerNames = ReadLayerNames(srcDxf);
                var mapping = BuildMapping(layerNames, include, (old) => format.Replace("{old}", old).Replace("{stem}", s.stem));

                // DXF を安全置換（LAYER テーブルの 2 グループ値、および全エンティティの 8 グループ値を対象）
                DxfLayerRename(srcDxf, dstDxf, mapping);
            }

            // ---- (C) 置換済み DXF の統合（DXFIN）----
            var mergeScr = BuildDxfinMergeScript(Directory.GetFiles(editedDir, "*.dxf"), output);
            var mergeScrPath = Path.Combine(jobDir, "merge_dxfin.scr");
            await File.WriteAllTextAsync(mergeScrPath, mergeScr, new UTF8Encoding(false));

            logger.LogInformation("[dxf_textmap] merge DXFIN start: {Accore}", accorePath);
            var mrgRes = AccoreRunner.Run(accorePath, seed /* 空シード */, mergeScrPath, locale, timeoutMs);
            if (!mrgRes.Ok)
            {
                if (!keepTemp) { try { Directory.Delete(jobDir, true); } catch { } }
                return new { ok = false, step = "merge_dxfin", error = mrgRes.Error, stdoutTail = mrgRes.StdoutTail, stderrTail = mrgRes.StderrTail, staging = jobDir, exitCode = mrgRes.ExitCode };
            }

            // ---- (D) 原子保存（既存ユーティリティ）----
            try
            {
                var stagedOut = Path.Combine(outDir, "final.dwg");
                if (!File.Exists(stagedOut))
                {
                    // スクリプトは final.dwg に保存する設計
                    var any = Directory.GetFiles(outDir, "*.dwg").FirstOrDefault();
                    if (any != null) stagedOut = any;
                }
                if (!File.Exists(stagedOut)) throw new IOException("No merged DWG produced");

                if (atomicWrite)
                {
                    var tmp = output! + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.Copy(stagedOut, tmp, overwrite: true);
                    AtomicFile.AtomicMove(tmp, output!);                       // 原子置換 :contentReference[oaicite:6]{index=6}
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output!)!);
                    File.Copy(stagedOut, output!, overwrite: true);
                }
            }
            finally
            {
                if (!keepTemp) { try { Directory.Delete(jobDir, true); } catch { } }
            }

            return new { ok = true, output, staging = jobDir, msg = "Completed (DXF text map route)" };
        }

        // --- scripts ---

        private static string BuildExportToDxfScript(IEnumerable<string> dwgs, string dxfOutDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("._CMDECHO 0"); sb.AppendLine("._FILEDIA 0"); sb.AppendLine("._EXPERT 5");
            bool first = true;
            foreach (var dwg in dwgs)
            {
                var dxfPath = Path.Combine(dxfOutDir, Path.GetFileNameWithoutExtension(dwg) + ".dxf").Replace("\\", "/");
                if (!first) sb.AppendLine($"._OPEN \"{dwg.Replace("\\", "/")}\"");
                sb.AppendLine($"._-DXFOUT \"{dxfPath}\"");
                sb.AppendLine($"2018");
                sb.AppendLine($"Y");
                first = false;
            }
            sb.AppendLine("._QUIT Y");
            return sb.ToString();
        }

        private static string BuildDxfinMergeScript(IEnumerable<string> editedDxfs, string finalOutput)
        {
            var sb = new StringBuilder();
            sb.AppendLine("._CMDECHO 0"); sb.AppendLine("._FILEDIA 0"); sb.AppendLine("._EXPERT 5");
            foreach (var dxf in editedDxfs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"._DXFIN \"{dxf.Replace("\\", "/")}\"");
            sb.AppendLine("._-PURGE A * N"); sb.AppendLine("._-AUDIT Y");
            var outPath = Path.Combine(Path.GetDirectoryName(finalOutput)!, "final.dwg").Replace("\\", "/");
            sb.AppendLine($"._-SAVEAS 2018 \"{outPath}\"");
            sb.AppendLine("._QUIT Y");
            return sb.ToString();
        }

        // --- DXF helpers ---

        private static HashSet<string> ReadLayerNames(string dxfPath)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var sr = new StreamReader(dxfPath, DetectEncoding(dxfPath));
            string? line; string? prev = null; bool inLayerTable = false;
            while ((line = sr.ReadLine()) != null)
            {
                var t = line.Trim();
                if (prev == "0" && t == "TABLE") { /* 次の 2 の値を見る */ }
                if (prev == "2" && t == "LAYER") inLayerTable = true;
                if (prev == "0" && t == "ENDTAB") inLayerTable = false;

                // LAYER テーブル中の「2 → Name」
                if (inLayerTable && prev == "2" && !string.IsNullOrEmpty(t))
                    names.Add(t);

                prev = t;
            }
            return names;
        }

        private static Dictionary<string, string> BuildMapping(HashSet<string> layerNames, List<string> include, Func<string, string> composer)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (include.Count == 0) return map; // include が空なら何もしない（必要ならここで既定規則を定義）

            foreach (var pat in include)
            {
                var rx = WildcardToRegex(pat);
                foreach (var name in layerNames)
                    if (rx.IsMatch(name) && !map.ContainsKey(name))
                        map[name] = composer(name);
            }
            return map;
        }

        private static void DxfLayerRename(string src, string dst, Dictionary<string, string> map)
        {
            if (map.Count == 0) { File.Copy(src, dst, overwrite: true); return; }

            using var sr = new StreamReader(src, DetectEncoding(src));
            using var sw = new StreamWriter(dst, false, new UTF8Encoding(false));
            string? line; string? prev = null; bool inLayerTable = false;
            while ((line = sr.ReadLine()) != null)
            {
                var t = line; var code = prev?.Trim();

                // LAYER テーブル内の 2 (=layer name) と、エンティティの 8 (=layer name) に限定して置換
                if ((inLayerTable && code == "2") || code == "8")
                {
                    var name = t.Trim();
                    if (map.TryGetValue(name, out var nn)) { sw.WriteLine(nn); prev = null; continue; }
                }

                // 状態遷移
                var trim = t.Trim();
                if (code == "0" && trim == "TABLE") { /* 次の 2=LAYER を待つ */ }
                if (code == "2" && trim == "LAYER") inLayerTable = true;
                if (code == "0" && trim == "ENDTAB") inLayerTable = false;

                sw.WriteLine(t);
                prev = trim;
            }
        }

        private static Regex WildcardToRegex(string pat)
        {
            var esc = Regex.Escape(pat).Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex("^" + esc + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Encoding DetectEncoding(string path)
        {
            // DXF はほぼ ASCII/ANSI/UTF-8。BOM だけ簡易検出。
            using var fs = File.OpenRead(path);
            Span<byte> bom = stackalloc byte[3];
            int n = fs.Read(bom);
            if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            return new UTF8Encoding(false);
        }
    }
}
