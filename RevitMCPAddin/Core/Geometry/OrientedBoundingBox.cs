// ================================================================
// File: Core/Geometry/OrientedBoundingBox.cs
// Purpose:
//   - OBB用の最小DTO群（Abstractionsに依存しないローカル定義）
//   - Add-in 単体でビルド可能
// Target: .NET Framework 4.8 / Revit 2023+
// ================================================================
using System.Collections.Generic;

namespace RevitMCPAddin.Core.Geometry
{
    /// <summary>3D座標 (mmやft等、呼び出し側の意味で使用)</summary>
    public sealed class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Point3D() { }
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Oriented Bounding Box の結果DTO（ワールド座標軸ベクトル/中心/半径/コーナー等）</summary>
    public sealed class OrientedBoundingBoxDto
    {
        /// <summary>中心点（world）</summary>
        public Point3D Center { get; set; }

        /// <summary>OBB のローカル基底軸（world単位ベクトル）</summary>
        public Point3D AxisX { get; set; }
        public Point3D AxisY { get; set; }
        public Point3D AxisZ { get; set; }

        /// <summary>各軸方向の半径（extents, internal units=ft）</summary>
        public double ExtentX { get; set; }
        public double ExtentY { get; set; }
        public double ExtentZ { get; set; }

        /// <summary>8コーナー（world）。IncludeCorners=trueのとき出力</summary>
        public List<Point3D> Corners { get; set; } = new List<Point3D>(8);

        /// <summary>体積（参考）= 8*Ex*Ey*Ez（内部単位立方フィート）</summary>
        public double Volume { get; set; }

        /// <summary>基底推定戦略などの注記</summary>
        public string Notes { get; set; }
    }

    /// <summary>OBB取得の要求DTO</summary>
    public sealed class GetObbRequest
    {
        public long ElementId { get; set; }

        /// <summary>auto | family | wall | plane2d | faces | edgesOnly（省略可: "auto"）</summary>
        public string Strategy { get; set; } = "auto";

        /// <summary>coarse | medium | fine（省略可: "fine"）</summary>
        public string DetailLevel { get; set; } = "fine";

        /// <summary>8コーナーを付与するか</summary>
        public bool IncludeCorners { get; set; } = true;
    }

    /// <summary>OBB取得の応答DTO</summary>
    public sealed class ObbResponse
    {
        public bool Ok { get; set; }
        public string Msg { get; set; }
        public OrientedBoundingBoxDto Obb { get; set; }
    }
}
