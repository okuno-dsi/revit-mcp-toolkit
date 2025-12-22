// RevitMCPAddin/Commands/ScheduleOps/ReorderScheduleFieldsCommand.cs
// Purpose: Reorder existing ViewSchedule fields (column order) without recreating fields.
// Notes:
//  - Prefer ScheduleDefinition.SetFieldOrder via reflection to keep existing ScheduleFieldId stable
//    (so filters/sorting remain valid).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class ReorderScheduleFieldsCommand : IRevitCommandHandler
    {
        public string CommandName => "reorder_schedule_fields";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)(cmd.Params ?? new JObject());

                var doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                    return ResultUtil.Err("No active document.");

                // scheduleViewId is optional when the active view is a schedule.
                int scheduleViewId = p.Value<int?>("scheduleViewId") ?? 0;
                ViewSchedule vs = null;
                if (scheduleViewId > 0)
                {
                    vs = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(scheduleViewId)) as ViewSchedule;
                }
                if (vs == null)
                {
                    vs = uiapp.ActiveUIDocument?.ActiveView as ViewSchedule;
                    if (vs != null) scheduleViewId = vs.Id.IntValue();
                }
                if (vs == null)
                    return ResultUtil.Err("ScheduleView not found (specify scheduleViewId or activate a schedule view).");

                bool includeHidden = p.Value<bool?>("includeHidden") ?? false;
                bool appendUnspecified = p.Value<bool?>("appendUnspecified") ?? true;
                bool strict = p.Value<bool?>("strict") ?? false;

                var def = vs.Definition;
                var current = def.GetFieldOrder().ToList();
                if (current.Count == 0)
                    return ResultUtil.Err("Schedule has no fields to reorder.");

                var infos = new List<FieldInfo>(current.Count);
                for (int i = 0; i < current.Count; i++)
                {
                    var fi = BuildFieldInfo(def, current[i], i);
                    if (fi != null) infos.Add(fi);
                }

                // Fast path: move one visible column before another (Excel-like column letters or 1-based indexes).
                MoveRequest moveReq;
                string moveErr;
                bool hasMove = TryGetMoveRequest(p, out moveReq, out moveErr);
                if (!string.IsNullOrWhiteSpace(moveErr))
                    return ResultUtil.Err(moveErr);

                bool? rfOpt = p.Value<bool?>("returnFields");
                bool returnFields = rfOpt.HasValue ? rfOpt.Value : !hasMove; // default: fast path returns summary only

                List<object> missing = null;
                IList<ScheduleFieldId> newOrder;
                object movedSummary = null;

                if (hasMove)
                {
                    string err;
                    if (!TryBuildMovedOrder(current, infos, moveReq, out newOrder, out movedSummary, out err))
                        return ResultUtil.Err(err);
                }
                else
                {
                    // order: array of strings/ints/objects
                    var specs = ParseOrderSpecs(p);
                    if (specs.Count == 0)
                        return ResultUtil.Err("order is required (array of fieldName/columnHeading/paramId) or specify moveColumn/beforeColumn.");

                    var reorderTargets = includeHidden ? infos : infos.Where(x => !x.Hidden).ToList();
                    var remaining = new HashSet<ScheduleFieldId>(reorderTargets.Select(x => x.FieldId));

                    missing = new List<object>();
                    var orderedTargets = new List<ScheduleFieldId>();
                    foreach (var spec in specs)
                    {
                        var match = FindFirstMatch(reorderTargets, remaining, spec);
                        if (match == null)
                        {
                            missing.Add(spec.ToDebugObject());
                            continue;
                        }
                        remaining.Remove(match);
                        orderedTargets.Add(match);
                    }

                    if (strict && missing.Count > 0)
                    {
                        return ResultUtil.Err(new
                        {
                            code = "SCHEDULE_FIELD_NOT_FOUND",
                            msg = "One or more fields were not found in this schedule.",
                            missing = missing
                        });
                    }

                    if (appendUnspecified)
                    {
                        // Append the remaining fields in their original order.
                        foreach (var fi in reorderTargets)
                        {
                            if (remaining.Contains(fi.FieldId))
                                orderedTargets.Add(fi.FieldId);
                        }
                    }
                    else
                    {
                        if (orderedTargets.Count != reorderTargets.Count)
                        {
                            return ResultUtil.Err(new
                            {
                                code = "SCHEDULE_FIELD_ORDER_INCOMPLETE",
                                msg = "order does not cover all target fields; set appendUnspecified=true or provide full order.",
                                specifiedCount = orderedTargets.Count,
                                totalCount = reorderTargets.Count,
                                missing = missing
                            });
                        }
                    }

                    // Build full field order list.
                    var computed = new List<ScheduleFieldId>(current.Count);
                    computed.AddRange(orderedTargets);
                    if (!includeHidden)
                    {
                        foreach (var fi in infos)
                        {
                            if (fi.Hidden)
                                computed.Add(fi.FieldId);
                        }
                    }

                    newOrder = computed;
                }

                if (!SameFieldSet(current, newOrder))
                {
                    return ResultUtil.Err(new
                    {
                        code = "SCHEDULE_FIELD_ORDER_INVALID",
                        msg = "Computed order is invalid (field set mismatch).",
                        currentCount = current.Count,
                        newCount = newOrder.Count
                    });
                }

                using (var tx = new Transaction(doc, "Reorder Schedule Fields"))
                {
                    tx.Start();
                    string err;
                    if (!TrySetFieldOrder(def, newOrder, out err))
                    {
                        try { tx.RollBack(); } catch { /* ignore */ }
                        return ResultUtil.Err(new
                        {
                            code = "SCHEDULE_FIELD_REORDER_UNSUPPORTED",
                            msg = "Revit API did not accept schedule field reordering in this environment.",
                            detail = err
                        });
                    }
                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    scheduleViewId = scheduleViewId,
                    title = vs.Name,
                    mode = hasMove ? "move" : "order",
                    moved = movedSummary,
                    missing = missing,
                    fieldCount = current.Count,
                    fields = returnFields ? def.GetFieldOrder().ToList().Select((fid, idx) => BuildFieldSummary(def, fid, idx)).ToList() : null,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        private static bool TryGetMoveRequest(JObject p, out MoveRequest req, out string error)
        {
            req = new MoveRequest();
            error = "";

            string moveColumn = p.Value<string>("moveColumn") ?? p.Value<string>("fromColumn");
            string beforeColumn = p.Value<string>("beforeColumn") ?? p.Value<string>("toColumn") ?? p.Value<string>("insertBeforeColumn");
            int? moveIndex = p.Value<int?>("moveIndex") ?? p.Value<int?>("fromIndex");
            int? beforeIndex = p.Value<int?>("beforeIndex") ?? p.Value<int?>("toIndex") ?? p.Value<int?>("insertBeforeIndex");
            bool visibleOnly = p.Value<bool?>("visibleOnly") ?? true;

            bool any = !string.IsNullOrWhiteSpace(moveColumn)
                       || !string.IsNullOrWhiteSpace(beforeColumn)
                       || moveIndex.HasValue
                       || beforeIndex.HasValue;
            if (!any) return false;

            // Column-letter mode (Excel-like): always targets visible columns
            if (!string.IsNullOrWhiteSpace(moveColumn) || !string.IsNullOrWhiteSpace(beforeColumn))
            {
                if (string.IsNullOrWhiteSpace(moveColumn) || string.IsNullOrWhiteSpace(beforeColumn))
                {
                    error = "moveColumn and beforeColumn are required together (e.g., moveColumn:'N', beforeColumn:'H').";
                    return false;
                }

                int from;
                int before;
                if (!TryParseExcelColumnLabel(moveColumn, out from))
                {
                    error = $"Invalid moveColumn: '{moveColumn}'. Use A..Z, AA.. etc.";
                    return false;
                }
                if (!TryParseExcelColumnLabel(beforeColumn, out before))
                {
                    error = $"Invalid beforeColumn: '{beforeColumn}'. Use A..Z, AA.. etc.";
                    return false;
                }

                req.FromIndex1 = from;
                req.BeforeIndex1 = before;
                req.FromColumn = moveColumn;
                req.BeforeColumn = beforeColumn;
                req.VisibleOnly = true;
                return true;
            }

            // Numeric index mode (1-based)
            if (!moveIndex.HasValue || !beforeIndex.HasValue)
            {
                error = "moveIndex and beforeIndex are required together (1-based).";
                return false;
            }

            req.FromIndex1 = moveIndex.Value;
            req.BeforeIndex1 = beforeIndex.Value;
            req.VisibleOnly = visibleOnly;
            return true;
        }

        private static bool TryParseExcelColumnLabel(string label, out int index1Based)
        {
            index1Based = 0;
            if (string.IsNullOrWhiteSpace(label)) return false;
            var s = label.Trim().ToUpperInvariant();
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 'A' || c > 'Z') return false;
                v = (v * 26) + (c - 'A' + 1);
            }
            if (v <= 0) return false;
            index1Based = v;
            return true;
        }

        private static bool TryBuildMovedOrder(
            List<ScheduleFieldId> current,
            List<FieldInfo> infos,
            MoveRequest req,
            out IList<ScheduleFieldId> newOrder,
            out object movedSummary,
            out string error)
        {
            newOrder = null;
            movedSummary = null;
            error = "";

            if (current == null || infos == null || current.Count == 0)
            {
                error = "Schedule has no fields to reorder.";
                return false;
            }

            if (req.FromIndex1 <= 0)
            {
                error = "moveIndex/moveColumn must be >= 1.";
                return false;
            }
            if (req.BeforeIndex1 <= 0)
            {
                error = "beforeIndex/beforeColumn must be >= 1.";
                return false;
            }

            // Visible-only move keeps hidden field positions unchanged.
            if (req.VisibleOnly)
            {
                var visibleSlots = new List<int>();
                var visibleIds = new List<ScheduleFieldId>();
                var visibleInfos = new List<FieldInfo>();

                for (int i = 0; i < infos.Count; i++)
                {
                    if (!infos[i].Hidden)
                    {
                        visibleSlots.Add(infos[i].Index);
                        visibleIds.Add(infos[i].FieldId);
                        visibleInfos.Add(infos[i]);
                    }
                }

                int visibleCount = visibleIds.Count;
                if (visibleCount <= 0)
                {
                    error = "No visible columns in this schedule.";
                    return false;
                }

                if (req.FromIndex1 > visibleCount)
                {
                    error = $"moveIndex is out of range. visibleColumns={visibleCount}, moveIndex={req.FromIndex1}.";
                    return false;
                }
                if (req.BeforeIndex1 > visibleCount + 1)
                {
                    error = $"beforeIndex is out of range. visibleColumns={visibleCount}, beforeIndex={req.BeforeIndex1}.";
                    return false;
                }

                int from0 = req.FromIndex1 - 1;
                int before0 = req.BeforeIndex1 - 1;

                // No-op cases: moving before itself, or already immediately before the target.
                if (from0 == before0 || from0 == before0 - 1)
                {
                    movedSummary = new
                    {
                        ok = true,
                        noChange = true,
                        move = new { from = req.FromIndex1, before = req.BeforeIndex1, moveColumn = req.FromColumn, beforeColumn = req.BeforeColumn }
                    };
                    newOrder = current.ToList();
                    return true;
                }

                var movedId = visibleIds[from0];
                var movedInfo = visibleInfos[from0];

                visibleIds.RemoveAt(from0);
                visibleInfos.RemoveAt(from0);

                int insert0 = before0;
                if (from0 < insert0) insert0 -= 1;
                if (insert0 < 0) insert0 = 0;
                if (insert0 > visibleIds.Count) insert0 = visibleIds.Count;

                visibleIds.Insert(insert0, movedId);
                visibleInfos.Insert(insert0, movedInfo);

                var full = current.ToList();
                for (int j = 0; j < visibleSlots.Count; j++)
                {
                    full[visibleSlots[j]] = visibleIds[j];
                }

                newOrder = full;
                movedSummary = new
                {
                    ok = true,
                    noChange = false,
                    move = new
                    {
                        from = req.FromIndex1,
                        before = req.BeforeIndex1,
                        moveColumn = req.FromColumn,
                        beforeColumn = req.BeforeColumn,
                        visibleColumns = visibleCount
                    },
                    movedField = new { name = movedInfo.Name, heading = movedInfo.Heading, paramId = movedInfo.ParamId },
                    movedTo = new { index = insert0 + 1 }
                };
                return true;
            }

            // Full-order move (includes hidden fields)
            int total = current.Count;
            if (req.FromIndex1 > total)
            {
                error = $"moveIndex is out of range. totalFields={total}, moveIndex={req.FromIndex1}.";
                return false;
            }
            if (req.BeforeIndex1 > total + 1)
            {
                error = $"beforeIndex is out of range. totalFields={total}, beforeIndex={req.BeforeIndex1}.";
                return false;
            }

            int f0 = req.FromIndex1 - 1;
            int b0 = req.BeforeIndex1 - 1;
            if (f0 == b0 || f0 == b0 - 1)
            {
                movedSummary = new { ok = true, noChange = true, move = new { from = req.FromIndex1, before = req.BeforeIndex1 } };
                newOrder = current.ToList();
                return true;
            }

            var list = current.ToList();
            var item = list[f0];
            list.RemoveAt(f0);
            int ins = b0;
            if (f0 < ins) ins -= 1;
            if (ins < 0) ins = 0;
            if (ins > list.Count) ins = list.Count;
            list.Insert(ins, item);

            newOrder = list;
            movedSummary = new { ok = true, noChange = false, move = new { from = req.FromIndex1, before = req.BeforeIndex1 }, movedTo = new { index = ins + 1 }, totalFields = total };
            return true;
        }

        private static List<OrderSpec> ParseOrderSpecs(JObject p)
        {
            var list = new List<OrderSpec>();

            // Preferred: order:[ ... ]
            if (p["order"] is JArray orderArr)
            {
                foreach (var tok in orderArr)
                {
                    if (tok == null) continue;
                    if (tok.Type == JTokenType.String)
                    {
                        list.Add(new OrderSpec { Name = tok.Value<string>() });
                        continue;
                    }
                    if (tok.Type == JTokenType.Integer)
                    {
                        list.Add(new OrderSpec { ParamId = tok.Value<int>() });
                        continue;
                    }
                    if (tok.Type == JTokenType.Object)
                    {
                        var o = (JObject)tok;
                        var spec = new OrderSpec
                        {
                            Name = o.Value<string>("name") ?? o.Value<string>("fieldName"),
                            Heading = o.Value<string>("heading") ?? o.Value<string>("columnHeading"),
                            ParamId = o.Value<int?>("paramId")
                        };
                        list.Add(spec);
                        continue;
                    }
                }
            }

            // Back-compat: orderNames/orderParamIds
            if (list.Count == 0)
            {
                try
                {
                    var names = p["orderNames"]?.ToObject<List<string>>();
                    if (names != null)
                    {
                        foreach (var n in names)
                            list.Add(new OrderSpec { Name = n });
                    }
                }
                catch { /* ignore */ }

                try
                {
                    var ids = p["orderParamIds"]?.ToObject<List<int>>();
                    if (ids != null)
                    {
                        foreach (var id in ids)
                            list.Add(new OrderSpec { ParamId = id });
                    }
                }
                catch { /* ignore */ }
            }

            return list;
        }

        private static ScheduleFieldId FindFirstMatch(List<FieldInfo> targets, HashSet<ScheduleFieldId> remaining, OrderSpec spec)
        {
            foreach (var fi in targets)
            {
                if (!remaining.Contains(fi.FieldId))
                    continue;

                if (spec.ParamId.HasValue && fi.ParamId == spec.ParamId.Value)
                    return fi.FieldId;

                var head = Norm(spec.Heading);
                if (!string.IsNullOrEmpty(head))
                {
                    if (string.Equals(head, Norm(fi.Heading), StringComparison.OrdinalIgnoreCase))
                        return fi.FieldId;
                }

                var name = Norm(spec.Name);
                if (!string.IsNullOrEmpty(name))
                {
                    if (string.Equals(name, Norm(fi.Name), StringComparison.OrdinalIgnoreCase))
                        return fi.FieldId;
                    if (string.Equals(name, Norm(fi.Heading), StringComparison.OrdinalIgnoreCase))
                        return fi.FieldId;
                }
            }

            return null;
        }

        private static FieldInfo BuildFieldInfo(ScheduleDefinition def, ScheduleFieldId fid, int index)
        {
            try
            {
                var f = def.GetField(fid);
                if (f == null) return null;

                string name = "";
                string heading = "";
                int paramId = int.MinValue;
                bool hidden = false;

                try { name = f.GetName() ?? ""; } catch { name = ""; }
                try { heading = f.ColumnHeading ?? ""; } catch { heading = ""; }
                try { paramId = f.ParameterId != null ? f.ParameterId.IntValue() : int.MinValue; } catch { paramId = int.MinValue; }
                try { hidden = f.IsHidden; } catch { hidden = false; }

                return new FieldInfo
                {
                    FieldId = fid,
                    Index = index,
                    Name = name,
                    Heading = heading,
                    ParamId = paramId,
                    Hidden = hidden
                };
            }
            catch
            {
                return null;
            }
        }

        private static object BuildFieldSummary(ScheduleDefinition def, ScheduleFieldId fid, int index)
        {
            try
            {
                var f = def.GetField(fid);
                if (f == null) return new { index = index, name = "", heading = "", paramId = int.MinValue, hidden = false };
                string name = ""; try { name = f.GetName() ?? ""; } catch { name = ""; }
                string heading = ""; try { heading = f.ColumnHeading ?? ""; } catch { heading = ""; }
                int pid = int.MinValue; try { pid = f.ParameterId != null ? f.ParameterId.IntValue() : int.MinValue; } catch { pid = int.MinValue; }
                bool hidden = false; try { hidden = f.IsHidden; } catch { hidden = false; }
                return new { index = index, name = name, heading = heading, paramId = pid, hidden = hidden };
            }
            catch
            {
                return new { index = index, name = "", heading = "", paramId = int.MinValue, hidden = false };
            }
        }

        private static bool SameFieldSet(IList<ScheduleFieldId> a, IList<ScheduleFieldId> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            var sa = new HashSet<ScheduleFieldId>(a);
            var sb = new HashSet<ScheduleFieldId>(b);
            return sa.SetEquals(sb);
        }

        private static bool TrySetFieldOrder(ScheduleDefinition def, IList<ScheduleFieldId> order, out string error)
        {
            error = "";
            try
            {
                var t = def.GetType();
                var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "SetFieldOrder" && m.GetParameters().Length == 1)
                    .ToList();

                foreach (var mi in methods)
                {
                    var pt = mi.GetParameters()[0].ParameterType;
                    object arg = null;

                    if (pt.IsAssignableFrom(order.GetType()))
                    {
                        arg = order;
                    }
                    else if (pt.IsArray && pt.GetElementType() == typeof(ScheduleFieldId))
                    {
                        arg = order.ToArray();
                    }

                    if (arg == null) continue;
                    mi.Invoke(def, new object[] { arg });
                    return true;
                }

                error = "ScheduleDefinition.SetFieldOrder not found in this Revit API.";
                return false;
            }
            catch (TargetInvocationException tie)
            {
                error = tie.InnerException != null ? tie.InnerException.Message : tie.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Norm(string s)
        {
            return (s ?? "").Trim();
        }

        private sealed class FieldInfo
        {
            public ScheduleFieldId FieldId { get; set; }
            public int Index { get; set; }
            public string Name { get; set; }
            public string Heading { get; set; }
            public int ParamId { get; set; }
            public bool Hidden { get; set; }
        }

        private sealed class OrderSpec
        {
            public string Name { get; set; }
            public string Heading { get; set; }
            public int? ParamId { get; set; }

            public object ToDebugObject()
            {
                return new
                {
                    name = Name ?? "",
                    heading = Heading ?? "",
                    paramId = ParamId.HasValue ? ParamId.Value : (int?)null
                };
            }
        }

        private sealed class MoveRequest
        {
            public int FromIndex1 { get; set; }
            public int BeforeIndex1 { get; set; }
            public bool VisibleOnly { get; set; }
            public string FromColumn { get; set; }
            public string BeforeColumn { get; set; }
        }
    }
}


