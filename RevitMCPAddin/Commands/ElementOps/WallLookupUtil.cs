// ================================================================
// File: RevitMCPAddin/Commands/ElementOps/WallLookupUtil.cs
// 壁要素の解決 (elementId / wallId / uniqueId 両対応)
// ================================================================
using Autodesk.Revit.DB;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    internal static class WallLookupUtil
    {
        internal static bool TryGetWall(
            Document doc,
            RequestCommand cmd,
            out Autodesk.Revit.DB.Wall wall,
            out int wallId,
            out string uniqueId,
            out string err)
        {
            wall = null; wallId = 0; uniqueId = string.Empty; err = null;
            if (doc == null) { err = "アクティブドキュメントがありません。"; return false; }

            var p = cmd.Params;

            // elementId / wallId 優先
            int id = p.Value<int?>("elementId") ?? p.Value<int?>("wallId") ?? 0;
            if (id > 0)
            {
                var e = doc.GetElement(new ElementId(id)) as Autodesk.Revit.DB.Wall;
                if (e != null)
                {
                    wall = e; wallId = e.Id.IntegerValue; uniqueId = e.UniqueId ?? string.Empty;
                    return true;
                }
            }

            // uniqueId でも解決
            var uid = p.Value<string>("uniqueId");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var e = doc.GetElement(uid) as Autodesk.Revit.DB.Wall;
                if (e != null)
                {
                    wall = e; wallId = e.Id.IntegerValue; uniqueId = e.UniqueId ?? string.Empty;
                    return true;
                }
            }

            err = "Wall element not found. Provide wallId/elementId or uniqueId.";
            return false;
        }
    }
}
