#nullable enable
// ================================================================
// File   : Core/RebarMappingService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Data-driven logical parameter mapping for rebar automation and inspection.
// Notes  :
//  - Loads RebarMapping.json once (best-effort) and caches parsed profiles.
//  - Provides evaluation: logical key -> resolved value (+ optional debug/source).
//  - Does NOT modify the model (read-only).
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.Exceptions;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed class RebarMappingLoadStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public int? version { get; set; }
        public string? sha8 { get; set; }
        public string? units_length { get; set; }
        public string? profile_default { get; set; }
        public string[] profiles { get; set; } = Array.Empty<string>();
    }

    internal static class RebarMappingService
    {
        private const string EnvMappingPath = "REVITMCP_REBAR_MAPPING_PATH";
        private const string DefaultFileName = "RebarMapping.json";

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static RebarMappingIndex? _index;
        private static RebarMappingLoadStatus _status = new RebarMappingLoadStatus
        {
            ok = false,
            code = "NOT_LOADED",
            msg = "Rebar mapping not loaded."
        };

        public static RebarMappingLoadStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new RebarMappingLoadStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    version = _status.version,
                    sha8 = _status.sha8,
                    units_length = _status.units_length,
                    profile_default = _status.profile_default,
                    profiles = _status.profiles ?? Array.Empty<string>()
                };
            }
        }

        public static RebarMappingLoadStatus Reload()
        {
            lock (_gate)
            {
                _loaded = false;
                _index = null;
                _status = new RebarMappingLoadStatus
                {
                    ok = false,
                    code = "NOT_LOADED",
                    msg = "Rebar mapping not loaded."
                };
            }
            EnsureLoadedBestEffort();
            return GetStatus();
        }

        public static bool TryGetIndex(out RebarMappingIndex index)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                index = _index!;
                return _loaded && _index != null;
            }
        }

        /// <summary>
        /// Resolve logical keys for an element.
        /// </summary>
        /// <param name="doc">Document (for type params, cover types, etc.)</param>
        /// <param name="e">Target host element</param>
        /// <param name="requestedProfileName">If null/empty, auto-select by category; fallback to default profile.</param>
        /// <param name="keys">If null/empty, resolves all keys in the matched profile.</param>
        /// <param name="includeDebug">If true, include per-key source selection details.</param>
        public static JObject ResolveForElement(Document doc, Element e, string? requestedProfileName, IEnumerable<string>? keys, bool includeDebug)
        {
            if (doc == null || e == null)
                return new JObject { ["ok"] = false, ["code"] = "INVALID_ARGS", ["msg"] = "doc/element is null." };

            EnsureLoadedBestEffort();

            RebarMappingIndex? idx;
            lock (_gate) { idx = _index; }
            if (!_loaded || idx == null)
            {
                var st = GetStatus();
                return new JObject
                {
                    ["ok"] = false,
                    ["code"] = st.code ?? "NOT_READY",
                    ["msg"] = st.msg ?? "Rebar mapping is not available.",
                    ["path"] = st.path,
                    ["sha8"] = st.sha8
                };
            }

            RebarMappingProfile? prof = null;
            if (!string.IsNullOrWhiteSpace(requestedProfileName))
            {
                idx.TryGetProfile(requestedProfileName!.Trim(), out prof);
            }
            if (prof == null)
            {
                prof = idx.MatchProfileForElement(doc, e) ?? idx.GetDefaultProfile();
            }
            if (prof == null)
            {
                return new JObject
                {
                    ["ok"] = false,
                    ["code"] = "NOT_FOUND",
                    ["msg"] = "No mapping profiles available."
                };
            }

            var keyList = new List<string>();
            if (keys != null)
            {
                foreach (var k in keys)
                {
                    var kk = (k ?? string.Empty).Trim();
                    if (kk.Length > 0) keyList.Add(kk);
                }
            }
            if (keyList.Count == 0)
                keyList.AddRange(prof.Keys);

            int elementId = 0;
            try { elementId = e.Id.IntValue(); } catch { elementId = 0; }

            int typeId = 0;
            string typeName = string.Empty;
            try
            {
                var tid = e.GetTypeId();
                typeId = tid != null ? tid.IntValue() : 0;
                var t = doc.GetElement(tid) as ElementType;
                typeName = t != null ? (t.Name ?? string.Empty) : string.Empty;
            }
            catch { /* ignore */ }

            string categoryName = string.Empty;
            string? categoryBic = null;
            try
            {
                categoryName = e.Category != null ? (e.Category.Name ?? string.Empty) : string.Empty;
                int catId = e.Category != null && e.Category.Id != null ? e.Category.Id.IntValue() : 0;
                if (catId != 0)
                {
                    try { categoryBic = ((BuiltInCategory)catId).ToString(); } catch { categoryBic = null; }
                }
            }
            catch { /* ignore */ }

            var valuesObj = new JObject();
            var sourcesObj = new JObject();
            var errorsArr = new JArray();

            foreach (var key in keyList.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!prof.TryGetEntry(key, out var entry) || entry == null)
                {
                    errorsArr.Add(new JObject
                    {
                        ["key"] = key,
                        ["code"] = "NOT_FOUND",
                        ["msg"] = "Key not found in profile."
                    });
                    continue;
                }

                if (!TryResolveEntry(doc, e, entry, out var valueToken, out var usedSource, out var errCode, out var errMsg))
                {
                    errorsArr.Add(new JObject
                    {
                        ["key"] = entry.Key,
                        ["code"] = errCode ?? "NOT_RESOLVED",
                        ["msg"] = errMsg ?? "Failed to resolve value."
                    });
                    continue;
                }

                valuesObj[entry.Key] = valueToken ?? JValue.CreateNull();
                if (includeDebug && usedSource != null)
                    sourcesObj[entry.Key] = usedSource.ToJson();
            }

            var root = new JObject
            {
                ["ok"] = true,
                ["mapping"] = new JObject
                {
                    ["path"] = idx.Path,
                    ["sha8"] = idx.VersionShort,
                    ["version"] = idx.Version,
                    ["units_length"] = idx.UnitsLength,
                    ["profile"] = prof.Name
                },
                ["host"] = new JObject
                {
                    ["elementId"] = elementId,
                    ["categoryName"] = categoryName,
                    ["categoryBic"] = categoryBic,
                    ["typeId"] = typeId,
                    ["typeName"] = typeName
                },
                ["values"] = valuesObj,
                ["errors"] = errorsArr
            };

            if (includeDebug)
                root["sources"] = sourcesObj;

            return root;
        }

        private static bool TryResolveEntry(
            Document doc,
            Element host,
            RebarMappingEntry entry,
            out JToken? valueToken,
            out RebarMappingSource? usedSource,
            out string? errorCode,
            out string? errorMsg)
        {
            valueToken = null;
            usedSource = null;
            errorCode = null;
            errorMsg = null;

            foreach (var src in entry.Sources)
            {
                if (src == null) continue;
                try
                {
                    if (src.Kind == "constant")
                    {
                        if (TryConvertTokenToEntryType(src.ConstantValue, entry, out valueToken, out errorCode, out errorMsg))
                        {
                            usedSource = src;
                            return true;
                        }
                        continue;
                    }

                    if (src.Kind == "derived")
                    {
                        if (TryResolveDerived(doc, host, src.Name, entry, out valueToken, out errorCode, out errorMsg))
                        {
                            usedSource = src;
                            return true;
                        }
                        continue;
                    }

                    if (src.Kind == "instanceParam")
                    {
                        var p = host.LookupParameter(src.Name);
                        if (TryReadParameterAsEntryType(p, entry, out valueToken))
                        {
                            usedSource = src;
                            return true;
                        }
                        continue;
                    }

                    if (src.Kind == "instanceParamGuid")
                    {
                        if (TryParseGuid(src.Name, out var guid))
                        {
                            var p = TryFindParameterByGuid(host, guid);
                            if (TryReadParameterAsEntryType(p, entry, out valueToken))
                            {
                                usedSource = src;
                                return true;
                            }
                        }
                        continue;
                    }

                    if (src.Kind == "typeParam")
                    {
                        var tid = host.GetTypeId();
                        if (tid == null || tid == ElementId.InvalidElementId) continue;
                        var t = doc.GetElement(tid);
                        if (t == null) continue;
                        var p = t.LookupParameter(src.Name);
                        if (TryReadParameterAsEntryType(p, entry, out valueToken))
                        {
                            usedSource = src;
                            return true;
                        }
                        continue;
                    }

                    if (src.Kind == "typeParamGuid")
                    {
                        var tid = host.GetTypeId();
                        if (tid == null || tid == ElementId.InvalidElementId) continue;
                        var t = doc.GetElement(tid);
                        if (t == null) continue;

                        if (TryParseGuid(src.Name, out var guid))
                        {
                            var p = TryFindParameterByGuid(t, guid);
                            if (TryReadParameterAsEntryType(p, entry, out valueToken))
                            {
                                usedSource = src;
                                return true;
                            }
                        }
                        continue;
                    }

                    if (src.Kind == "builtInParam")
                    {
                        if (TryResolveBuiltInParameter(src.Name, out var bip))
                        {
                            var p = host.get_Parameter(bip);
                            if (TryReadParameterAsEntryType(p, entry, out valueToken))
                            {
                                usedSource = src;
                                return true;
                            }
                        }
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    errorCode = "REVIT_EXCEPTION";
                    errorMsg = ex.Message;
                    continue;
                }
            }

            errorCode = errorCode ?? "NOT_RESOLVED";
            errorMsg = errorMsg ?? "No sources produced a valid value.";
            return false;
        }

        private static bool TryResolveBuiltInParameter(string? name, out BuiltInParameter bip)
        {
            bip = BuiltInParameter.INVALID;
            var s = (name ?? string.Empty).Trim();
            if (s.Length == 0) return false;
            try
            {
                return Enum.TryParse(s, ignoreCase: true, result: out bip) && bip != BuiltInParameter.INVALID;
            }
            catch
            {
                bip = BuiltInParameter.INVALID;
                return false;
            }
        }

        private static bool TryParseGuid(string? s, out Guid guid)
        {
            guid = Guid.Empty;
            var t = (s ?? string.Empty).Trim();
            if (t.Length == 0) return false;
            try { return Guid.TryParse(t, out guid) && guid != Guid.Empty; }
            catch { guid = Guid.Empty; return false; }
        }

        private static Parameter? TryFindParameterByGuid(Element e, Guid guid)
        {
            if (e == null || guid == Guid.Empty) return null;
            try
            {
                foreach (Parameter p in e.Parameters)
                {
                    if (p == null) continue;
                    try
                    {
                        if (p.GUID == guid) return p;
                    }
                    catch
                    {
                        // Not a shared parameter (GUID not accessible) - ignore.
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static bool TryReadParameterAsEntryType(Parameter? p, RebarMappingEntry entry, out JToken? valueToken)
        {
            valueToken = null;
            if (p == null) return false;

            try
            {
                static bool IsLengthLikeSpec(Parameter par)
                {
                    try
                    {
                        var spec = UnitHelper.GetSpec(par);
                        if (spec == null) return false;
                        if (spec.Equals(SpecTypeId.Length)) return true;
                        var tid = spec.TypeId ?? string.Empty;
                        if (tid.IndexOf("reinforcementSpacing", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        return false;
                    }
                    catch { return false; }
                }

                static bool TryParseFirstIntegerToken(string text, out double number)
                {
                    number = 0.0;
                    if (string.IsNullOrWhiteSpace(text)) return false;

                    try
                    {
                        var s = text.Trim();
                        int i = 0;
                        while (i < s.Length && !char.IsDigit(s[i])) i++;
                        if (i >= s.Length) return false;

                        int j = i;
                        while (j < s.Length && char.IsDigit(s[j])) j++;
                        if (j <= i) return false;

                        var token = s.Substring(i, j - i);
                        int iv = 0;
                        if (!int.TryParse(token, out iv)) return false;
                        number = iv;
                        return true;
                    }
                    catch
                    {
                        number = 0.0;
                        return false;
                    }
                }

                switch (entry.Type)
                {
                    case "string":
                        {
                            string? s = null;
                            try { s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString(); } catch { s = null; }
                            if (string.IsNullOrWhiteSpace(s)) return false;

                            // Optional numeric validation for string values (e.g., "D25", "25 mm").
                            // If validation is present, require that a numeric token can be extracted.
                            try
                            {
                                if (entry.Min.HasValue || entry.Max.HasValue)
                                {
                                    double n = 0.0;
                                    if (!TryParseFirstIntegerToken(s, out n)) return false;
                                    if (!TryValidateNumber(entry, n, out var _, out var _2)) return false;
                                }
                            }
                            catch { return false; }

                            valueToken = new JValue(s);
                            return true;
                        }
                    case "int":
                        {
                            if (p.StorageType == StorageType.Integer)
                            {
                                int iv;
                                try { iv = p.AsInteger(); } catch { return false; }

                                try
                                {
                                    if (!TryValidateNumber(entry, iv, out var _, out var _2)) return false;
                                }
                                catch { return false; }

                                valueToken = new JValue(iv);
                                return true;
                            }
                            if (p.StorageType == StorageType.Double)
                            {
                                double raw = 0.0;
                                try { raw = p.AsDouble(); } catch { raw = 0.0; }

                                // Revit Length params are stored in internal units (ft), but many RC-family parameters
                                // store millimeters as plain numbers (non-Length spec). Heuristic: convert only when spec is length-like.
                                double v = raw;
                                try
                                {
                                    if (IsLengthLikeSpec(p))
                                        v = UnitHelper.FtToMm(raw);
                                }
                                catch { /* ignore */ }

                                try
                                {
                                    if (!TryValidateNumber(entry, v, out var _, out var _2)) return false;
                                }
                                catch { return false; }

                                valueToken = new JValue((int)Math.Round(v));
                                return true;
                            }
                            return false;
                        }
                    case "bool":
                        {
                            if (p.StorageType == StorageType.Integer)
                            {
                                int iv = 0;
                                try { iv = p.AsInteger(); } catch { iv = 0; }
                                valueToken = new JValue(iv != 0);
                                return true;
                            }
                            if (p.StorageType == StorageType.Double)
                            {
                                double ft = 0.0;
                                try { ft = p.AsDouble(); } catch { ft = 0.0; }
                                valueToken = new JValue(Math.Abs(ft) > 1e-9);
                                return true;
                            }
                            if (p.StorageType == StorageType.String)
                            {
                                var s = (p.AsString() ?? string.Empty).Trim();
                                if (s.Length == 0) return false;
                                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || s == "1")
                                {
                                    valueToken = new JValue(true);
                                    return true;
                                }
                                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || s == "0")
                                {
                                    valueToken = new JValue(false);
                                    return true;
                                }
                                return false;
                            }
                            return false;
                        }
                    case "double":
                        {
                            if (p.StorageType == StorageType.Double)
                            {
                                double raw = 0.0;
                                try { raw = p.AsDouble(); } catch { raw = 0.0; }

                                // Revit Length params are stored in internal units (ft), but many RC-family parameters
                                // store millimeters as plain numbers (non-Length spec). Convert only when spec is length-like.
                                double v = raw;
                                try
                                {
                                    if (IsLengthLikeSpec(p))
                                        v = UnitHelper.FtToMm(raw);
                                }
                                catch { /* ignore */ }

                                if (!TryValidateNumber(entry, v, out var _, out var _2)) return false;
                                valueToken = new JValue(Math.Round(v, 6));
                                return true;
                            }
                            if (p.StorageType == StorageType.Integer)
                            {
                                int iv = 0;
                                try { iv = p.AsInteger(); } catch { iv = 0; }
                                double dv = iv;
                                if (!TryValidateNumber(entry, dv, out var _, out var _2)) return false;
                                valueToken = new JValue(dv);
                                return true;
                            }
                            return false;
                        }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryConvertTokenToEntryType(JToken? token, RebarMappingEntry entry, out JToken? valueToken, out string? errorCode, out string? errorMsg)
        {
            valueToken = null;
            errorCode = null;
            errorMsg = null;

            if (token == null || token.Type == JTokenType.Null)
                return false;

            try
            {
                switch (entry.Type)
                {
                    case "string":
                        {
                            var s = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
                            if (string.IsNullOrWhiteSpace(s)) return false;
                            valueToken = new JValue(s);
                            return true;
                        }
                    case "int":
                        {
                            if (token.Type == JTokenType.Integer)
                            {
                                valueToken = new JValue(token.Value<int>());
                                return true;
                            }
                            if (int.TryParse(token.ToString(), out var iv))
                            {
                                valueToken = new JValue(iv);
                                return true;
                            }
                            return false;
                        }
                    case "bool":
                        {
                            if (token.Type == JTokenType.Boolean)
                            {
                                valueToken = new JValue(token.Value<bool>());
                                return true;
                            }
                            var s = token.ToString().Trim();
                            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || s == "1")
                            {
                                valueToken = new JValue(true);
                                return true;
                            }
                            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || s == "0")
                            {
                                valueToken = new JValue(false);
                                return true;
                            }
                            return false;
                        }
                    case "double":
                        {
                            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                            {
                                var dv = token.Value<double>();
                                if (!TryValidateNumber(entry, dv, out errorCode, out errorMsg)) return false;
                                valueToken = new JValue(Math.Round(dv, 6));
                                return true;
                            }
                            if (double.TryParse(token.ToString(), out var dv2))
                            {
                                if (!TryValidateNumber(entry, dv2, out errorCode, out errorMsg)) return false;
                                valueToken = new JValue(Math.Round(dv2, 6));
                                return true;
                            }
                            return false;
                        }
                }
            }
            catch (Exception ex)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = ex.Message;
                return false;
            }

            return false;
        }

        private static bool TryValidateNumber(RebarMappingEntry entry, double value, out string? errorCode, out string? errorMsg)
        {
            errorCode = null;
            errorMsg = null;

            if (entry.Min.HasValue && value < entry.Min.Value)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = $"Value {value} is below min {entry.Min.Value} for key '{entry.Key}'.";
                return false;
            }

            if (entry.Max.HasValue && value > entry.Max.Value)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = $"Value {value} is above max {entry.Max.Value} for key '{entry.Key}'.";
                return false;
            }

            return true;
        }

        private static bool TryResolveDerived(
            Document doc,
            Element host,
            string? derivedName,
            RebarMappingEntry entry,
            out JToken? valueToken,
            out string? errorCode,
            out string? errorMsg)
        {
            valueToken = null;
            errorCode = null;
            errorMsg = null;

            var name = (derivedName ?? string.Empty).Trim();
            if (name.Length == 0) return false;

            try
            {
                if (name.Equals("locationcurve.length", StringComparison.OrdinalIgnoreCase))
                {
                    var lc = host.Location as LocationCurve;
                    var curve = lc != null ? lc.Curve : null;
                    if (curve == null) return false;
                    double mm = UnitHelper.FtToMm(curve.Length);
                    if (!TryValidateNumber(entry, mm, out errorCode, out errorMsg)) return false;
                    valueToken = new JValue(Math.Round(mm, 6));
                    return true;
                }

                if (name.Equals("bbox.width_local", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bbox.height_local", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bbox.z_local", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetLocalBboxSizeMm(host, out var w, out var h, out var z))
                    {
                        double v = 0.0;
                        if (name.Equals("bbox.width_local", StringComparison.OrdinalIgnoreCase)) v = w;
                        if (name.Equals("bbox.height_local", StringComparison.OrdinalIgnoreCase)) v = h;
                        if (name.Equals("bbox.z_local", StringComparison.OrdinalIgnoreCase)) v = z;
                        if (!TryValidateNumber(entry, v, out errorCode, out errorMsg)) return false;
                        valueToken = new JValue(Math.Round(v, 6));
                        return true;
                    }
                    return false;
                }

                if (name.Equals("hostcover.top", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("hostcover.bottom", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("hostcover.other", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetHostCoverMm(doc, host, name, out var coverMm))
                    {
                        if (!TryValidateNumber(entry, coverMm, out errorCode, out errorMsg)) return false;
                        valueToken = new JValue(Math.Round(coverMm, 6));
                        return true;
                    }
                    return false;
                }
            }
            catch (DisabledDisciplineException ex)
            {
                errorCode = "DISCIPLINE_DISABLED";
                errorMsg = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorCode = "REVIT_EXCEPTION";
                errorMsg = ex.Message;
                return false;
            }

            errorCode = "NOT_FOUND";
            errorMsg = $"Unknown derived name: '{derivedName}'.";
            return false;
        }

        private static bool TryGetLocalBboxSizeMm(Element e, out double widthMm, out double heightMm, out double zMm)
        {
            widthMm = 0.0;
            heightMm = 0.0;
            zMm = 0.0;

            BoundingBoxXYZ? bb = null;
            try { bb = e.get_BoundingBox(null); } catch { bb = null; }
            if (bb == null) return false;

            Transform tr = Transform.Identity;
            try
            {
                if (e is FamilyInstance fi)
                {
                    tr = fi.GetTransform() ?? Transform.Identity;
                }
            }
            catch { tr = Transform.Identity; }

            Transform inv = Transform.Identity;
            try { inv = tr.Inverse; } catch { inv = Transform.Identity; }

            try
            {
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                };

                double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

                foreach (var p in corners)
                {
                    XYZ q;
                    try { q = inv.OfPoint(p); } catch { q = p; }

                    if (q.X < minX) minX = q.X;
                    if (q.Y < minY) minY = q.Y;
                    if (q.Z < minZ) minZ = q.Z;
                    if (q.X > maxX) maxX = q.X;
                    if (q.Y > maxY) maxY = q.Y;
                    if (q.Z > maxZ) maxZ = q.Z;
                }

                if (double.IsInfinity(minX) || double.IsInfinity(maxX)) return false;
                if (!(minX < maxX && minY < maxY && minZ < maxZ)) return false;

                widthMm = UnitHelper.FtToMm(maxX - minX);
                heightMm = UnitHelper.FtToMm(maxY - minY);
                zMm = UnitHelper.FtToMm(maxZ - minZ);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetHostCoverMm(Document doc, Element host, string derivedName, out double coverMm)
        {
            coverMm = 0.0;
            if (doc == null || host == null) return false;

            // 0) Round-column cover parameter (explicit instance/type parameter)
            try
            {
                if (TryGetNamedCoverParamMm(host, "かぶり厚-丸", out coverMm))
                    return true;
            }
            catch { /* ignore */ }

            BuiltInParameter bip;
            if (derivedName.Equals("hostcover.top", StringComparison.OrdinalIgnoreCase)) bip = BuiltInParameter.CLEAR_COVER_TOP;
            else if (derivedName.Equals("hostcover.bottom", StringComparison.OrdinalIgnoreCase)) bip = BuiltInParameter.CLEAR_COVER_BOTTOM;
            else bip = BuiltInParameter.CLEAR_COVER_OTHER;

            // 1) Built-in cover parameter (ElementId -> RebarCoverType)
            try
            {
                var p = host.get_Parameter(bip);
                if (p != null)
                {
                    if (p.StorageType == StorageType.ElementId)
                    {
                        var id = p.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            var coverType = doc.GetElement(id) as RebarCoverType;
                            if (coverType != null)
                            {
                                coverMm = UnitHelper.FtToMm(coverType.CoverDistance);
                                return true;
                            }
                        }
                    }
                    else if (p.StorageType == StorageType.Double)
                    {
                        coverMm = UnitHelper.FtToMm(p.AsDouble());
                        if (coverMm > 0.0) return true;
                    }
                }
            }
            catch { /* ignore */ }

            // 2) RebarHostData fallback (best-effort)
            try
            {
                if (RebarHostData.IsValidHost(host))
                {
                    var hd = RebarHostData.GetRebarHostData(host);
                    if (hd != null)
                    {
                        // Avoid compile-time dependency on StructuralFaceOrientation (API differences).
                        var orientType = Type.GetType("Autodesk.Revit.DB.Structure.StructuralFaceOrientation, RevitAPI");
                        if (orientType != null && orientType.IsEnum)
                        {
                            var mi = hd.GetType().GetMethod("GetCoverTypeId", new[] { orientType });
                            if (mi != null)
                            {
                                object o = null;
                                try
                                {
                                    string enumName = "Top";
                                    if (derivedName.Equals("hostcover.bottom", StringComparison.OrdinalIgnoreCase)) enumName = "Bottom";
                                    else if (derivedName.Equals("hostcover.other", StringComparison.OrdinalIgnoreCase)) enumName = "Other";

                                    try { o = Enum.Parse(orientType, enumName, true); }
                                    catch
                                    {
                                        // Fallback for "Other" depending on API: try Left, else first enum value.
                                        if (enumName.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                        {
                                            try { o = Enum.Parse(orientType, "Left", true); } catch { o = null; }
                                        }
                                        if (o == null)
                                        {
                                            var vals = Enum.GetValues(orientType);
                                            o = vals != null && vals.Length > 0 ? vals.GetValue(0) : null;
                                        }
                                    }
                                }
                                catch { o = null; }

                                if (o != null)
                                {
                                    var idObj = mi.Invoke(hd, new object[] { o });
                                    if (idObj is ElementId id && id != ElementId.InvalidElementId)
                                    {
                                        var coverType = doc.GetElement(id) as RebarCoverType;
                                        if (coverType != null)
                                        {
                                            coverMm = UnitHelper.FtToMm(coverType.CoverDistance);
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool TryGetNamedCoverParamMm(Element host, string paramName, out double coverMm)
        {
            coverMm = 0.0;
            if (host == null || string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter p = null;
            try { p = host.LookupParameter(paramName); } catch { p = null; }
            if (p == null)
            {
                try
                {
                    var t = host.Document?.GetElement(host.GetTypeId());
                    if (t != null) p = t.LookupParameter(paramName);
                }
                catch { p = null; }
            }

            if (p == null) return false;
            return TryReadParamAsMm(p, out coverMm);
        }

        private static bool TryReadParamAsMm(Parameter p, out double mm)
        {
            mm = 0.0;
            if (p == null) return false;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    mm = UnitHelper.FtToMm(p.AsDouble());
                    return mm > 0.0;
                }
                if (p.StorageType == StorageType.Integer)
                {
                    mm = p.AsInteger();
                    return mm > 0.0;
                }
                if (p.StorageType == StorageType.String)
                {
                    var s = (p.AsString() ?? "").Trim();
                    if (s.Length == 0) return false;
                    var m = Regex.Match(s, @"[-+]?\d+(\.\d+)?");
                    if (!m.Success) return false;
                    if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        mm = v;
                        return mm > 0.0;
                    }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        private static void EnsureLoadedBestEffort()
        {
            lock (_gate)
            {
                if (_loaded) return;

                try
                {
                    var path = ResolveMappingPath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        _status = new RebarMappingLoadStatus
                        {
                            ok = false,
                            code = "REBAR_MAPPING_NOT_FOUND",
                            msg = "RebarMapping.json not found. Place it in the add-in folder (preferred), %LOCALAPPDATA%\\RevitMCP (cache), or %USERPROFILE%\\Documents\\Codex\\Design.",
                            path = path
                        };
                        _loaded = false;
                        _index = null;
                        return;
                    }

                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var root = JObject.Parse(json);
                    var sha = Sha256Hex(bytes);
                    _index = RebarMappingIndex.Build(root, path, sha, out _status);
                    _loaded = true;
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _index = null;
                    _status = new RebarMappingLoadStatus
                    {
                        ok = false,
                        code = "REBAR_MAPPING_LOAD_FAILED",
                        msg = ex.Message
                    };
                }
            }
        }

        private static string? ResolveMappingPath()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvMappingPath);
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env!)) return env;
            }
            catch { /* ignore */ }

            try
            {
                // Preferred: next to the add-in (packaged/installed).
                var p0 = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, "Resources", DefaultFileName);
                if (File.Exists(p0)) return p0;
            }
            catch { /* ignore */ }

            try
            {
                var p0b = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, DefaultFileName);
                if (File.Exists(p0b)) return p0b;
            }
            catch { /* ignore */ }

            try
            {
                // Cache / local override
                var p1 = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.LocalRoot, DefaultFileName);
                if (File.Exists(p1)) return p1;
            }
            catch { /* ignore */ }

            try
            {
                // Legacy/dev: Documents\Codex\Design
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var p2 = System.IO.Path.Combine(docs, "Codex", "Design", DefaultFileName);
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { /* ignore */ }

            // Dev convenience: walk up and look for "<ancestor>\\Codex\\Design\\RebarMapping.json"
            try
            {
                var cur = RevitMCPAddin.Core.Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = System.IO.Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = System.IO.Path.Combine(parent, "Codex", "Design", DefaultFileName);
                    if (File.Exists(cand)) return cand;
                    cur = parent;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ----------------- Parsed index models -----------------
        internal sealed class RebarMappingIndex
        {
            public string Path { get; private set; } = string.Empty;
            public string VersionShort { get; private set; } = string.Empty; // sha8
            public int Version { get; private set; }
            public string UnitsLength { get; private set; } = "mm";

            private readonly Dictionary<string, RebarMappingProfile> _profiles = new Dictionary<string, RebarMappingProfile>(StringComparer.OrdinalIgnoreCase);
            private string _defaultProfileName = string.Empty;

            public static RebarMappingIndex Build(JObject root, string path, string shaHex, out RebarMappingLoadStatus status)
            {
                var idx = new RebarMappingIndex();
                idx.Path = path ?? string.Empty;
                idx.VersionShort = (shaHex ?? string.Empty).Length >= 8 ? shaHex.Substring(0, 8) : (shaHex ?? string.Empty);

                int version = 0;
                try { version = root.Value<int?>("version") ?? 0; } catch { version = 0; }
                idx.Version = version;

                string unitsLen = "mm";
                try
                {
                    var u = root["units"] as JObject;
                    if (u != null) unitsLen = (u.Value<string>("length") ?? "mm").Trim();
                }
                catch { /* ignore */ }
                idx.UnitsLength = string.IsNullOrWhiteSpace(unitsLen) ? "mm" : unitsLen;

                var profilesArr = root["profiles"] as JArray;
                int loaded = 0;
                if (profilesArr != null)
                {
                    foreach (var pTok in profilesArr.OfType<JObject>())
                    {
                        var prof = RebarMappingProfile.TryParse(pTok);
                        if (prof == null) continue;
                        idx._profiles[prof.Name] = prof;
                        loaded++;
                    }
                }

                if (idx._profiles.ContainsKey("default")) idx._defaultProfileName = "default";
                else if (idx._profiles.Count > 0) idx._defaultProfileName = idx._profiles.Keys.FirstOrDefault() ?? string.Empty;

                status = new RebarMappingLoadStatus
                {
                    ok = true,
                    code = "OK",
                    msg = $"Rebar mapping loaded ({loaded} profiles).",
                    path = idx.Path,
                    version = idx.Version,
                    sha8 = idx.VersionShort,
                    units_length = idx.UnitsLength,
                    profile_default = idx._defaultProfileName,
                    profiles = idx._profiles.Keys.OrderBy(x => x).ToArray()
                };

                return idx;
            }

            public bool TryGetProfile(string name, out RebarMappingProfile profile)
            {
                return _profiles.TryGetValue((name ?? string.Empty).Trim(), out profile!);
            }

            public RebarMappingProfile? GetDefaultProfile()
            {
                if (!string.IsNullOrWhiteSpace(_defaultProfileName) && _profiles.TryGetValue(_defaultProfileName, out var p))
                    return p;
                return _profiles.Values.FirstOrDefault();
            }

            public RebarMappingProfile? MatchProfileForElement(Document doc, Element e)
            {
                if (e == null) return null;

                string? bic = null;
                string catName = string.Empty;
                try
                {
                    catName = e.Category != null ? (e.Category.Name ?? string.Empty) : string.Empty;
                    int catId = e.Category != null && e.Category.Id != null ? e.Category.Id.IntValue() : 0;
                    if (catId != 0)
                    {
                        try { bic = ((BuiltInCategory)catId).ToString(); } catch { bic = null; }
                    }
                }
                catch { /* ignore */ }

                Element? type = null;
                string typeName = string.Empty;
                string familyName = string.Empty;
                try
                {
                    var tid = e.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                    {
                        type = doc != null ? doc.GetElement(tid) : null;
                        typeName = type != null ? (type.Name ?? string.Empty) : string.Empty;
                        var et = type as ElementType;
                        if (et != null) familyName = et.FamilyName ?? string.Empty;
                    }
                }
                catch { /* ignore */ }

                bool ContainsAny(string hay, IEnumerable<string> needles)
                {
                    if (string.IsNullOrWhiteSpace(hay)) return false;
                    foreach (var n in needles)
                    {
                        var t = (n ?? string.Empty).Trim();
                        if (t.Length == 0) continue;
                        if (hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                    return false;
                }

                bool HasAnyParam(Element? target, IEnumerable<string> paramNames)
                {
                    if (target == null) return false;
                    foreach (var n in paramNames)
                    {
                        var t = (n ?? string.Empty).Trim();
                        if (t.Length == 0) continue;
                        try
                        {
                            if (target.LookupParameter(t) != null) return true;
                        }
                        catch { /* ignore */ }
                    }
                    return false;
                }

                bool CategoryMatch(RebarMappingProfile prof)
                {
                    if (prof.AppliesToAll) return true;
                    if (!string.IsNullOrWhiteSpace(bic) && prof.AppliesToCategories.Contains(bic!)) return true;
                    if (!string.IsNullOrWhiteSpace(catName) && prof.AppliesToCategories.Contains(catName)) return true;
                    return false;
                }

                bool ProfileMatch(RebarMappingProfile prof)
                {
                    if (!CategoryMatch(prof)) return false;

                    if (prof.FamilyNameContains.Count > 0 && !ContainsAny(familyName, prof.FamilyNameContains)) return false;
                    if (prof.TypeNameContains.Count > 0 && !ContainsAny(typeName, prof.TypeNameContains)) return false;

                    if (prof.RequiresInstanceParamsAny.Count > 0 && !HasAnyParam(e, prof.RequiresInstanceParamsAny)) return false;
                    if (prof.RequiresTypeParamsAny.Count > 0 && !HasAnyParam(type, prof.RequiresTypeParamsAny)) return false;

                    return true;
                }

                int SpecificityScore(RebarMappingProfile prof)
                {
                    int s = 0;
                    if (!prof.AppliesToAll) s += 10;
                    if (prof.FamilyNameContains.Count > 0) s += 5;
                    if (prof.TypeNameContains.Count > 0) s += 5;
                    if (prof.RequiresInstanceParamsAny.Count > 0) s += 20;
                    if (prof.RequiresTypeParamsAny.Count > 0) s += 20;
                    return s;
                }

                RebarMappingProfile? best = null;
                int bestPri = int.MinValue;
                int bestSpec = int.MinValue;
                foreach (var prof in _profiles.Values)
                {
                    if (prof == null) continue;
                    if (!ProfileMatch(prof)) continue;

                    int pri = prof.Priority;
                    int spec = SpecificityScore(prof);
                    if (best == null || pri > bestPri || (pri == bestPri && spec > bestSpec) || (pri == bestPri && spec == bestSpec && string.CompareOrdinal(prof.Name, best.Name) < 0))
                    {
                        best = prof;
                        bestPri = pri;
                        bestSpec = spec;
                    }
                }

                return best;
            }
        }

        internal sealed class RebarMappingProfile
        {
            public string Name { get; private set; } = "default";
            public bool AppliesToAll { get; private set; }
            public HashSet<string> AppliesToCategories { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int Priority { get; private set; }
            public List<string> FamilyNameContains { get; private set; } = new List<string>();
            public List<string> TypeNameContains { get; private set; } = new List<string>();
            public List<string> RequiresTypeParamsAny { get; private set; } = new List<string>();
            public List<string> RequiresInstanceParamsAny { get; private set; } = new List<string>();

            private readonly Dictionary<string, RebarMappingEntry> _map = new Dictionary<string, RebarMappingEntry>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> Keys => _map.Keys.OrderBy(x => x);

            public bool TryGetEntry(string key, out RebarMappingEntry entry)
            {
                return _map.TryGetValue((key ?? string.Empty).Trim(), out entry!);
            }

            public static RebarMappingProfile? TryParse(JObject obj)
            {
                if (obj == null) return null;

                var name = (obj.Value<string>("name") ?? string.Empty).Trim();
                if (name.Length == 0) name = "default";

                var prof = new RebarMappingProfile();
                prof.Name = name;
                try { prof.Priority = obj.Value<int?>("priority") ?? 0; } catch { prof.Priority = 0; }

                try
                {
                    var applies = obj["appliesTo"] as JObject;
                    var catsArr = applies != null ? (applies["categories"] as JArray) : null;
                    if (catsArr != null && catsArr.Count > 0)
                    {
                        foreach (var t in catsArr)
                        {
                            if (t == null || t.Type != JTokenType.String) continue;
                            var s = (t.Value<string>() ?? string.Empty).Trim();
                            if (s.Length > 0) prof.AppliesToCategories.Add(s);
                        }
                    }

                    var famArr = applies != null ? (applies["familyNameContains"] as JArray) : null;
                    if (famArr != null)
                    {
                        foreach (var t in famArr)
                        {
                            if (t == null || t.Type != JTokenType.String) continue;
                            var s = (t.Value<string>() ?? string.Empty).Trim();
                            if (s.Length > 0) prof.FamilyNameContains.Add(s);
                        }
                    }

                    var typeArr = applies != null ? (applies["typeNameContains"] as JArray) : null;
                    if (typeArr != null)
                    {
                        foreach (var t in typeArr)
                        {
                            if (t == null || t.Type != JTokenType.String) continue;
                            var s = (t.Value<string>() ?? string.Empty).Trim();
                            if (s.Length > 0) prof.TypeNameContains.Add(s);
                        }
                    }

                    var reqTypeAnyArr = applies != null ? (applies["requiresTypeParamsAny"] as JArray) : null;
                    if (reqTypeAnyArr != null)
                    {
                        foreach (var t in reqTypeAnyArr)
                        {
                            if (t == null || t.Type != JTokenType.String) continue;
                            var s = (t.Value<string>() ?? string.Empty).Trim();
                            if (s.Length > 0) prof.RequiresTypeParamsAny.Add(s);
                        }
                    }

                    var reqInstAnyArr = applies != null ? (applies["requiresInstanceParamsAny"] as JArray) : null;
                    if (reqInstAnyArr != null)
                    {
                        foreach (var t in reqInstAnyArr)
                        {
                            if (t == null || t.Type != JTokenType.String) continue;
                            var s = (t.Value<string>() ?? string.Empty).Trim();
                            if (s.Length > 0) prof.RequiresInstanceParamsAny.Add(s);
                        }
                    }
                }
                catch { /* ignore */ }
                prof.AppliesToAll = prof.AppliesToCategories.Count == 0;

                var mapObj = obj["map"] as JObject;
                if (mapObj != null)
                {
                    foreach (var kv in mapObj)
                    {
                        var key = (kv.Key ?? string.Empty).Trim();
                        if (key.Length == 0) continue;
                        if (!(kv.Value is JObject entryObj)) continue;
                        var ent = RebarMappingEntry.TryParse(key, entryObj);
                        if (ent == null) continue;
                        prof._map[ent.Key] = ent;
                    }
                }

                return prof;
            }
        }

        internal sealed class RebarMappingEntry
        {
            public string Key { get; private set; } = string.Empty;
            public string Type { get; private set; } = "string"; // string|double|int|bool
            public string Unit { get; private set; } = string.Empty; // e.g. mm
            public double? Min { get; private set; }
            public double? Max { get; private set; }
            public List<RebarMappingSource> Sources { get; private set; } = new List<RebarMappingSource>();

            public static RebarMappingEntry? TryParse(string key, JObject obj)
            {
                if (obj == null) return null;
                var ent = new RebarMappingEntry();
                ent.Key = (key ?? string.Empty).Trim();
                if (ent.Key.Length == 0) return null;

                string type = (obj.Value<string>("type") ?? "string").Trim().ToLowerInvariant();
                if (type != "string" && type != "double" && type != "int" && type != "bool") type = "string";
                ent.Type = type;
                ent.Unit = (obj.Value<string>("unit") ?? string.Empty).Trim();

                try
                {
                    var val = obj["validation"] as JObject;
                    if (val != null)
                    {
                        var minTok = val["min"];
                        var maxTok = val["max"];
                        if (minTok != null && (minTok.Type == JTokenType.Float || minTok.Type == JTokenType.Integer))
                            ent.Min = minTok.Value<double>();
                        if (maxTok != null && (maxTok.Type == JTokenType.Float || maxTok.Type == JTokenType.Integer))
                            ent.Max = maxTok.Value<double>();
                    }
                }
                catch { /* ignore */ }

                var srcArr = obj["sources"] as JArray;
                if (srcArr != null)
                {
                    foreach (var sTok in srcArr.OfType<JObject>())
                    {
                        var s = RebarMappingSource.TryParse(sTok);
                        if (s != null) ent.Sources.Add(s);
                    }
                }

                return ent;
            }
        }

        internal sealed class RebarMappingSource
        {
            public string Kind { get; private set; } = string.Empty;
            public string Name { get; private set; } = string.Empty;
            public JToken? ConstantValue { get; private set; }

            public static RebarMappingSource? TryParse(JObject obj)
            {
                if (obj == null) return null;
                var kind = (obj.Value<string>("kind") ?? string.Empty).Trim();
                if (kind.Length == 0) return null;

                var src = new RebarMappingSource();
                src.Kind = kind;
                src.Name = (obj.Value<string>("name") ?? string.Empty).Trim();

                if (kind.Equals("constant", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "constant";
                    src.ConstantValue = obj["value"];
                }
                else if (kind.Equals("derived", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "derived";
                }
                else if (kind.Equals("instanceParam", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "instanceParam";
                }
                else if (kind.Equals("instanceParamGuid", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "instanceParamGuid";
                }
                else if (kind.Equals("typeParam", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "typeParam";
                }
                else if (kind.Equals("typeParamGuid", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "typeParamGuid";
                }
                else if (kind.Equals("builtInParam", StringComparison.OrdinalIgnoreCase))
                {
                    src.Kind = "builtInParam";
                }
                else
                {
                    src.Kind = kind;
                }

                return src;
            }

            public JObject ToJson()
            {
                var o = new JObject
                {
                    ["kind"] = Kind,
                    ["name"] = Name
                };
                if (Kind == "constant")
                    o["value"] = ConstantValue != null ? ConstantValue.DeepClone() : JValue.CreateNull();
                return o;
            }
        }
    }
}
