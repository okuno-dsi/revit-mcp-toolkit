using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// 指定要素のジオメトリから PlanarFace を検出し、
    /// フェイス上にファミリインスタンスを配置するユーティリティクラス
    /// また、壁の元仕上げ面積およびペイント面積を取得する機能を提供します。
    /// </summary>
    public static class FaceHostHelper
    {
        /// <summary>
        /// 指定要素のジオメトリからすべての PlanarFace を取得する
        /// </summary>
        public static IList<PlanarFace> GetPlanarFaces(Element elem)
        {
            var opts = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geomElem = elem.get_Geometry(opts);
            var faces = new List<PlanarFace>();
            CollectFacesRecursively(geomElem, faces);
            return faces;
        }

        /// <summary>
        /// GeometryElement 内を再帰的に探索し、PlanarFace を収集する
        /// </summary>
        private static void CollectFacesRecursively(GeometryElement geomElem, List<PlanarFace> faces)
        {
            foreach (GeometryObject obj in geomElem)
            {
                switch (obj)
                {
                    case GeometryInstance gi:
                        CollectFacesRecursively(gi.GetInstanceGeometry(), faces);
                        break;
                    case Solid solid when solid.Faces.Size > 0:
                        foreach (Face face in solid.Faces)
                        {
                            if (face is PlanarFace pf)
                                faces.Add(pf);
                        }
                        break;
                    case GeometryElement nested:
                        CollectFacesRecursively(nested, faces);
                        break;
                }
            }
        }

        /// <summary>
        /// 指定された PlanarFace 上にファミリインスタンスを配置する
        /// </summary>
        public static FamilyInstance CreateOnFace(
            Document doc,
            PlanarFace face,
            XYZ insertionPoint,
            FamilySymbol symbol)
        {
            Reference reference = face.Reference;
            XYZ refDir = face.XVector.Normalize();
            return doc.Create.NewFamilyInstance(reference, insertionPoint, refDir, symbol);
        }

        /// <summary>
        /// 壁の分割フェイスに施されたペイント面積を合計し、マテリアル名と面積を返す
        /// </summary>
        public static IDictionary<string, double> GetWallPaintData(
            Document doc,
            Wall wall)
        {
            var result = new Dictionary<string, double>();
            var faces = GetPlanarFaces(wall);

            foreach (var face in faces)
            {
                if (!doc.IsPainted(wall.Id, face))
                    continue;

                var regions = face.HasRegions ? face.GetRegions() : new Face[] { face };
                foreach (var region in regions)
                {
                    var matId = region.MaterialElementId;
                    var material = doc.GetElement(matId) as Material;
                    double area = UnitUtils.ConvertFromInternalUnits(region.Area, UnitTypeId.SquareMeters);
                    string name = material?.Name ?? "<Unknown>";
                    if (result.ContainsKey(name)) result[name] += area;
                    else result[name] = area;
                }
            }
            return result;
        }

        /// <summary>
        /// 壁の分割フェイスにおける元の仕上げ面積を合計し、マテリアル名と面積を返す
        /// </summary>
        public static IDictionary<string, double> GetWallOriginalFinishData(
            Document doc,
            Wall wall)
        {
            var result = new Dictionary<string, double>();
            var faces = GetPlanarFaces(wall);

            foreach (var face in faces)
            {
                var regions = face.HasRegions ? face.GetRegions() : new Face[] { face };
                foreach (var region in regions)
                {
                    var matId = region.MaterialElementId;
                    var material = doc.GetElement(matId) as Material;
                    double area = UnitUtils.ConvertFromInternalUnits(region.Area, UnitTypeId.SquareMeters);
                    string name = material?.Name ?? "<Unknown>";
                    if (result.ContainsKey(name)) result[name] += area;
                    else result[name] = area;
                }
            }
            return result;
        }
    }
}
