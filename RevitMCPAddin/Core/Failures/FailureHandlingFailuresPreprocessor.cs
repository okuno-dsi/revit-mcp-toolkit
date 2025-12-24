#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core.Failures
{
    /// <summary>
    /// Transaction-level failure handler that respects FailureHandlingContext.Mode and the whitelist.
    /// This is used by TxnUtil so existing commands can opt into failureHandling without being rewritten.
    /// </summary>
    internal sealed class FailureHandlingFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            try
            {
                var mode = FailureHandlingContext.Mode;
                if (mode == FailureHandlingMode.Off)
                {
                    // Fallback to legacy behavior: delete warnings.
                    return PreprocessWarningsOnly(a);
                }

                FailureWhitelistIndex whitelist = null;
                try
                {
                    FailureWhitelistIndex tmp;
                    if (FailureWhitelistService.TryGetIndex(out tmp)) whitelist = tmp;
                }
                catch { /* ignore */ }

                bool requestRollback = false;

                var msgs = a.GetFailureMessages();
                if (msgs == null || msgs.Count == 0) return FailureProcessingResult.Continue;

                foreach (FailureMessageAccessor f in msgs)
                {
                    var defId = f.GetFailureDefinitionId();
                    var sev = f.GetSeverity();

                    string action = "none";
                    bool whitelisted = false;
                    string ruleId = string.Empty;
                    FailureWhitelistRule matchedRule = null;

                    if (whitelist != null)
                    {
                        FailureWhitelistRule r;
                        if (whitelist.TryGetRule(defId.Guid, out r) && r != null && r.Enabled)
                        {
                            if ((sev == FailureSeverity.Warning && r.MatchWarning) ||
                                (sev == FailureSeverity.Error && r.MatchError))
                            {
                                whitelisted = true;
                                ruleId = r.Id ?? string.Empty;
                                matchedRule = r;
                            }
                        }
                    }

                    if (mode == FailureHandlingMode.Rollback)
                    {
                        requestRollback = true;
                        action = "rollback";
                    }
                    else if (!whitelisted || matchedRule == null)
                    {
                        requestRollback = true;
                        action = "rollback_unwhitelisted";
                    }
                    else
                    {
                        // Whitelisted: apply selected mode, constrained by rule allowlist.
                        if (sev == FailureSeverity.Warning)
                        {
                            if (mode == FailureHandlingMode.Delete)
                            {
                                if (matchedRule.AllowDeleteWarning)
                                {
                                    try
                                    {
                                        a.DeleteWarning(f);
                                        action = "delete_warning";
                                    }
                                    catch
                                    {
                                        requestRollback = true;
                                        action = "rollback_delete_warning_failed";
                                    }
                                }
                                else
                                {
                                    requestRollback = true;
                                    action = "rollback_delete_not_allowed";
                                }
                            }
                            else if (mode == FailureHandlingMode.Resolve)
                            {
                                bool resolved = false;
                                if (matchedRule.AllowResolve)
                                {
                                    resolved = TryResolveFailureBestEffort(a, f);
                                    action = resolved ? "resolve" : "resolve_failed";
                                }

                                if (!resolved)
                                {
                                    if (matchedRule.AllowDeleteWarning)
                                    {
                                        try
                                        {
                                            a.DeleteWarning(f);
                                            action = action + "|delete_warning";
                                        }
                                        catch
                                        {
                                            requestRollback = true;
                                            action = "rollback_warning_unhandled";
                                        }
                                    }
                                    else
                                    {
                                        requestRollback = true;
                                        action = "rollback_warning_unhandled";
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Error / others
                            if (mode == FailureHandlingMode.Resolve && matchedRule.AllowResolve)
                            {
                                bool resolved = TryResolveFailureBestEffort(a, f);
                                if (resolved)
                                {
                                    action = "resolve";
                                }
                                else
                                {
                                    requestRollback = true;
                                    action = "rollback_error_resolve_failed";
                                }
                            }
                            else
                            {
                                requestRollback = true;
                                action = "rollback_error";
                            }
                        }
                    }

                    // Capture into context issues (agent-friendly).
                    try
                    {
                        bool includeDesc = (whitelist == null || whitelist.IncludeDescriptionText);
                        if (!includeDesc)
                        {
                            // Always record details when rollback is involved (for debugging and auditability).
                            if (!string.IsNullOrWhiteSpace(action) && action.StartsWith("rollback", StringComparison.OrdinalIgnoreCase))
                                includeDesc = true;
                            else if (sev == FailureSeverity.Error)
                                includeDesc = true;
                        }
                        var msg = includeDesc ? f.GetDescriptionText() : string.Empty;

                        int[] ids = new int[0];
                        if (whitelist == null || whitelist.IncludeElements)
                        {
                            try
                            {
                                var failing = f.GetFailingElementIds();
                                if (failing != null)
                                {
                                    var max = whitelist != null ? whitelist.MaxElementIdsPerFailure : 50;
                                    if (max > 0) ids = failing.Take(max).Select(x => x.IntValue()).ToArray();
                                }
                            }
                            catch { /* ignore */ }
                        }

                        FailureHandlingContext.Issues.failures.Add(new FailureRecord
                        {
                            id = defId.ToString(),
                            idGuid = defId.Guid.ToString(),
                            severity = sev.ToString(),
                            message = msg,
                            elementIds = ids,
                            action = action,
                            whitelisted = whitelisted,
                            ruleId = ruleId
                        });
                    }
                    catch { /* ignore */ }
                }

                if (requestRollback)
                {
                    try
                    {
                        var opts = a.GetFailureHandlingOptions();
                        opts.SetClearAfterRollback(true);
                        a.SetFailureHandlingOptions(opts);
                    }
                    catch { /* ignore */ }

                    try
                    {
                        FailureHandlingContext.Issues.rollbackRequested = true;
                        if (string.IsNullOrWhiteSpace(FailureHandlingContext.Issues.rollbackReason))
                            FailureHandlingContext.Issues.rollbackReason = "IFailuresPreprocessor.ProceedWithRollBack";
                    }
                    catch { /* ignore */ }

                    return FailureProcessingResult.ProceedWithRollBack;
                }

                return FailureProcessingResult.Continue;
            }
            catch
            {
                // As a safety fallback, do not block commits.
                return FailureProcessingResult.Continue;
            }
        }

        private static FailureProcessingResult PreprocessWarningsOnly(FailuresAccessor a)
        {
            try
            {
                var msgs = a.GetFailureMessages();
                foreach (var m in msgs)
                {
                    if (m.GetSeverity() == FailureSeverity.Warning)
                    {
                        a.DeleteWarning(m);
                    }
                }
            }
            catch { /* ignore */ }
            return FailureProcessingResult.Continue;
        }

        private static bool TryResolveFailureBestEffort(FailuresAccessor fa, FailureMessageAccessor f)
        {
            // Reflection-based to reduce API version coupling.
            try
            {
                // If a default resolution type exists, set it.
                try
                {
                    var getDefault = f.GetType().GetMethod("GetDefaultResolutionType", BindingFlags.Public | BindingFlags.Instance);
                    if (getDefault != null)
                    {
                        var def = getDefault.Invoke(f, null);
                        if (def != null)
                        {
                            var setCur = f.GetType().GetMethod("SetCurrentResolutionType", BindingFlags.Public | BindingFlags.Instance);
                            if (setCur != null)
                            {
                                setCur.Invoke(f, new object[] { def });
                            }
                        }
                    }
                }
                catch { /* ignore */ }

                // Try FailuresAccessor.ResolveFailure(FailureMessageAccessor)
                try
                {
                    var m = fa.GetType().GetMethod("ResolveFailure", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(fa, new object[] { f });
                        return true;
                    }
                }
                catch { /* ignore */ }

                // Try FailuresAccessor.ResolveFailures(FailureResolutionType)
                try
                {
                    var getDefault = f.GetType().GetMethod("GetDefaultResolutionType", BindingFlags.Public | BindingFlags.Instance);
                    if (getDefault != null)
                    {
                        var def = getDefault.Invoke(f, null);
                        if (def != null)
                        {
                            var m = fa.GetType().GetMethod("ResolveFailures", BindingFlags.Public | BindingFlags.Instance);
                            if (m != null)
                            {
                                m.Invoke(fa, new object[] { def });
                                return true;
                            }
                        }
                    }
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }

            return false;
        }
    }
}
