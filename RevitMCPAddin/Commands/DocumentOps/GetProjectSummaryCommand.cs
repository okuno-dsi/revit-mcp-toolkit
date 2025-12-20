#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    public class GetProjectSummaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_summary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var app = uiapp.Application;
            var isWorkshared = doc.IsWorkshared;

            int worksets = 0;
            try {
                if (isWorkshared) worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).Count();
            } catch { }

            int viewCount = 0; try { viewCount = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate); } catch { }
            int categoriesCount = 0; try { categoriesCount = doc.Settings.Categories.Size; } catch { }
            int warnings = 0; try { warnings = doc.GetWarnings()?.Count ?? 0; } catch { }
            bool isCloud = false; try { isCloud = doc.IsModelInCloud; } catch { }
            string unitSystem = string.Empty; try { unitSystem = doc.DisplayUnitSystem.ToString(); } catch { }

            int designOptions = 0;
            try { designOptions = new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).ToElements().Count; } catch { }

            var phases = new List<string>();
            try { phases = doc.Phases?.Cast<Phase>().Select(p => p.Name).ToList() ?? new List<string>(); } catch { }

            return new {
                ok = true,
                projectName = doc.Title,
                revitVersion = $"{app.VersionName} ({app.VersionNumber})",
                docPath = doc.PathName ?? string.Empty,
                isCloudModel = isCloud,
                unitSystem = unitSystem,
                levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Count(),
                views = viewCount,
                categories = categoriesCount,
                phases = phases,
                isWorkshared = isWorkshared,
                worksets = worksets,
                warnings = warnings,
                designOptions = designOptions
            };
        }
    }
}

