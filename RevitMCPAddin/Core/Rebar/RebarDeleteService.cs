#nullable enable
// ================================================================
// File   : Core/Rebar/RebarDeleteService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Safe-ish delete helpers for tool-generated rebars in a host.
// Notes  :
//  - Deletion is limited to rebars "in host" (RebarHostData) AND tagged.
//  - Tag check is based on instance Comments by default (best-effort).
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Core.Rebar
{
    internal static class RebarDeleteService
    {
        private static IEnumerable<Autodesk.Revit.DB.Structure.Rebar> EnumerateRebarsInHost(Document doc, Element host)
        {
            var list = new List<Autodesk.Revit.DB.Structure.Rebar>();
            if (doc == null || host == null) return list;

            bool validHost = false;
            try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
            if (!validHost) return list;

            RebarHostData? rhd = null;
            try { rhd = RebarHostData.GetRebarHostData(host); } catch { rhd = null; }
            if (rhd == null) return list;

            // Revit API signature differs by version: GetRebarsInHost() may return ElementIds or Rebar objects.
            IEnumerable? items = null;
            try { items = rhd.GetRebarsInHost() as IEnumerable; } catch { items = null; }
            if (items == null) return list;

            foreach (var it in items)
            {
                if (it == null) continue;

                Autodesk.Revit.DB.Structure.Rebar? rebar = null;
                try
                {
                    rebar = it as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null)
                    {
                        var id = it as ElementId;
                        if (id != null)
                            rebar = doc.GetElement(id) as Autodesk.Revit.DB.Structure.Rebar;
                    }
                }
                catch { rebar = null; }

                if (rebar != null) list.Add(rebar);
            }

            return list;
        }

        public static IList<int> CollectAllRebarIdsInHost(Document doc, Element host)
        {
            var list = new List<int>();
            foreach (var rebar in EnumerateRebarsInHost(doc, host))
            {
                try
                {
                    int v = rebar.Id.IntValue();
                    if (v > 0) list.Add(v);
                }
                catch { /* ignore */ }
            }
            return list.Distinct().OrderBy(x => x).ToList();
        }

        public static IList<int> CollectTaggedRebarIdsInHost(Document doc, Element host, string tag)
        {
            var list = new List<int>();
            if (doc == null || host == null) return list;

            var needle = (tag ?? string.Empty).Trim();
            if (needle.Length == 0) return list;

            foreach (var rebar in EnumerateRebarsInHost(doc, host))
            {
                if (rebar == null) continue;

                string comments = string.Empty;
                try
                {
                    var p = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    comments = p != null ? (p.AsString() ?? string.Empty) : string.Empty;
                }
                catch { comments = string.Empty; }

                if (comments.Length == 0) continue;

                if (comments.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try
                    {
                        int v = rebar.Id.IntValue();
                        if (v > 0) list.Add(v);
                    }
                    catch { /* ignore */ }
                }
            }

            return list.Distinct().OrderBy(x => x).ToList();
        }

        public static IList<int> CollectUntaggedRebarIdsInHost(Document doc, Element host, string tag)
        {
            var list = new List<int>();
            if (doc == null || host == null) return list;

            var needle = (tag ?? string.Empty).Trim();
            if (needle.Length == 0) return list; // no tag => do not classify as untagged

            foreach (var rebar in EnumerateRebarsInHost(doc, host))
            {
                if (rebar == null) continue;

                string comments = string.Empty;
                try
                {
                    var p = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    comments = p != null ? (p.AsString() ?? string.Empty) : string.Empty;
                }
                catch { comments = string.Empty; }

                if (comments.Length == 0 || comments.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    try
                    {
                        int v = rebar.Id.IntValue();
                        if (v > 0) list.Add(v);
                    }
                    catch { /* ignore */ }
                }
            }

            return list.Distinct().OrderBy(x => x).ToList();
        }

        public static IList<int> DeleteElementsByIds(Document doc, IEnumerable<int> elementIds)
        {
            var deleted = new List<int>();
            if (doc == null || elementIds == null) return deleted;

            var ids = new List<ElementId>();
            foreach (var i in elementIds.Distinct())
            {
                if (i <= 0) continue;
                ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(i));
            }
            if (ids.Count == 0) return deleted;

            try
            {
                var result = doc.Delete(ids);
                if (result != null)
                {
                    foreach (var rid in result)
                    {
                        try
                        {
                            int v = rid.IntValue();
                            if (v > 0) deleted.Add(v);
                        }
                        catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                // fall back to best-effort per-id deletion
                foreach (var id in ids)
                {
                    try
                    {
                        var result = doc.Delete(id);
                        if (result == null) continue;
                        foreach (var rid in result)
                        {
                            try
                            {
                                int v = rid.IntValue();
                                if (v > 0) deleted.Add(v);
                            }
                            catch { /* ignore */ }
                        }
                    }
                    catch { /* ignore */ }
                }
            }

            return deleted.Distinct().OrderBy(x => x).ToList();
        }
    }
}
