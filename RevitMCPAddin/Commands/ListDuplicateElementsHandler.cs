// RevitMcpAddin/Commands/ListDuplicateElementsHandler.cs
// 修正版: Arc.Angle → (EndAngle - StartAngle)
// #endregion ディレクティブも補完済

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMcpAddin.Commands
{
    public sealed class ListDuplicateElementsHandler : IExternalEventHandler
    {
        private static readonly NumberFormatInfo NFI = new CultureInfo("en-US", false).NumberFormat;
        private JObject _payload;

        public void SetPayload(JObject payload) => _payload = payload;
        public string GetName() => "ListDuplicateElementsHandler";

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                ReturnError("No active document.");
                return;
            }

            double tolFeet = 0.00328084; // ~1mm default
            try
            {
                var pr = _payload?["params"];
                if (pr != null && pr["toleranceFeet"] != null)
                    tolFeet = Math.Max(1e-9, pr.Value<double>("toleranceFeet"));
            }
            catch { }

            var fec = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var skippedNoLoc = 0;
            var skippedOthers = 0;
            var mapPoint = new Dictionary<string, List<Item>>(4096);
            var mapCurve = new Dictionary<string, List<Item>>(4096);

            foreach (var e in fec)
            {
                try
                {
                    var cat = e.Category;
                    if (cat == null) { skippedOthers++; continue; }

                    var typeId = e.GetTypeId();
                    var loc = e.Location;
                    if (loc == null) { skippedNoLoc++; continue; }

                    if (loc is LocationPoint lp)
                    {
                        var p = lp.Point;
                        var key = BuildPointKey(cat.Id.IntValue(), typeId.IntValue(), p, tolFeet);
                        var meta = new Item(e.Id, cat.Name, GetElementTypeName(doc, typeId), "LocationPoint", p, null);
                        Append(mapPoint, key, meta);
                    }
                    else if (loc is LocationCurve lc)
                    {
                        var c = lc.Curve;
                        if (c == null) { skippedOthers++; continue; }

                        string key;
                        CurveSignature sig = BuildCurveSignature(c, tolFeet, out key);
                        var meta = new Item(e.Id, cat.Name, GetElementTypeName(doc, typeId), "LocationCurve", sig?.Anchor, sig);
                        Append(mapCurve, key, meta);
                    }
                    else
                        skippedOthers++;
                }
                catch { skippedOthers++; }
            }

            var result = new ResultDto
            {
                ok = true,
                toleranceFeet = tolFeet,
                duplicates = new List<DuplicateGroup>(),
                skipped = new Dictionary<string, int> {
                    { "noLocation", skippedNoLoc },
                    { "others", skippedOthers }
                }
            };

            foreach (var kv in mapPoint)
            {
                var list = kv.Value;
                if (list.Count >= 2)
                {
                    var f = list[0];
                    result.duplicates.Add(new DuplicateGroup
                    {
                        category = f.CategoryName,
                        typeName = f.TypeName,
                        count = list.Count,
                        elementIds = list.Select(x => x.Id.IntValue()).ToList(),
                        location = new PointDto(f.Point),
                        mode = f.Mode
                    });
                }
            }

            foreach (var kv in mapCurve)
            {
                var list = kv.Value;
                if (list.Count >= 2)
                {
                    var f = list[0];
                    result.duplicates.Add(new DuplicateGroup
                    {
                        category = f.CategoryName,
                        typeName = f.TypeName,
                        count = list.Count,
                        elementIds = list.Select(x => x.Id.IntValue()).ToList(),
                        location = new PointDto(f.Point ?? f.Sig?.Anchor ?? XYZ.Zero),
                        mode = f.Mode
                    });
                }
            }

            result.duplicates = result.duplicates
                .OrderByDescending(d => d.count)
                .ThenBy(d => d.category)
                .ThenBy(d => d.typeName)
                .ToList();

            ReturnOk(result);
        }

        #region Helpers

        private static void Append(Dictionary<string, List<Item>> map, string key, Item item)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<Item>();
                map[key] = list;
            }
            list.Add(item);
        }

        private static string GetElementTypeName(Document doc, ElementId typeId)
        {
            if (typeId == ElementId.InvalidElementId) return "";
            return doc.GetElement(typeId)?.Name ?? "";
        }

        private static string BuildPointKey(int catId, int typeId, XYZ p, double tolFeet)
        {
            var rx = RoundToGrid(p.X, tolFeet);
            var ry = RoundToGrid(p.Y, tolFeet);
            var rz = RoundToGrid(p.Z, tolFeet);
            return $"cat:{catId};type:{typeId};pt:{rx},{ry},{rz}";
        }

        private static CurveSignature BuildCurveSignature(Curve c, double tolFeet, out string key)
        {
            if (c is Line ln)
            {
                var a = ln.GetEndPoint(0);
                var b = ln.GetEndPoint(1);
                var s1 = $"{RoundToGrid(a.X, tolFeet)},{RoundToGrid(a.Y, tolFeet)},{RoundToGrid(a.Z, tolFeet)}";
                var s2 = $"{RoundToGrid(b.X, tolFeet)},{RoundToGrid(b.Y, tolFeet)},{RoundToGrid(b.Z, tolFeet)}";
                var smin = string.Compare(s1, s2, StringComparison.Ordinal) <= 0 ? s1 : s2;
                var smax = smin == s1 ? s2 : s1;
                key = $"curve:line;A:{smin};B:{smax}";
                return new CurveSignature { Kind = "Line", Anchor = a };
            }
            else if (c is Arc arc)
            {
                var center = arc.Center;
                var r = arc.Radius;

                // 端点（順不同に揃える）
                var e0 = arc.GetEndPoint(0);
                var e1 = arc.GetEndPoint(1);
                var e0s = $"{RoundToGrid(e0.X, tolFeet)},{RoundToGrid(e0.Y, tolFeet)},{RoundToGrid(e0.Z, tolFeet)}";
                var e1s = $"{RoundToGrid(e1.X, tolFeet)},{RoundToGrid(e1.Y, tolFeet)},{RoundToGrid(e1.Z, tolFeet)}";
                var smin = string.Compare(e0s, e1s, StringComparison.Ordinal) <= 0 ? e0s : e1s;
                var smax = (smin == e0s) ? e1s : e0s;

                // 弧長でマイナー/メジャーを区別（角度は使わない）
                var len = RoundToGrid(arc.Length, tolFeet);

                key = $"curve:arc;"
                    + $"C:{RoundToGrid(center.X, tolFeet)},{RoundToGrid(center.Y, tolFeet)},{RoundToGrid(center.Z, tolFeet)};"
                    + $"R:{RoundToGrid(r, tolFeet)};"
                    + $"E:{smin}|{smax};"
                    + $"L:{len}";

                return new CurveSignature { Kind = "Arc", Anchor = center };
            }
            else if (c is Ellipse el)
            {
                var center = el.Center;
                key = $"curve:ellipse;C:{RoundToGrid(center.X, tolFeet)},{RoundToGrid(center.Y, tolFeet)},{RoundToGrid(center.Z, tolFeet)};R:{RoundToGrid(el.RadiusX, tolFeet)},{RoundToGrid(el.RadiusY, tolFeet)}";
                return new CurveSignature { Kind = "Ellipse", Anchor = center };
            }
            else
            {
                var a = c.GetEndPoint(0);
                var b = c.GetEndPoint(1);
                var length = RoundToGrid(c.ApproximateLength, tolFeet);
                var s1 = $"{RoundToGrid(a.X, tolFeet)},{RoundToGrid(a.Y, tolFeet)},{RoundToGrid(a.Z, tolFeet)}";
                var s2 = $"{RoundToGrid(b.X, tolFeet)},{RoundToGrid(b.Y, tolFeet)},{RoundToGrid(b.Z, tolFeet)}";
                var smin = string.Compare(s1, s2, StringComparison.Ordinal) <= 0 ? s1 : s2;
                var smax = smin == s1 ? s2 : s1;
                key = $"curve:other;A:{smin};B:{smax};L:{length}";
                return new CurveSignature { Kind = "Other", Anchor = a };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string RoundToGrid(double value, double grid)
        {
            if (grid <= 0) return value.ToString("G17", NFI);
            var q = Math.Round(value / grid);
            var v = q * grid;
            return v.ToString("0.######", NFI);
        }

        #endregion

        #region DTOs and Utilities

        private void ReturnOk(ResultDto dto)
        {
            var json = JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = _payload?["id"], result = dto });
            RevitLogger.WriteInfo(json);
            RpcReturnChannel.Set(json);
        }

        private void ReturnError(string msg)
        {
            var json = JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = _payload?["id"], error = new { code = -32001, message = msg } });
            RevitLogger.WriteError(json);
            RpcReturnChannel.Set(json);
        }

        private sealed class Item
        {
            public ElementId Id;
            public string CategoryName, TypeName, Mode;
            public XYZ Point;
            public CurveSignature Sig;
            public Item(ElementId id, string cat, string typ, string mode, XYZ p, CurveSignature sig)
            { Id = id; CategoryName = cat; TypeName = typ; Mode = mode; Point = p; Sig = sig; }
        }

        private sealed class CurveSignature
        {
            public string Kind;
            public XYZ Anchor;
        }

        private sealed class ResultDto
        {
            public bool ok;
            public double toleranceFeet;
            public List<DuplicateGroup> duplicates;
            public Dictionary<string, int> skipped;
        }

        private sealed class DuplicateGroup
        {
            public string category;
            public string typeName;
            public int count;
            public List<int> elementIds;
            public PointDto location;
            public string mode;
        }

        private sealed class PointDto
        {
            public double x, y, z;
            public PointDto() { }
            public PointDto(XYZ p) { x = p.X; y = p.Y; z = p.Z; }
        }

        #endregion
    }

    internal static class RevitLogger
    {
        public static void WriteInfo(string msg) => System.Diagnostics.Debug.WriteLine("[INFO] " + msg);
        public static void WriteError(string msg) => System.Diagnostics.Debug.WriteLine("[ERROR] " + msg);
    }

    internal static class RpcReturnChannel
    {
        private static string _last;
        public static void Set(string json) { _last = json; }
        public static string Consume() { var s = _last; _last = null; return s; }
    }
}

