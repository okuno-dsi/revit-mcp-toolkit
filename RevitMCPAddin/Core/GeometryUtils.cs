// ================================================================
// File: RevitMCPAddin/Core/GeometryUtils.cs
// Target : .NET Framework 4.8 / C# 8
// Purpose: 2D/3D 線分・点の幾何判定（平行, 角度, 交点, 距離, 重なり）
// Policy : 既定単位は mm 前提（コマンドの入口で mm をそのまま渡す）
// Notes  : Revit API 非依存（純C#）。Tolerance は mm/deg ベース。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitMCPAddin.Core
{
    public static class GeometryUtils
    {
        // ------------ 基本型 ------------
        public struct Vec2 { public double X, Y; public Vec2(double x, double y) { X = x; Y = y; } }
        public struct Vec3 { public double X, Y, Z; public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; } }

        public struct Segment2
        {
            public Vec2 A, B;
            public Segment2(Vec2 a, Vec2 b) { A = a; B = b; }
            public Vec2 Dir => new Vec2(B.X - A.X, B.Y - A.Y);
            public double Length => Math.Sqrt((B.X - A.X) * (B.X - A.X) + (B.Y - A.Y) * (B.Y - A.Y));
        }

        public struct Segment3
        {
            public Vec3 A, B;
            public Segment3(Vec3 a, Vec3 b) { A = a; B = b; }
            public Vec3 Dir => new Vec3(B.X - A.X, B.Y - A.Y, B.Z - A.Z);
            public double Length => Math.Sqrt((B.X - A.X) * (B.X - A.X) + (B.Y - A.Y) * (B.Y - A.Y) + (B.Z - A.Z) * (B.Z - A.Z));
        }

        public struct Tolerance
        {
            public double DistMm;     // 位置判定の許容差
            public double AngleDeg;   // 角度判定の許容差
            public Tolerance(double distMm = 0.1, double angleDeg = 1e-4) { DistMm = distMm; AngleDeg = angleDeg; }
        }

        // ------------ 内部ヘルパ ------------
        static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vec2 Sub(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        static double CrossZ(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X; // 2D外積（Z成分）
        static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static double Norm(Vec2 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
        static double Norm(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        static double Clamp(double x, double lo, double hi) => (x < lo ? lo : (x > hi ? hi : x));
        static double RadToDeg(double r) => r * 180.0 / Math.PI;

        // ========== 2D: 平行/角度/交点/距離/重なり ==========

        public class Line2DAnalysis
        {
            public bool ok = true;
            public string? msg;

            public bool isParallel;
            public bool isColinear;

            public double angleDeg; // 0..180
            public bool intersectionExists;
            public (double x, double y)? intersection; // 交点（有限線分が交わる場合）

            public double? distanceBetweenParallelMm; // 平行時の距離

            public bool overlapExists; // 同一直線上 & 投影が重なる
            public double? overlapLengthMm;
            public (double x, double y)? overlapStart;
            public (double x, double y)? overlapEnd;
        }

        /// <summary>
        /// 2D 線分同士の総合解析（mm単位）
        /// </summary>
        public static Line2DAnalysis AnalyzeSegments2D(Segment2 s1, Segment2 s2, Tolerance tol)
        {
            var res = new Line2DAnalysis();

            // 方向ベクトル
            var v1 = s1.Dir; var v2 = s2.Dir;
            var n1 = Norm(v1); var n2 = Norm(v2);
            if (n1 < tol.DistMm || n2 < tol.DistMm)
            {
                res.ok = false; res.msg = "線分長が短すぎます。"; return res;
            }

            // 角度
            var cos = Clamp(Dot(new Vec2(v1.X / n1, v1.Y / n1), new Vec2(v2.X / n2, v2.Y / n2)), -1, 1);
            var angle = RadToDeg(Math.Acos(Math.Abs(cos))); // 方向は無向扱い（平行判定としては小さいほど平行）
            res.angleDeg = angle;
            res.isParallel = angle <= tol.AngleDeg;

            // 平行/同一直線（コリニア）チェック
            // s2.A が s1 に乗るか： ( (s2.A - s1.A) と v1 の外積Z )
            var cross = Math.Abs(CrossZ(Sub(s2.A, s1.A), v1));
            res.isColinear = res.isParallel && cross <= tol.DistMm * (1.0 + n1 * 1e-12);

            if (res.isParallel)
            {
                // 平行なら最短距離（点と直線距離）= 面積/底辺
                // 距離 = | (s2.A - s1.A) × v1 | / |v1|
                res.distanceBetweenParallelMm = Math.Abs(CrossZ(Sub(s2.A, s1.A), v1)) / n1;

                // コリニアならオーバーラップ
                if (res.isColinear)
                {
                    // s1 の軸に射影して1次元区間の重なりを計算
                    var e = new Vec2(v1.X / n1, v1.Y / n1); // 単位方向
                    double p1 = Dot(Sub(s1.A, s1.A), e), p2 = Dot(Sub(s1.B, s1.A), e);
                    double q1 = Dot(Sub(s2.A, s1.A), e), q2 = Dot(Sub(s2.B, s1.A), e);
                    if (p2 < p1) { var t = p1; p1 = p2; p2 = t; }
                    if (q2 < q1) { var t = q1; q1 = q2; q2 = t; }

                    double lo = Math.Max(p1, q1);
                    double hi = Math.Min(p2, q2);

                    res.overlapExists = hi >= lo - tol.DistMm;
                    if (res.overlapExists)
                    {
                        double len = Math.Max(0.0, hi - lo);
                        res.overlapLengthMm = len;
                        var os = new Vec2(s1.A.X + e.X * lo, s1.A.Y + e.Y * lo);
                        var oe = new Vec2(s1.A.X + e.X * hi, s1.A.Y + e.Y * hi);
                        res.overlapStart = (os.X, os.Y);
                        res.overlapEnd = (oe.X, oe.Y);
                    }
                }
                // 平行では交点は存在しない（コリニアで区間重なりは別途判定済み）
                res.intersectionExists = false;
                res.intersection = null;
                return res;
            }

            // 平行でない → 交点を計算（有限線分判定込み）
            // 連立: s1.A + t*v1 = s2.A + u*v2
            // 2x2 を外積で解く
            double denom = CrossZ(v1, v2);
            if (Math.Abs(denom) < 1e-18)
            {
                res.intersectionExists = false;
                return res;
            }
            var r = Sub(s2.A, s1.A);
            double tParam = CrossZ(r, v2) / denom;
            double uParam = CrossZ(r, v1) / denom;

            // 有限線分内？
            bool onS1 = tParam >= -tol.DistMm / n1 && tParam <= 1.0 + tol.DistMm / n1;
            bool onS2 = uParam >= -tol.DistMm / n2 && uParam <= 1.0 + tol.DistMm / n2;
            res.intersectionExists = onS1 && onS2;

            if (res.intersectionExists)
            {
                var p = new Vec2(s1.A.X + tParam * v1.X, s1.A.Y + tParam * v1.Y);
                res.intersection = (p.X, p.Y);
            }
            return res;
        }

        // ========== 3D: 角度/交差 or 最短距離（スキュー） ==========

        public class Line3DAnalysis
        {
            public bool ok = true;
            public string? msg;

            public double angleDeg; // 0..180 （方向ベクトル間）
            public bool isParallel;
            public bool isColinear; // 厳密同一直線上（近似判定）

            public bool intersects; // 真の交差（最短距離~0）
            public (double x, double y, double z)? intersection;

            public double shortestDistanceMm;
            public (double x, double y, double z) closestOn1;
            public (double x, double y, double z) closestOn2;
        }

        /// <summary>
        /// 3D 線分同士（無限直線ベース）の総合解析（mm単位）
        /// </summary>
        public static Line3DAnalysis AnalyzeSegments3D(Segment3 s1, Segment3 s2, Tolerance tol)
        {
            var res = new Line3DAnalysis();

            var d1 = s1.Dir; var d2 = s2.Dir;
            var n1 = Norm(d1); var n2 = Norm(d2);
            if (n1 < tol.DistMm || n2 < tol.DistMm)
            { res.ok = false; res.msg = "線分長が短すぎます。"; return res; }

            var u1 = new Vec3(d1.X / n1, d1.Y / n1, d1.Z / n1);
            var u2 = new Vec3(d2.X / n2, d2.Y / n2, d2.Z / n2);

            // 角度
            var cos = Clamp(Dot(u1, u2), -1, 1);
            res.angleDeg = RadToDeg(Math.Acos(Math.Abs(cos)));
            res.isParallel = res.angleDeg <= tol.AngleDeg;

            // 最近接点（無限直線）を求める
            // 参考: 最短距離の一般式 (線分パラメータ s,t を解く)
            var w0 = Sub(s1.A, s2.A);
            double a = Dot(u1, u1);          // =1
            double b = Dot(u1, u2);
            double c = Dot(u2, u2);          // =1
            double d = Dot(u1, w0);
            double e = Dot(u2, w0);
            double denom = a * c - b * b;       // = 1 - b^2

            double sParam, tParam;
            if (Math.Abs(denom) < 1e-12)
            {
                // ほぼ平行
                sParam = 0.0;
                tParam = e; // 任意。次で最短点を出すため補正。
            }
            else
            {
                sParam = (b * e - c * d) / denom;
                tParam = (a * e - b * d) / denom;
            }

            // 無限直線上の最近接点
            var p1 = new Vec3(s1.A.X + sParam * u1.X, s1.A.Y + sParam * u1.Y, s1.A.Z + sParam * u1.Z);
            var p2 = new Vec3(s2.A.X + tParam * u2.X, s2.A.Y + tParam * u2.Y, s2.A.Z + tParam * u2.Z);
            var dp = Sub(p1, p2);
            res.shortestDistanceMm = Norm(dp);

            // 同一直線（コリニア）目安：平行 かつ s2.A が s1 線上に近い（距離~0）
            res.isColinear = res.isParallel && DistancePointToLine3D(s2.A, s1.A, u1) <= tol.DistMm;

            // 有限「線分」として交差するかは、上の最近接点がそれぞれ [A,B] 範囲内かどうかも見る
            bool onS1 = IsParamWithinSegment3D(s1, p1, tol);
            bool onS2 = IsParamWithinSegment3D(s2, p2, tol);
            res.intersects = res.shortestDistanceMm <= tol.DistMm && onS1 && onS2;

            if (res.intersects)
                res.intersection = ((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5, (p1.Z + p2.Z) * 0.5);

            res.closestOn1 = (p1.X, p1.Y, p1.Z);
            res.closestOn2 = (p2.X, p2.Y, p2.Z);
            return res;
        }

        static double DistancePointToLine3D(Vec3 P, Vec3 A, Vec3 u) // uは単位方向
            => Norm(Cross(Sub(P, A), u)); // |(P-A)×u| = 線からの距離

        static bool IsParamWithinSegment3D(Segment3 s, Vec3 P, Tolerance tol)
        {
            var v = s.Dir;
            var len2 = Dot(v, v);
            if (len2 < 1e-18) return false;
            var t = Dot(Sub(P, s.A), v) / len2; // 0..1 で線分内
            return t >= -tol.DistMm / Math.Sqrt(len2) && t <= 1.0 + tol.DistMm / Math.Sqrt(len2);
        }

        // ========== 点と線分（2D/3D）関係 ==========

        public class PointLineReport2D
        {
            public bool ok = true;
            public string? msg;

            public double distanceMm;
            public (double x, double y) projection;
            public double t;           // 0..1 で線分内
            public bool onSegment;
        }

        public static PointLineReport2D AnalyzePointToSegment2D(Vec2 p, Segment2 s, Tolerance tol)
        {
            var v = s.Dir;
            var len2 = v.X * v.X + v.Y * v.Y;
            if (len2 < 1e-18) return new PointLineReport2D { ok = false, msg = "線分長が短すぎます。" };

            var ap = Sub(p, s.A);
            var t = Dot(ap, v) / len2;
            var tClamped = Math.Max(0.0, Math.Min(1.0, t));
            var proj = new Vec2(s.A.X + v.X * tClamped, s.A.Y + v.Y * tClamped);
            var dist = Norm(Sub(p, proj));

            return new PointLineReport2D
            {
                distanceMm = dist,
                projection = (proj.X, proj.Y),
                t = tClamped,
                onSegment = dist <= tol.DistMm
            };
        }

        public class PointLineReport3D
        {
            public bool ok = true;
            public string? msg;

            public double distanceMm;
            public (double x, double y, double z) projection;
            public double t;           // 0..1 で線分内
            public bool onSegment;
        }

        public static PointLineReport3D AnalyzePointToSegment3D(Vec3 p, Segment3 s, Tolerance tol)
        {
            var v = s.Dir;
            var len2 = Dot(v, v);
            if (len2 < 1e-18) return new PointLineReport3D { ok = false, msg = "線分長が短すぎます。" };

            var ap = Sub(p, s.A);
            var t = Dot(ap, v) / len2;
            var tClamped = Math.Max(0.0, Math.Min(1.0, t));
            var proj = new Vec3(s.A.X + v.X * tClamped, s.A.Y + v.Y * tClamped, s.A.Z + v.Z * tClamped);
            var dist = Norm(Sub(p, proj));

            return new PointLineReport3D
            {
                distanceMm = dist,
                projection = (proj.X, proj.Y, proj.Z),
                t = tClamped,
                onSegment = dist <= tol.DistMm
            };
        }

        // ========== 点と点 ==========

        public static double Distance2D(Vec2 a, Vec2 b) => Norm(Sub(a, b));
        public static double Distance3D(Vec3 a, Vec3 b) => Norm(Sub(a, b));
    }
}
