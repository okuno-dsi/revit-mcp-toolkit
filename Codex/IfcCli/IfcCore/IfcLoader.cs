using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace IfcCore;

/// <summary>
/// Simple STEP text parser focused on:
///   - IFCPROPERTYSET / IFCPROPERTYSINGLEVALUE
///   - IFCRELDEFINESBYPROPERTIES
/// Used for property fill-rate analysis and checking.
/// </summary>
public class IfcLoader : IIfcLoader
{
    private sealed class Raw
    {
        public int Id;
        public string Type = string.Empty;
        public string[] Args = Array.Empty<string>();
    }

    public IfcModel Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        var model = new IfcModel { SourcePath = Path.GetFullPath(path) };
        var raws = new Dictionary<int, Raw>();

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            var t = line.Trim();
            if (t.Length == 0 || !t.StartsWith("#", StringComparison.Ordinal)) continue;

            var eq = t.IndexOf('=', 1);
            if (eq < 0) continue;
            var idPart = t.Substring(1, eq - 1);
            if (!int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;

            var rhs = t.Substring(eq + 1).Trim();
            var semi = rhs.LastIndexOf(';');
            if (semi >= 0) rhs = rhs.Substring(0, semi);

            var pIdx = rhs.IndexOf('(');
            if (pIdx <= 0) continue;

            var type = rhs.Substring(0, pIdx).Trim();
            var argStr = rhs.Substring(pIdx + 1);
            var lastParen = argStr.LastIndexOf(')');
            if (lastParen >= 0) argStr = argStr.Substring(0, lastParen);

            var args = SplitTopLevel(argStr);

            raws[id] = new Raw { Id = id, Type = type, Args = args };
        }

        // collect building storeys (for level assignment)
        var storeyNames = new Dictionary<int, string>();
        var storeyElev = new Dictionary<int, double>();

