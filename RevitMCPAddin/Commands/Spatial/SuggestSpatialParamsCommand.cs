// ================================================================
// File: Commands/Spatial/SuggestSpatialParamsCommand.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Purpose: Suggest parameter names for Room / Space / Area (fuzzy matching)
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitRoom = Autodesk.Revit.DB.Architecture.Room;
using RevitArea = Autodesk.Revit.DB.Area;
using RevitSpace = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Spatial
{
    public class SuggestSpatialParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "spatial.suggest_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, message = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var kinds = ReadKinds(p);
            if (kinds.Count == 0)
                kinds.Add("room"); // default

            var hints = ReadHints(p);
            var matchMode = (p.Value<string>("matchMode") ?? "fuzzy").Trim().ToLowerInvariant();
            int maxMatches = Math.Max(1, p.Value<int?>("maxMatchesPerHint") ?? 5);
            int maxCandidates = Math.Max(1, p.Value<int?>("maxCandidates") ?? 200);
            int sampleLimit = Math.Max(1, p.Value<int?>("sampleLimitPerKind") ?? 30);
            bool includeAll = p.Value<bool?>("includeAllCandidates") ?? (hints.Count == 0);

            var elementIds = ReadElementIds(p);
            bool useAll = p.Value<bool?>("all") ?? (elementIds.Count == 0);

            var items = new List<object>();
            foreach (var kind in kinds)
            {
                var elems = CollectElements(doc, kind, elementIds, useAll, sampleLimit).ToList();
                var candidates = BuildCandidates(elems);

                var byHint = new List<object>();
                foreach (var hint in hints)
                {
                    var matches = Suggest(candidates, hint, matchMode, maxMatches);
                    byHint.Add(new
                    {
                        hint,
                        matches
                    });
                }

                var payload = new Dictionary<string, object>
                {
                    ["kind"] = kind,
                    ["elementSampleCount"] = elems.Count,
                    ["totalCandidates"] = candidates.Count,
                    ["byHint"] = byHint
                };

                if (includeAll)
                {
                    payload["candidates"] = candidates
                        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                        .Take(maxCandidates)
                        .Select(c => new
                        {
                            name = c.Name,
                            id = c.Id,
                            storageType = c.StorageType,
                            dataType = c.DataType,
                            count = c.Count
                        })
                        .ToList();
                }

                items.Add(payload);
            }

            return new
            {
                ok = true,
                kinds,
                hints,
                matchMode,
                maxMatchesPerHint = maxMatches,
                sampleLimitPerKind = sampleLimit,
                items
            };
        }

        private static List<string> ReadKinds(JObject p)
        {
            var kinds = new List<string>();
            var kindSingle = p.Value<string>("kind");
            if (!string.IsNullOrWhiteSpace(kindSingle))
                kinds.Add(NormalizeKind(kindSingle));

            if (p["kinds"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = t?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s))
                        kinds.Add(NormalizeKind(s));
                }
            }

            return kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ReadHints(JObject p)
        {
            var hints = new List<string>();
            var hintSingle = p.Value<string>("hint");
            if (!string.IsNullOrWhiteSpace(hintSingle))
                hints.Add(hintSingle);

            if (p["hints"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = t?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s))
                        hints.Add(s);
                }
            }

            return hints;
        }

        private static List<int> ReadElementIds(JObject p)
        {
            var ids = new List<int>();
            if (p["elementIds"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    int id;
                    if (int.TryParse(t.ToString(), out id))
                        ids.Add(id);
                }
            }
            return ids;
        }

        private static string NormalizeKind(string kind)
        {
            var k = kind.Trim().ToLowerInvariant();
            if (k == "rooms") k = "room";
            if (k == "spaces") k = "space";
            if (k == "areas") k = "area";
            return k;
        }

        private static IEnumerable<Element> CollectElements(Document doc, string kind, List<int> elementIds, bool useAll, int sampleLimit)
        {
            if (!useAll && elementIds.Count > 0)
            {
                foreach (var id in elementIds)
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                    if (e == null) continue;
                    if (DetectKind(e) == NormalizeKind(kind))
                        yield return e;
                }
                yield break;
            }

            BuiltInCategory bic;
            switch (NormalizeKind(kind))
            {
                case "room":
                    bic = BuiltInCategory.OST_Rooms;
                    break;
                case "space":
                    bic = BuiltInCategory.OST_MEPSpaces;
                    break;
                case "area":
                    bic = BuiltInCategory.OST_Areas;
                    break;
                default:
                    yield break;
            }

            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            int count = 0;
            foreach (var e in elems)
            {
                if (count >= sampleLimit) break;
                yield return e;
                count++;
            }
        }

        private static string DetectKind(Element elem)
        {
            if (elem is RevitRoom) return "room";
            if (elem is RevitSpace) return "space";
            if (elem is RevitArea) return "area";
            return null;
        }

        private sealed class ParamCandidate
        {
            public string Name;
            public int Id;
            public string StorageType;
            public string DataType;
            public int Count;
            public string Normalized;
        }

        private static List<ParamCandidate> BuildCandidates(IEnumerable<Element> elems)
        {
            var map = new Dictionary<string, ParamCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in elems)
            {
                foreach (Parameter prm in e.Parameters)
                {
                    var name = prm.Definition?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (!map.TryGetValue(name, out var c))
                    {
                        c = new ParamCandidate
                        {
                            Name = name,
                            Id = prm.Id.IntValue(),
                            StorageType = prm.StorageType.ToString(),
                            DataType = SafeDataType(prm),
                            Count = 0,
                            Normalized = Normalize(name)
                        };
                        map[name] = c;
                    }
                    c.Count++;
                }
            }
            return map.Values.ToList();
        }

        private static string SafeDataType(Parameter prm)
        {
            try
            {
                return prm.Definition?.GetDataType()?.TypeId;
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Convert full-width ASCII to half-width
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == '\u3000') { sb.Append(' '); continue; }
                if (ch >= 0xFF01 && ch <= 0xFF5E)
                {
                    sb.Append((char)(ch - 0xFEE0));
                    continue;
                }
                sb.Append(ch);
            }

            var t = sb.ToString().ToLowerInvariant();
            // Remove spaces and punctuation
            t = Regex.Replace(t, @"[\\s\\-_()/\\\\【】\\[\\]（）「」『』\""'`.,:;！？!?]+", "");
            return t;
        }

        private static List<object> Suggest(List<ParamCandidate> candidates, string hint, string matchMode, int maxMatches)
        {
            var hintNorm = Normalize(hint);
            var scored = new List<(ParamCandidate c, int score)>();

            foreach (var c in candidates)
            {
                int score = 0;
                if (matchMode == "exact")
                {
                    score = (c.Normalized == hintNorm) ? 100 : 0;
                }
                else if (matchMode == "contains")
                {
                    if (!string.IsNullOrEmpty(hintNorm) && c.Normalized.Contains(hintNorm))
                        score = 90;
                }
                else // fuzzy
                {
                    if (c.Normalized == hintNorm)
                        score = 100;
                    else if (!string.IsNullOrEmpty(hintNorm) && c.Normalized.Contains(hintNorm))
                        score = 90;
                    else if (!string.IsNullOrEmpty(hintNorm) && hintNorm.Contains(c.Normalized))
                        score = 80;
                }

                if (score > 0)
                    scored.Add((c, score));
            }

            return scored
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxMatches)
                .Select(x => new
                {
                    name = x.c.Name,
                    id = x.c.Id,
                    storageType = x.c.StorageType,
                    dataType = x.c.DataType,
                    count = x.c.Count,
                    score = x.score
                })
                .ToList<object>();
        }
    }
}
