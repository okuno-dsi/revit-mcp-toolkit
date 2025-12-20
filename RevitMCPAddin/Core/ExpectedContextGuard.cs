#nullable enable
using System;
using System.Text;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Lightweight, opt-in execution guard for cross-port/process safety.
    /// If caller passes __expectPid / __expectProjectTitle / __expectViewId,
    /// this guard validates the active context and returns a standardized error object on mismatch.
    /// Absent parameters are ignored (no-op).
    /// </summary>
    public static class ExpectedContextGuard
    {
        public static object? Validate(UIApplication uiapp, JObject p)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null)
                {
                    return new { ok = false, code = "NO_DOC", msg = "No active document." };
                }

                // __expectPid
                var expectPid = p.Value<int?>("__expectPid");
                if (expectPid.HasValue && expectPid.Value > 0)
                {
                    int curPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    if (curPid != expectPid.Value)
                    {
                        return new
                        {
                            ok = false,
                            code = "EXPECT_PID_MISMATCH",
                            msg = "Process PID mismatch.",
                            details = new { expected = expectPid.Value, actual = curPid }
                        };
                    }
                }

                // Prefer GUID-based project validation when provided
                var expectGuid = p.Value<string>("__expectProjectGuid");
                if (!string.IsNullOrWhiteSpace(expectGuid))
                {
                    string actualGuid = string.Empty;
                    try { actualGuid = doc.ProjectInformation?.UniqueId ?? string.Empty; } catch { }
                    if (!string.Equals((expectGuid ?? string.Empty).Trim(), (actualGuid ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return new
                        {
                            ok = false,
                            code = "EXPECT_PROJECT_GUID_MISMATCH",
                            msg = "Project GUID mismatch.",
                            details = new { expected = expectGuid, actual = actualGuid }
                        };
                    }
                }
                else
                {
                    // Fallback (optional) title-based check only when GUID expectation is not provided
                    var expectTitle = p.Value<string>("__expectProjectTitle");
                    if (!string.IsNullOrWhiteSpace(expectTitle))
                    {
                        string norm(string s)
                        {
                            if (string.IsNullOrEmpty(s)) return string.Empty;
                            var t = s.Trim();
                            return t.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
                        }
                        var exp = norm(expectTitle!);
                        var act = norm(doc.Title ?? "");
                        if (!string.Equals(exp, act, StringComparison.Ordinal))
                        {
                            return new
                            {
                                ok = false,
                                code = "EXPECT_PROJECT_MISMATCH",
                                msg = "Project title mismatch.",
                                details = new { expected = expectTitle, actual = doc.Title }
                            };
                        }
                    }
                }

                // __expectViewId (ensure exists; if also activeOnly == true, check ActiveView)
                var expectViewId = p.Value<int?>("__expectViewId");
                if (expectViewId.HasValue && expectViewId.Value > 0)
                {
                    var v = doc.GetElement(new ElementId(expectViewId.Value)) as View;
                    if (v == null)
                    {
                        return new
                        {
                            ok = false,
                            code = "EXPECT_VIEW_NOT_FOUND",
                            msg = "Expected viewId not found in current document.",
                            details = new { expected = expectViewId.Value }
                        };
                    }

                    bool activeOnly = p.Value<bool?>("__expectViewActive") ?? false;
                    if (activeOnly)
                    {
                        var av = uidoc.ActiveView;
                        if (av == null || av.Id.IntegerValue != expectViewId.Value)
                        {
                            return new
                            {
                                ok = false,
                                code = "EXPECT_VIEW_NOT_ACTIVE",
                                msg = "Expected view is not active.",
                                details = new { expected = expectViewId.Value, activeViewId = av?.Id.IntegerValue }
                            };
                        }
                    }
                }

                return null; // OK
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "EXPECT_GUARD_ERROR", msg = ex.Message };
            }
        }
    }
}
