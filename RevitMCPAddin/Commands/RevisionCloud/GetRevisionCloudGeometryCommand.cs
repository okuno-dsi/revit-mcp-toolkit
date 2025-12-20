// ================================================================
// File: Commands/RevisionCloud/GetRevisionCloudGeometryCommand.cs
// Purpose: Return revision cloud geometry as loops of segments in mm
// Target: .NET Framework 4.8 / C# 7.3 / Revit 2023+
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class GetRevisionCloudGeometryCommand : IRevitCommandHandler
    {
        public string CommandName { get { return "get_revision_cloud_geometry"; } }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            // target resolve
            Element elem = null;
            JToken tok;
            if (p.TryGetValue("elementId", out tok))
                elem = doc.GetElement(new ElementId(tok.Value<int>()));
            else if (p.TryGetValue("uniqueId", out tok))
                elem = doc.GetElement(tok.Value<string>());

            var rc = elem as Autodesk.Revit.DB.RevisionCloud;
            if (rc == null) return new { ok = false, msg = "Element is not a RevisionCloud." };

            bool includeCurveType = p.Value<bool?>("includeCurveType") ?? true;
            double tolFt = 1e-6;                 // 幾何判定用(内部単位 ft)
            double joinTolMm = 0.5;              // 端点結合の mm 許容
            double joinTolFt = ConvertToInternalUnits(joinTolMm, UnitTypeId.Millimeters);

            // --- 1) Geometry 抽出（カーブ群）
            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };
            var ge = rc.get_Geometry(opts);
            var curves = new List<Curve>();

            Action<GeometryElement> harvest = null;
            harvest = (GeometryElement gEl) =>
            {
                foreach (var go in gEl)
                {
                    var c = go as Curve;
                    if (c != null)
                    {
                        // 0長さカーブは除外
                        if (c.ApproximateLength > tolFt) curves.Add(c);
                        continue;
                    }
                    var gi = go as GeometryInstance;
                    if (gi != null)
                    {
                        var ig = gi.GetInstanceGeometry();
                        if (ig != null) harvest(ig);
                    }
                }
            };
            harvest(ge);

            if (curves.Count == 0)
                return new { ok = false, msg = "No curve geometry found in RevisionCloud." };

            // --- 2) 端点グラフ化 → ループ再構成
            // ノード: XYZ（端点） を丸め／距離閾値でマージ
            var nodes = new List<XYZ>();
            Func<XYZ, XYZ> snap = pt =>
            {
                // 既存ノードの近傍を探してスナップ
                foreach (var n in nodes)
                    if (pt.DistanceTo(n) <= joinTolFt) return n;
                nodes.Add(pt);
                return pt;
            };

            var segs = new List<(XYZ a, XYZ b, Curve c, string kind)>();
            foreach (var c in curves)
            {
                var l = c as Line;
                var a = c as Arc;
                XYZ p0 = c.GetEndPoint(0);
                XYZ p1 = c.GetEndPoint(1);
                var s0 = snap(p0);
                var s1 = snap(p1);
                var kind = (l != null) ? "Line" : (a != null) ? "Arc" : c.GetType().Name;
                segs.Add((s0, s1, c, kind));
            }

            // 隣接辞書
            var adj = new Dictionary<XYZ, List<(XYZ to, Curve c, string kind)>>(new XyzEq(joinTolFt));
            Action<XYZ> addNode = n =>
            {
                if (!adj.ContainsKey(n)) adj[n] = new List<(XYZ, Curve, string)>();
            };
            foreach (var s in segs)
            {
                addNode(s.a); addNode(s.b);
                adj[s.a].Add((s.b, s.c, s.kind));
                adj[s.b].Add((s.a, s.c, s.kind));
            }

            // 未訪問セグメント集合
            var unused = new HashSet<Curve>(segs.Select(x => x.c));
            var loops = new List<List<(Curve c, string kind)>>();

            foreach (var s in segs)
            {
                if (!unused.Contains(s.c)) continue;

                // 端点から“なるべく連続”に辿って閉路を構成
                var path = new List<(Curve, string)>();
                var current = s.a;
                var target = s.b;
                Curve currentCurve = s.c;
                path.Add((currentCurve, s.kind));
                unused.Remove(currentCurve);

                // forward
                var curEnd = target;
                while (true)
                {
                    var next = adj[curEnd]
                        .FirstOrDefault(e => unused.Contains(e.c));
                    if (next.c == null) break;
                    path.Add((next.c, next.kind));
                    unused.Remove(next.c);
                    // 進行
                    // 次のセグメントのもう一方端点へ
                    var n0 = next.to;
                    // 相手側端点を求める
                    var endPoints = new[] { next.c.GetEndPoint(0), next.c.GetEndPoint(1) };
                    var other = (endPoints[0].IsAlmostEqualTo(n0) ? endPoints[1] : endPoints[0]);
                    // 現ノードを更新
                    curEnd = other;
                    // ループ判定
                    if (curEnd.DistanceTo(s.a) <= joinTolFt) break;
                }

                // ループとして追加（閉じていないこともあり得るが、そのまま返す）
                loops.Add(path);
            }

            // --- 3) 出力整形（mm）
            var outLoops = new List<object>();
            foreach (var loop in loops)
            {
                var segments = new List<object>();
                foreach (var item in loop)
                {
                    var c = item.c;
                    var line = c as Line;
                    var arc = c as Arc;
                    if (line != null)
                    {
                        segments.Add(new
                        {
                            type = includeCurveType ? "Line" : null,
                            start = PtMm(line.GetEndPoint(0)),
                            end = PtMm(line.GetEndPoint(1)),
                            lengthMm = Math.Round(ConvertFromInternalUnits(line.ApproximateLength, UnitTypeId.Millimeters), 3)
                        });
                    }
                    else if (arc != null)
                    {
                        // 角度は中心+端点から算出
                        var center = arc.Center;
                        var s = arc.GetEndPoint(0);
                        var e = arc.GetEndPoint(1);

                        // 2D 平面前提：Z はビュー平面に依存するが、ここでは XY 投影角で十分
                        Func<XYZ, double> ang = p => Math.Atan2(p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI;
                        double startDeg = ang(s);
                        double endDeg = ang(e);

                        segments.Add(new
                        {
                            type = includeCurveType ? "Arc" : null,
                            start = PtMm(s),
                            end = PtMm(e),
                            center = PtMm(center),
                            radiusMm = Math.Round(ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters), 3),
                            startAngleDeg = Math.Round(startDeg, 6),
                            endAngleDeg = Math.Round(endDeg, 6),
                            lengthMm = Math.Round(ConvertFromInternalUnits(arc.ApproximateLength, UnitTypeId.Millimeters), 3)
                        });
                    }
                    else
                    {
                        // NURBS等はテッセレーションで折れ線化しておく
                        var tess = c.Tessellate();
                        for (int i = 0; i + 1 < tess.Count; i++)
                        {
                            segments.Add(new
                            {
                                type = includeCurveType ? "Poly" : null,
                                start = PtMm(tess[i]),
                                end = PtMm(tess[i + 1]),
                                lengthMm = Math.Round(ConvertFromInternalUnits(tess[i].DistanceTo(tess[i + 1]), UnitTypeId.Millimeters), 3)
                            });
                        }
                    }
                }
                if (segments.Count > 0)
                    outLoops.Add(new { segments });
            }

            return new
            {
                ok = true,
                elementId = rc.Id.IntegerValue,
                totalLoops = outLoops.Count,
                loops = outLoops,
                units = new { Length = "mm" }
            };
        }

        // ---- helpers ----
        private static object PtMm(XYZ p) => new
        {
            x = Math.Round(ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters), 3),
            y = Math.Round(ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters), 3),
            z = Math.Round(ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters), 3)
        };

        // XYZ 同値比較（許容距離）
        private sealed class XyzEq : IEqualityComparer<XYZ>
        {
            private readonly double _tol;
            public XyzEq(double tolFt) { _tol = tolFt; }
            public bool Equals(XYZ a, XYZ b)
            {
                if (ReferenceEquals(a, b)) return true;
                if ((object)a == null || (object)b == null) return false;
                return a.DistanceTo(b) <= _tol;
            }
            public int GetHashCode(XYZ p)
            {
                // 許容誤差内で丸めた座標のハッシュに近似
                double s = 1.0 / _tol;
                int hx = (int)Math.Round(p.X * s);
                int hy = (int)Math.Round(p.Y * s);
                int hz = (int)Math.Round(p.Z * s);
                unchecked { return (hx * 397) ^ (hy * 97) ^ hz; }
            }
        }
    }
}
