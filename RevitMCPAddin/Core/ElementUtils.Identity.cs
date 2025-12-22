using Autodesk.Revit.DB;
using RevitMCP.Abstractions.Models;

namespace RevitMCPAddin.Core
{
    public static class ElementUtils
    {
        public static ElementIdentity? BuildIdentity(Document doc, string uniqueId)
        {
            var e = doc.GetElement(uniqueId);
            if (e == null) return null;

            var cat = e.Category?.Name ?? "";
            var fam = (e as FamilyInstance)?.Symbol?.Family?.Name
                      ?? (e as ElementType)?.FamilyName ?? "";
            var typ = (e as FamilyInstance)?.Symbol?.Name
                      ?? (e as ElementType)?.Name ?? e.Name;
            var lvl = (e as FamilyInstance)?.LevelId is ElementId lid && lid.IntValue() > 0
                        ? doc.GetElement(lid)?.Name
                        : (e.LevelId != ElementId.InvalidElementId ? doc.GetElement(e.LevelId)?.Name : null);

            return new ElementIdentity
            {
                UniqueId = e.UniqueId,
                ElementId = e.Id.IntValue(),
                Category = cat,
                FamilyName = fam,
                TypeName = typ,
                LevelName = lvl,
                IsPinned = (e as Element)?.Pinned,
                DocumentPath = GetDocumentPathDisplay(doc),
                DocumentKind = GetDocumentKind(doc)
            };
        }

        private static string GetDocumentPathDisplay(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                {
                    var mp = doc.GetWorksharingCentralModelPath();
                    if (mp != null) return ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                }
                if (!string.IsNullOrEmpty(doc.PathName)) return doc.PathName;
            }
            catch { }
            return "(Unsaved Document)";
        }

        private static string GetDocumentKind(Document doc)
        {
            try
            {
                if (doc.IsModelInCloud) return "Cloud";
                if (doc.IsWorkshared) return "Workshared";
                return "Local";
            }
            catch { return "Unknown"; }
        }
    }
}

