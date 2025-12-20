// File: RevitMCPAddin/Commands/ElementOps/Paint/GetPaintInfoCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Paint
{
    public class GetPaintInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_paint_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            try
            {
                // 対象要素: elementId / uniqueId 両対応
                Element elem = null;
                int elementId = p.Value<int?>("elementId") ?? 0;
                string uniqueId = p.Value<string>("uniqueId");

                if (elementId > 0) elem = doc.GetElement(new ElementId(elementId));
                else if (!string.IsNullOrWhiteSpace(uniqueId)) elem = doc.GetElement(uniqueId);

                if (elem == null)
                    return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

                bool includeUnpainted = p.Value<bool?>("includeUnpainted") ?? false;

                // ① ペイント可能フェイス列挙
                IList<Autodesk.Revit.DB.Face> faces = PaintHelper.GetPaintableFaces(elem) ?? (IList<Autodesk.Revit.DB.Face>)new List<Autodesk.Revit.DB.Face>();

                // ② 各フェイスの塗装状態
                var list = new List<object>(faces.Count);
                int paintedCount = 0;

                for (int i = 0; i < faces.Count; i++)
                {
                    var face = faces[i];
                    bool isPainted = false;
                    ElementId matId = ElementId.InvalidElementId;
                    string matName = "";

                    try
                    {
                        isPainted = doc.IsPainted(elem.Id, face);
                        if (isPainted)
                        {
                            paintedCount++;
                            matId = doc.GetPaintedMaterial(elem.Id, face);
                            var mat = (matId != null && matId != ElementId.InvalidElementId)
                                        ? doc.GetElement(matId) as Autodesk.Revit.DB.Material
                                        : null;
                            matName = mat?.Name ?? "";
                        }
                    }
                    catch
                    {
                        isPainted = false;
                        matId = ElementId.InvalidElementId;
                        matName = "";
                    }

                    if (!includeUnpainted && !isPainted) continue;

                    string stableRep = "";
                    try { stableRep = face?.Reference?.ConvertToStableRepresentation(doc) ?? ""; } catch { /* ignore */ }

                    list.Add(new
                    {
                        faceIndex = i,
                        isPainted,
                        materialId = (matId != null && matId != ElementId.InvalidElementId) ? (int?)matId.IntegerValue : null,
                        materialName = matName,
                        faceStableReference = stableRep
                    });
                }

                return new
                {
                    ok = true,
                    elementId = elem.Id.IntegerValue,
                    uniqueId = elem.UniqueId,
                    totalFaces = faces.Count,
                    paintedCount,
                    returnedCount = list.Count,
                    paints = list,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = $"get_paint_info 失敗: {ex.Message}" };
            }
        }
    }
}