        foreach (var r in raws.Values)
        {
            if (r.Type.Equals("IFCBUILDINGSTOREY", StringComparison.OrdinalIgnoreCase))
            {
                if (r.Args.Length >= 3)
                {
                    var name = Unwrap(r.Args[2]);
                    storeyNames[r.Id] = name;
                }

                // Last argument is Elevation in IFC2x3 building storey
                if (r.Args.Length >= 10)
                {
                    var elevStr = r.Args[9].Trim();
                    if (double.TryParse(elevStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
                    {
                        storeyElev[r.Id] = elev;
                    }
                }
            }
        }

        // collect property singles and psets
        var singles = new Dictionary<int, (string Name, bool HasValue)>();
        var psets = new Dictionary<int, (string Pset, List<int> PropIds)>();

        foreach (var r in raws.Values)
        {
            if (r.Type.Equals("IFCPROPERTYSINGLEVALUE", StringComparison.OrdinalIgnoreCase))
            {
                if (r.Args.Length >= 3)
                {
                    var name = Unwrap(r.Args[0]);
                    var val = r.Args[2].Trim();
                    var hasValue = val != "$" && val.Length > 0;
                    singles[r.Id] = (name, hasValue);
                }
            }
            else if (r.Type.Equals("IFCPROPERTYSET", StringComparison.OrdinalIgnoreCase))
            {
                if (r.Args.Length >= 5)
                {
                    var psetName = Unwrap(r.Args[2]);
                    var listArg = r.Args[4].Trim();
                    var propIds = ParseIdList(listArg);
                    psets[r.Id] = (psetName, propIds);
                }
            }
        }

        // map element -> storey via IfcRelContainedInSpatialStructure
        var elementStorey = new Dictionary<int, int>();

        foreach (var r in raws.Values)
        {
            if (!r.Type.Equals("IFCRELCONTAINEDINSPATIALSTRUCTURE", StringComparison.OrdinalIgnoreCase)) continue;
            if (r.Args.Length < 6) continue;

            var relatedIds = ParseIdList(r.Args[4].Trim());
            var relating = r.Args[5].Trim();
            if (!relating.StartsWith("#", StringComparison.Ordinal)) continue;
            if (!int.TryParse(relating[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var storeyId)) continue;
            if (!storeyNames.ContainsKey(storeyId)) continue; // we only care about building storeys

            foreach (var eid in relatedIds)
            {
                elementStorey[eid] = storeyId;
            }
        }

        // relDefinesByProperties
        foreach (var r in raws.Values)
        {
            if (!r.Type.Equals("IFCRELDEFINESBYPROPERTIES", StringComparison.OrdinalIgnoreCase)) continue;
            if (r.Args.Length < 6) continue;

            var relObjs = ParseIdList(r.Args[4].Trim());
            var relPsets = ParseIdList(r.Args[5].Trim());

            foreach (var psetId in relPsets)
            {
                if (!psets.TryGetValue(psetId, out var psetInfo)) continue;
                var psetName = psetInfo.Pset;

                foreach (var entId in relObjs)
                {
                    if (!raws.TryGetValue(entId, out var entRaw)) continue;
                    var ent = GetOrAddEntity(model, entRaw);

                    foreach (var propId in psetInfo.PropIds)
                    {
                        if (!singles.TryGetValue(propId, out var s)) continue;
                        var key = new PropertyKey(psetName, s.Name);
                        ent.Properties[key] = s.HasValue;
                    }
                }
            }
        }

        // ensure all raws present as entities
        foreach (var r in raws.Values)
        {
            if (!model.EntitiesById.ContainsKey(r.Id))
            {
                GetOrAddEntity(model, r);
            }
        }

        // assign storey information to entities where available (via RelContainedInSpatialStructure)
        foreach (var kv in elementStorey)
        {
            if (model.EntitiesById.TryGetValue(kv.Key, out var ent))
            {
                ent.StoreyId = kv.Value;
                if (storeyNames.TryGetValue(kv.Value, out var sname))
                {
                    ent.StoreyName = sname;
                }
            }
        }

        // --------------------------------------------------------------------
        // Fallback: infer storey for IFCSPACE (and other orphan entities) from
        // ObjectPlacement height and storey Elevation.
        // --------------------------------------------------------------------
        if (storeyElev.Count > 0)
        {
            // 1) collect Cartesian point Z
            var cpZ = new Dictionary<int, double>();
            foreach (var r in raws.Values)
            {
                if (!r.Type.Equals("IFCCARTESIANPOINT", StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Args.Length == 0) continue;

                var coord = r.Args[0].Trim();
                if (coord.StartsWith("(", StringComparison.Ordinal) && coord.EndsWith(")", StringComparison.Ordinal))
                {
                    coord = coord.Substring(1, coord.Length - 2);
                }

                var parts = coord.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                double z = 0.0;
                if (parts.Length >= 3)
                {
                    var zStr = parts[2].Trim();
                    if (!double.TryParse(zStr, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    {
                        z = 0.0;
                    }
                }

                cpZ[r.Id] = z;
            }

            // 2) axis placement Z (location of Axis2Placement3D)
            var axisZ = new Dictionary<int, double>();
            foreach (var r in raws.Values)
            {
                if (!r.Type.Equals("IFCAXIS2PLACEMENT3D", StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Args.Length == 0) continue;

                var locRef = r.Args[0].Trim();
                if (!locRef.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!int.TryParse(locRef.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cpId)) continue;

                if (cpZ.TryGetValue(cpId, out var z))
                {
                    axisZ[r.Id] = z;
                }
            }

            // 3) local placement Z, with parent chaining
            var placementZ = new Dictionary<int, double>();

            double GetPlacementZ(int placementId)
            {
                if (placementZ.TryGetValue(placementId, out var cached))
                {
                    return cached;
                }

                if (!raws.TryGetValue(placementId, out var r) ||
                    !r.Type.Equals("IFCLOCALPLACEMENT", StringComparison.OrdinalIgnoreCase) ||
                    r.Args.Length < 2)
                {
                    placementZ[placementId] = 0.0;
                    return 0.0;
                }

                double parentZ = 0.0;
                var parentRef = r.Args[0].Trim();
                if (!string.IsNullOrWhiteSpace(parentRef) &&
                    !parentRef.Equals("$", StringComparison.Ordinal) &&
                    parentRef.StartsWith("#", StringComparison.Ordinal))
                {
                    if (int.TryParse(parentRef.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentId))
                    {
                        parentZ = GetPlacementZ(parentId);
                    }
                }

                double localZ = 0.0;
                var axisRef = r.Args[1].Trim();
                if (axisRef.StartsWith("#", StringComparison.Ordinal))
                {
                    if (int.TryParse(axisRef.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var axisId) &&
                        axisZ.TryGetValue(axisId, out var az))
                    {
                        localZ = az;
                    }
                }

                var total = parentZ + localZ;
                placementZ[placementId] = total;
                return total;
            }

            // 4) assign inferred storey for IFCSPACE without explicit storey
            foreach (var ent in model.EntitiesById.Values)
            {
                if (!ent.IfcType.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(ent.StoreyName)) continue; // already assigned

                if (!raws.TryGetValue(ent.Id, out var raw)) continue;
                if (raw.Args.Length < 6) continue;

                var placeRef = raw.Args[5].Trim();
                if (!placeRef.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!int.TryParse(placeRef.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var placeId)) continue;

                var z = GetPlacementZ(placeId);

                int bestStoreyId = 0;
                double bestDelta = double.MaxValue;

                foreach (var kv in storeyElev)
                {
                    var sid = kv.Key;
                    var elev = kv.Value;
                    var d = Math.Abs(elev - z);
                    if (d < bestDelta)
                    {
                        bestDelta = d;
                        bestStoreyId = sid;
                    }
                }

                if (bestStoreyId != 0)
                {
                    ent.StoreyId = bestStoreyId;
                    if (storeyNames.TryGetValue(bestStoreyId, out var sname))
                    {
                        ent.StoreyName = sname;
                    }
                }
            }
        }

        return model;
    }

    private static IfcEntity GetOrAddEntity(IfcModel model, Raw raw)
    {
        if (model.EntitiesById.TryGetValue(raw.Id, out var e)) return e;

        var gid = raw.Args.Length > 0 ? Unwrap(raw.Args[0]) : string.Empty;
        var name = raw.Args.Length > 2 ? Unwrap(raw.Args[2]) : string.Empty;
        e = new IfcEntity
        {
            Id = raw.Id,
            IfcType = raw.Type,
            GlobalId = gid,
            Name = name
        };
        model.AddEntity(e);
        return e;
    }

    private static string[] SplitTopLevel(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
        var list = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        bool inStr = false;

        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\'' && (i == 0 || s[i - 1] != '\\'))
            {
                inStr = !inStr;
                sb.Append(c);
                continue;
            }
            if (!inStr)
            {
                if (c == '(') { depth++; sb.Append(c); continue; }
                if (c == ')') { depth = Math.Max(0, depth - 1); sb.Append(c); continue; }
                if (c == ',' && depth == 0)
                {
                    list.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
            }
            sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString().Trim());
        return list.ToArray();
    }

    private static List<int> ParseIdList(string s)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(s)) return ids;
        s = s.Trim();
        if (s.StartsWith("(", StringComparison.Ordinal) && s.EndsWith(")", StringComparison.Ordinal))
        {
            s = s[1..^1];
        }
        var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.StartsWith("#", StringComparison.Ordinal)) t = t[1..];
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private static string Unwrap(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
        {
            s = s[1..^1];
        }

        // Decode STEP extended encoding for Unicode (e.g. \X2\4E8B52D95BA4\X0\)
        if (s.IndexOf(@"\X2\", StringComparison.Ordinal) >= 0)
        {
            return DecodeStepUnicode(s);
        }

        return s;
    }

    private static string DecodeStepUnicode(string s)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            // Look for \X2\ prefix (STEP extended encoding)
            if (i + 3 < s.Length &&
                s[i] == '\\' && s[i + 1] == 'X' && s[i + 2] == '2' && s[i + 3] == '\\')
            {
                i += 4; // skip "\X2\"
                var hexChunks = new List<string>();

                // Read 4-hex chunks until "\X0\" or end
                while (i + 3 < s.Length &&
                       !(s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'X' && s[i + 2] == '0' && s[i + 3] == '\\'))
                {
                    if (i + 4 <= s.Length)
                    {
                        hexChunks.Add(s.Substring(i, 4));
                        i += 4;
                    }
                    else
                    {
                        // trailing chars that don't form a full chunk
                        sb.Append(s[i]);
                        i++;
                        break;
                    }
                }

                // Skip the closing \X0\ if present
                if (i + 3 < s.Length &&
                    s[i] == '\\' && s[i + 1] == 'X' && s[i + 2] == '0' && s[i + 3] == '\\')
                {
                    i += 4;
                }

                foreach (var h in hexChunks)
                {
                    if (int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
                    {
                        sb.Append((char)cp);
                    }
                    else
                    {
                        sb.Append('?');
                    }
                }
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }

        return sb.ToString();
    }
}
