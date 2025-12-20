using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Core
{
    public static class PaintHelper
    {
        /// <summary>
        /// 要素のペイント可能な全 Face を取得します。
        /// </summary>
        public static IList<Face> GetPaintableFaces(Element elem)
        {
            var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var geom = elem.get_Geometry(opts);
            return geom
                .OfType<Solid>()
                .SelectMany(s => s.Faces.Cast<Face>())
                .ToList();
        }

        /// <summary>
        /// 指定フェイスにマテリアルを塗装します。
        /// </summary>
        public static void ApplyPaint(Document doc, ElementId elemId, int faceIndex, ElementId matId)
        {
            var elem = doc.GetElement(elemId);
            var faces = GetPaintableFaces(elem);
            if (faceIndex < 0 || faceIndex >= faces.Count)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));

            using (var tx = new Transaction(doc, "Apply Paint"))
            {
                tx.Start();
                doc.Paint(elemId, faces[faceIndex], matId);  // :contentReference[oaicite:0]{index=0}
                tx.Commit();
            }
        }

        /// <summary>
        /// 指定フェイスのペイントを削除します。
        /// </summary>
        public static void RemovePaint(Document doc, ElementId elemId, int faceIndex)
        {
            var elem = doc.GetElement(elemId);
            var faces = GetPaintableFaces(elem);
            if (faceIndex < 0 || faceIndex >= faces.Count)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));

            using (var tx = new Transaction(doc, "Remove Paint"))
            {
                tx.Start();
                doc.RemovePaint(elemId, faces[faceIndex]);   // :contentReference[oaicite:1]{index=1}
                tx.Commit();
            }
        }

        /// <summary>
        /// 現在ペイントされているフェイスのインデックスとマテリアルIDを返します。
        /// </summary>
        public static IList<(int FaceIndex, ElementId MaterialId)> GetPaintInfo(Document doc, ElementId elemId)
        {
            var elem = doc.GetElement(elemId);
            var faces = GetPaintableFaces(elem);
            var result = new List<(int, ElementId)>();
            for (int i = 0; i < faces.Count; i++)
            {
                if (doc.IsPainted(elemId, faces[i]))      // :contentReference[oaicite:2]{index=2}
                {
                    var matId = doc.GetPaintedMaterial(elemId, faces[i]);  // :contentReference[oaicite:3]{index=3}
                    result.Add((i, matId));
                }
            }
            return result;
        }
    }
}
