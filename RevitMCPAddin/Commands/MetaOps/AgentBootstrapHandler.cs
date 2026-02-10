// ================================================================
// File: Commands/MetaOps/AgentBootstrapHandler.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8.0
// Purpose: JSON-RPC "agent_bootstrap" handler (Add-in側実体)
// Notes  : catalog は最小化、後で拡張可能
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Commands.MetaOps
{
    public sealed class AgentBootstrapHandler : IRevitCommandHandler
    {
        public string CommandName { get { return "agent_bootstrap"; } }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var app = uiapp != null ? uiapp.Application : null;
                var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
                var doc = uidoc != null ? uidoc.Document : null;

                // ---- server / project / environment ----
                var server = new
                {
                    product = app != null ? app.VersionName : "Autodesk Revit",
                    process = new { pid = System.Diagnostics.Process.GetCurrentProcess().Id }
                };

                string docKey = null;
                if (doc != null)
                {
                    try
                    {
                        string source;
                        docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                    }
                    catch { /* ignore */ }
                }

                var project = new
                {
                    ok = doc != null,
                    name = doc != null ? doc.Title : null,
                    number = doc != null ? doc.ProjectInformation.Number : null,
                    filePath = doc != null ? doc.PathName : null,
                    revitVersion = app != null ? app.VersionNumber : null,
                    documentGuid = doc != null ? (string.IsNullOrWhiteSpace(docKey) ? doc.ProjectInformation.UniqueId : docKey) : null,
                    message = doc == null ? "No active document." : null
                };

                var environment = new
                {
                    units = new
                    {
                        input = new { Length = "mm", Angle = "deg", Area = "m2", Volume = "m3" },
                        internal_ = new { Length = "ft", Angle = "rad", Area = "ft2", Volume = "ft3" }
                    },
                    activeViewId = uidoc != null && uidoc.ActiveView != null ? uidoc.ActiveView.Id.IntValue() : 0,
                    activeViewName = uidoc != null && uidoc.ActiveView != null ? uidoc.ActiveView.Name : null
                };

                // Unified document context (project + environment) for easier client consumption.
                // NOTE: Keep 'project' and 'environment' as-is for backward compatibility.
                var document = new
                {
                    ok = project.ok,
                    name = project.name,
                    number = project.number,
                    filePath = project.filePath,
                    revitVersion = project.revitVersion,
                    documentGuid = project.documentGuid,
                    activeViewId = environment.activeViewId,
                    activeViewName = environment.activeViewName,
                    units = new
                    {
                        input = environment.units.input,
                        internalUnits = environment.units.internal_
                    }
                };

                // ---- commands (lite) ----
                var hot = new List<string>
                {
                    "get_elements_in_view",
                    "set_category_visibility",
                    "set_visual_override",
                    "get_element_info",
                    "create_wall",
                    "move_element",
                    "update_element",
                    "export_dwg"
                };

                var commands = new
                {
                    count = hot.Count,
                    hot = hot,
                    catalog = new object[0]
                };

                var policies = new
                {
                    idFirst = true,
                    lengthUnit = "mm",
                    angleUnit = "deg",
                    throttle = new { minMsBetweenCalls = 80 }
                };

                var knownErrors = new object[]
                {
                    new { code = "VIEW_TEMPLATE_LOCK", msg = "Operation may be skipped due to view template" },
                    new { code = "READONLY_PARAM",    msg = "Parameter is read-only (check type vs instance)" },
                    new { code = "NO_LOCATION",       msg = "Element has no Location; cannot move" }
                };

                var terminology = RevitMCPAddin.Core.TermMapService.BuildTerminologyContextBlock();

                var response = new
                {
                    ok = true,
                    server = server,
                    project = project,
                    document = document,
                    environment = new
                    {
                        units = new
                        {
                            input = environment.units.input,
                            internalUnits = environment.units.internal_
                        },
                        activeViewId = environment.activeViewId,
                        activeViewName = environment.activeViewName
                    },
                    commands = commands,
                    policies = policies,
                    knownErrors = knownErrors,
                    terminology = terminology,
                    warnings = new string[0]
                };

                try
                {
                    RevitMCPAddin.Core.RevitLogger.AppendInfo("agent_bootstrap ok");
                }
                catch { /* best-effort */ }

                return response;
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    msg = "Unhandled exception in agent_bootstrap.",
                    detail = ex.ToString()
                };
            }
        }
    }
}

