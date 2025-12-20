// File: Commands/ParamOps/GetParameterIdentityCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// JSON-RPC: get_parameter_identity
    /// Resolve a parameter on a target (element/type) and report rich identity info:
    /// origin, placement, attachedTo, group, spec/unit, readOnly, ids, categories, sample value, notes, etc.
    ///
    /// Payload example:
    /// {
    ///   "target": { "by":"elementId|typeId|uniqueId", "value":123 },
    ///   "paramName":"符号", "builtInName":"", "builtInId":null, "guid":"",
    ///   "attachedToOverride":"instance|type",  // optional: prefer where to resolve first
    ///   "fields":["name","origin","group","placement","guid"] // optional projection
    /// }
    /// </summary>
    public class GetParameterIdentityCommand : IRevitCommandHandler
    {
        public string CommandName => "get_parameter_identity";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---- Resolve target ----
            Element target = null;
            string targetKind = "unknown";
            var tgt = p.SelectToken("target") as JObject;
            if (tgt == null) return ResultUtil.Err("target is required (by,value).");

            var by = (tgt.Value<string>("by") ?? "").Trim().ToLowerInvariant();
            var valTok = tgt["value"];
            try
            {
                switch (by)
                {
                    case "elementid":
                        {
                            int id = valTok?.Value<int>() ?? -1;
                            target = id > 0 ? doc.GetElement(new ElementId(id)) : null;
                            targetKind = "element";
                            break;
                        }
                    case "typeid":
                        {
                            int id = valTok?.Value<int>() ?? -1;
                            target = id > 0 ? doc.GetElement(new ElementId(id)) : null;
                            targetKind = "type";
                            break;
                        }
                    case "uniqueid":
                        {
                            string uid = valTok?.Value<string>() ?? string.Empty;
                            target = string.IsNullOrWhiteSpace(uid) ? null : doc.GetElement(uid);
                            targetKind = "element";
                            break;
                        }
                    default:
                        return ResultUtil.Err($"Unsupported target.by: {by}");
                }
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"Target resolution error: {ex.Message}");
            }
            if (target == null) return ResultUtil.Err("Target element/type not found.");

            // Prepare instance/type candidates
            Element instanceElem = null;
            Element typeElem = null;
            if (target is Element el)
            {
                if (targetKind == "type")
                {
                    typeElem = el;
                }
                else
                {
                    instanceElem = el;
                    try
                    {
                        var typeId = el.GetTypeId();
                        if (typeId != null && typeId != ElementId.InvalidElementId)
                            typeElem = doc.GetElement(typeId);
                    }
                    catch { }
                }
            }

            // Resolve preference
            var overrideAttach = (p.Value<string>("attachedToOverride") ?? "").Trim().ToLowerInvariant();
            bool preferType = overrideAttach == "type";
            bool preferInstance = overrideAttach == "instance";

            Parameter prm = null;
            string resolvedBy = null;
            string attachedTo = null; // instance | type
            Element resolvedOn = null;

            // Resolve with preference and auto-fallback
            if (preferType && typeElem != null)
            {
                prm = ParamResolver.ResolveByPayload(typeElem, p, out resolvedBy);
                if (prm != null) { attachedTo = "type"; resolvedOn = typeElem; }
            }
            if (prm == null && instanceElem != null && !preferType)
            {
                prm = ParamResolver.ResolveByPayload(instanceElem, p, out resolvedBy);
                if (prm != null) { attachedTo = "instance"; resolvedOn = instanceElem; }
            }
            // If preferred 'type' failed, also try instance as a fallback
            if (prm == null && instanceElem != null && preferType)
            {
                prm = ParamResolver.ResolveByPayload(instanceElem, p, out resolvedBy);
                if (prm != null) { attachedTo = "instance"; resolvedOn = instanceElem; }
            }
            // Always fallback to type if still null
            if (prm == null && typeElem != null)
            {
                prm = ParamResolver.ResolveByPayload(typeElem, p, out resolvedBy);
                if (prm != null) { attachedTo = "type"; resolvedOn = typeElem; }
            }

            if (prm == null)
            {
                return ResultUtil.Ok(new
                {
                    found = false,
                    target = new { kind = targetKind, id = target.Id.IntegerValue, uniqueId = (target as Element)?.UniqueId },
                    resolvedBy,
                    attachedTo = (string)null,
                    message = "Parameter not found on instance nor type"
                });
            }

            // Shared and GUID
            bool isShared = false;
            try { isShared = prm.IsShared; } catch { isShared = false; }

            string guidStr = string.Empty;
            try
            {
                var ext = prm.Definition as ExternalDefinition;
                if (ext != null)
                {
                    var g = ext.GUID;
                    if (g != Guid.Empty) guidStr = g.ToString();
                }
            }
            catch { /* ignore */ }

            if (string.IsNullOrWhiteSpace(guidStr) && isShared)
            {
                try
                {
                    var name = SafeParamName(prm);
                    var spe = new FilteredElementCollector(doc)
                        .OfClass(typeof(SharedParameterElement))
                        .Cast<SharedParameterElement>()
                        .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (spe != null)
                    {
                        var g = spe.GuidValue;
                        if (g != Guid.Empty) guidStr = g.ToString();
                    }
                }
                catch { /* ignore */ }
            }

            // Built-in and ids
            bool isBuiltIn = false;
            int paramId = 0;
            try
            {
                paramId = prm.Id?.IntegerValue ?? 0;
                isBuiltIn = paramId < 0;
            }
            catch { }

            // Storage type
            string storage = string.Empty;
            try { storage = prm.StorageType.ToString(); } catch { }

            // Parameter group + label (project UI group)
            string groupEnum = null; // projectGroup.enum
            string groupUi = null;   // projectGroup.uiLabel
            try
            {
                var grp = prm.Definition?.ParameterGroup ?? BuiltInParameterGroup.INVALID;
                groupEnum = grp.ToString();
                try { groupUi = LabelUtils.GetLabelFor(grp); } catch { groupUi = null; }
            }
            catch { }

            // Spec/DataType + display unit
            ForgeTypeId spec = null;
            try { spec = UnitHelper.GetSpec(prm); } catch { }
            string displayUnit = null;
            try { displayUnit = UnitHelper.UnitLabel(spec); } catch { displayUnit = null; }

            // Placement (Binding) and categories via ParameterBindings (only when not built-in)
            string placement = null; // instance | type | null
            List<string> categories = null;
            ElementId parameterElementId = null;
            bool? allowVaryBetweenGroups = null;
            SharedParameterElement sharedParamElement = null;
            try
            {
                if (!isBuiltIn)
                {
                    var def = prm.Definition;
                    if (def != null)
                    {
                        var map = doc.ParameterBindings;
                        var it = map.ForwardIterator();
                        it.Reset();
                        while (it.MoveNext())
                        {
                            var def2 = it.Key as Definition;
                            if (def2 == null) continue;
                            bool nameMatch = string.Equals(def2.Name ?? string.Empty, def.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                            bool guidMatch = false;
                            try
                            {
                                var e1 = def2 as ExternalDefinition;
                                var e2 = def as ExternalDefinition;
                                if (e1 != null && e2 != null) guidMatch = e1.GUID == e2.GUID;
                            }
                            catch { }
                            if (nameMatch || guidMatch)
                            {
                                var bind = it.Current as ElementBinding;
                                if (bind != null)
                                {
                                    placement = (bind is InstanceBinding) ? "instance" : (bind is TypeBinding) ? "type" : null;
                                    try
                                    {
                                        var cs = bind.Categories;
                                        if (cs != null)
                                        {
                                            categories = cs.Cast<Category>().Select(c => c?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                                        }
                                    }
                                    catch { }
                                }
                                // Try find ParameterElement (SharedParameterElement or ParameterElement) by definition
                                if (parameterElementId == null || parameterElementId == ElementId.InvalidElementId)
                                {
                                    try
                                    {
                                        var coll = new FilteredElementCollector(doc).OfClass(typeof(ParameterElement)).Cast<ParameterElement>();
                                        var pe = coll.FirstOrDefault(pe0 => string.Equals(pe0?.GetDefinition()?.Name ?? pe0?.Name ?? string.Empty, def.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                                        if (pe != null)
                                        {
                                            parameterElementId = pe.Id;
                                            sharedParamElement = pe as SharedParameterElement;
                                            // allow vary between groups (best-effort; API varies)
                                            try
                                            {
                                                var pi = pe.GetType().GetProperty("AllowsVaryBetweenGroups");
                                                if (pi != null && pi.PropertyType == typeof(bool))
                                                    allowVaryBetweenGroups = (bool)pi.GetValue(pe);
                                                else
                                                {
                                                    var mi = pe.GetType().GetMethod("GetAllowsVaryBetweenGroups");
                                                    if (mi != null)
                                                    {
                                                        var v = mi.Invoke(pe, null);
                                                        if (v is bool b) allowVaryBetweenGroups = b;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                }
                                break;
                            }
                        }

                        // If still not found and we have a shared GUID, try SharedParameterElement directly
                        if ((parameterElementId == null || parameterElementId == ElementId.InvalidElementId) && !string.IsNullOrWhiteSpace(guidStr))
                        {
                            try
                            {
                                var g = new Guid(guidStr);
                                var pe = new FilteredElementCollector(doc)
                                    .OfClass(typeof(SharedParameterElement))
                                    .Cast<SharedParameterElement>()
                                    .FirstOrDefault(x => x.GuidValue == g);
                                if (pe != null) { parameterElementId = pe.Id; sharedParamElement = pe; }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // Origin heuristic
            string origin = null; // builtIn | shared | project | family
            if (isBuiltIn) origin = "builtIn";
            else if (isShared) origin = "shared";
            else origin = "project"; // family reserved for family docs
            try { if (doc.IsFamilyDocument) origin = "family"; } catch { }

            // Readonly
            bool isReadOnly = false;
            try { isReadOnly = prm.IsReadOnly; } catch { }

            // Sample value (respect unitsMode)
            object valueSample = null;
            try
            {
                var mode = UnitHelper.ResolveUnitsMode(doc, p);
                // Use MapParameter to normalize the shape per mode
                var mapped = Newtonsoft.Json.Linq.JObject.FromObject(
                    UnitHelper.MapParameter(prm, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3)
                );

                // Project a compact block depending on mode
                switch (mode)
                {
                    case UnitsMode.Project:
                        valueSample = new
                        {
                            display = (string)mapped["display"],
                            unit = (string)mapped["unit"],              // "project"
                            value = (double?)mapped["value"],
                            raw = (double?)mapped["raw"]
                        };
                        break;
                    case UnitsMode.Raw:
                        valueSample = new
                        {
                            display = (string)mapped["display"],
                            unit = (string)mapped["unit"],              // "raw"
                            value = (double?)mapped["value"],           // raw value
                            raw = (double?)mapped["raw"]
                        };
                        break;
                    case UnitsMode.Both:
                        valueSample = new
                        {
                            display = (string)mapped["display"],
                            unitSi = (string)mapped["unitSi"],
                            valueSi = (double?)mapped["valueSi"],
                            unitProject = (string)mapped["unitProject"], // "project"
                            valueProject = (double?)mapped["valueProject"],
                            raw = (double?)mapped["raw"]
                        };
                        break;
                    case UnitsMode.SI:
                    default:
                        valueSample = new
                        {
                            display = (string)mapped["display"],
                            unit = (string)mapped["unit"],              // "mm|m2|m3|deg" or null
                            value = (double?)mapped["value"],           // SI value
                            raw = (double?)mapped["raw"]
                        };
                        break;
                }
            }
            catch { }

            // SPF Join (optional, v2)
            string spfGroup = null;
            object spfMeta = null;

            bool joinSpf = p.Value<bool?>("joinSharedParameterFile") ?? false;
            if (joinSpf && (origin == "shared" || origin == "project"))
            {
                try
                {
                    if (SpfCatalog.TryLoad(p["spf"], out var cat, out var spfErr) && cat != null)
                    {
                        var keyGuid = !string.IsNullOrWhiteSpace(guidStr) ? guidStr : null;
                        if (!string.IsNullOrWhiteSpace(keyGuid) && cat.TryGetByGuid(keyGuid, out var meta))
                        {
                            spfGroup = meta.GroupName;
                            spfMeta = new
                            {
                                source = "sharedParameterFile",
                                groupId = meta.GroupId,
                                groupName = meta.GroupName,
                                dataType = meta.DataType,
                                extra = meta.Extra.Count > 0 ? (object)meta.Extra : null
                            };
                        }
                    }
                }
                catch { }
            }

            // Notes
            string notes = null;
            try
            {
                if (isBuiltIn && isReadOnly) notes = "Built-in read-only parameter";
                else if (attachedTo == "instance" && isReadOnly && typeElem != null)
                    notes = "Likely type-level parameter; change type to edit";
            }
            catch { }

            // GUID canonicalization & provenance checks (v2) — only for shared parameters
            try
            {
                if (origin == "shared")
                {
                    Guid? speGuid = null;
                    if (sharedParamElement != null)
                    {
                        var g = sharedParamElement.GuidValue; if (g != Guid.Empty) speGuid = g;
                    }
                    Guid? extGuid = null;
                    try { var ext = prm.Definition as ExternalDefinition; if (ext != null && ext.GUID != Guid.Empty) extGuid = ext.GUID; } catch { }

                    var canonical = speGuid?.ToString();
                    if (!string.IsNullOrWhiteSpace(canonical)) guidStr = canonical; // prefer SPE GuidValue

                    if (extGuid.HasValue && speGuid.HasValue && extGuid.Value != speGuid.Value)
                    {
                        notes = AppendNote(notes, "ExternalDefinition.GUID != SharedParameterElement.GuidValue");
                    }
                }
                else
                {
                    // Non-shared parameters must not expose GUIDs
                    guidStr = null;
                    sharedParamElement = null;
                }
            }
            catch { }

            // Build parameter block
            var paramObj = new Dictionary<string, object>();
            paramObj["name"] = SafeParamName(prm);
            paramObj["paramId"] = paramId;
            paramObj["storageType"] = storage;
            paramObj["origin"] = origin;
            // v2: expose projectGroup (alias for backward compat)
            var projectGroup = new { enumName = groupEnum, uiLabel = groupUi };
            paramObj["group"] = projectGroup; // keep legacy key
            paramObj["projectGroup"] = projectGroup;
            if (!string.IsNullOrWhiteSpace(spfGroup)) paramObj["spfGroup"] = spfGroup;
            if (spfMeta != null) paramObj["spfMeta"] = spfMeta;
            paramObj["placement"] = placement;
            paramObj["attachedTo"] = attachedTo;
            paramObj["isReadOnly"] = isReadOnly;
            paramObj["isShared"] = isShared;
            paramObj["isBuiltIn"] = isBuiltIn;
            paramObj["guid"] = string.IsNullOrWhiteSpace(guidStr) ? null : guidStr;
            paramObj["parameterElementId"] = parameterElementId?.IntegerValue ?? 0;
            paramObj["categories"] = categories;
            paramObj["dataType"] = new { storage = storage, spec = spec?.TypeId }; // expose ForgeTypeId via TypeId string
            paramObj["displayUnit"] = displayUnit;
            paramObj["allowVaryBetweenGroups"] = allowVaryBetweenGroups;
            paramObj["value"] = valueSample;
            paramObj["notes"] = notes;

            // Optional projection
            var fieldsArr = p["fields"] as JArray;
            if (fieldsArr != null && fieldsArr.Count > 0)
            {
                var wanted = new HashSet<string>(fieldsArr.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                var proj = new Dictionary<string, object>();
                foreach (var k in paramObj.Keys.ToList())
                {
                    if (wanted.Contains(k)) proj[k] = paramObj[k];
                }
                paramObj = proj;
            }

            // Prepare result
            var res = new
            {
                found = true,
                target = new { kind = targetKind, id = target.Id.IntegerValue, uniqueId = (target as Element)?.UniqueId },
                resolvedBy,
                parameter = paramObj
            };

            return ResultUtil.Ok(res);
        }

        private static string SafeParamName(Parameter prm)
        {
            try { return prm?.Definition?.Name ?? prm?.ToString() ?? string.Empty; } catch { return prm?.ToString() ?? string.Empty; }
        }

        private static string AppendNote(string baseNote, string addition)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(addition)) return baseNote;
                if (string.IsNullOrWhiteSpace(baseNote)) return addition;
                if (baseNote.IndexOf(addition, StringComparison.OrdinalIgnoreCase) >= 0) return baseNote;
                return baseNote + "; " + addition;
            }
            catch { return baseNote ?? addition; }
        }
    }
}
