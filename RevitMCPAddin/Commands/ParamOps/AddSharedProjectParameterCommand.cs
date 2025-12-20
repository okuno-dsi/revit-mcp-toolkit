#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// JSON-RPC: add_shared_project_parameter
    /// Adds (binds) a shared parameter to the current project across specified categories.
    /// </summary>
    public class AddSharedProjectParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "add_shared_project_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();

            try
            {
                // ---- Read params ----
                var name = (p.Value<string>("name") ?? string.Empty).Trim();
                var groupName = (p.Value<string>("groupName") ?? string.Empty).Trim();
                var parameterTypeStr = (p.Value<string>("parameterType") ?? "Text").Trim();
                var parameterGroupToken = p["parameterGroup"];
                bool parameterGroupProvided = parameterGroupToken != null && parameterGroupToken.Type != JTokenType.Null;
                var parameterGroupStr = (parameterGroupToken?.Value<string>() ?? string.Empty).Trim();
                var isInstance = p.Value<bool?>("isInstance") ?? true;
                var forceBindingType = p.Value<bool?>("forceBindingType") ?? false;
                var mergeExistingCategories = p.Value<bool?>("mergeExistingCategories") ?? true;
                var createIfMissing = p.Value<bool?>("createDefinitionIfMissing") ?? true;

                var categories = new List<string>();
                if (p["categories"] is JArray cats)
                {
                    foreach (var c in cats)
                    {
                        var s = (c?.Value<string>() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(s)) categories.Add(s);
                    }
                }

                var guidStr = (p.Value<string>("guid") ?? string.Empty).Trim();
                Guid? targetGuid = null;
                if (!string.IsNullOrWhiteSpace(guidStr) && Guid.TryParse(guidStr, out var g))
                    targetGuid = g;

                if (string.IsNullOrWhiteSpace(name))
                    return ResultUtil.Err("name is required.");
                if (string.IsNullOrWhiteSpace(groupName))
                    return ResultUtil.Err("groupName is required.");
                if (categories.Count == 0)
                    return ResultUtil.Err("categories must contain at least one category name.");

                // ---- Shared parameter file ----
                var app = doc.Application;
                string spFile = app.SharedParametersFilename;
                if (string.IsNullOrWhiteSpace(spFile))
                {
                    return ResultUtil.Err(new
                    {
                        msg = "Shared parameter file is not configured.",
                        detail = new { sharedParametersFilename = spFile }
                    });
                }

                var defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    return ResultUtil.Err(new
                    {
                        msg = "Failed to open shared parameter file.",
                        detail = new { sharedParametersFilename = spFile }
                    });
                }

                // 1) Group
                var group = defFile.Groups.get_Item(groupName) ?? defFile.Groups.Create(groupName);

                // 2) Definition (find or create)
                Definition def = FindDefinition(group, name, targetGuid);
                if (def == null)
                {
                    if (!createIfMissing)
                    {
                        return ResultUtil.Err(new
                        {
                            msg = "Shared parameter definition not found and creation is disabled.",
                            detail = new { name, guid = targetGuid?.ToString() }
                        });
                    }

                    var fdt = ResolveSpecTypeIdOrDefault(parameterTypeStr);
                    var options = new ExternalDefinitionCreationOptions(name, fdt);
                    if (targetGuid.HasValue) options.GUID = targetGuid.Value;
                    def = group.Definitions.Create(options);
                }

                var extDef = def as ExternalDefinition;
                var resolvedGuid = extDef?.GUID.ToString() ?? targetGuid?.ToString() ?? string.Empty;

                // 2.5) Existing binding (if already present in project)
                var map = doc.ParameterBindings;
                Definition existingDefInMap = null;
                ElementBinding existingBinding = null;
                bool hasExistingBinding = TryFindExistingBinding(map, def, out existingDefInMap, out existingBinding);

                // 3) CategorySet
                CategorySet set = app.Create.NewCategorySet();
                var categoriesBound = new System.Collections.Generic.List<string>();
                var categoriesSkipped = new System.Collections.Generic.List<string>();
                foreach (var catName in categories)
                {
                    Category cat = null;
                    try { cat = doc.Settings.Categories.get_Item(catName); } catch { }
                    if (cat != null)
                    {
                        try { set.Insert(cat); } catch { /* ignore duplicates/invalid */ }
                        categoriesBound.Add(catName);
                    }
                    else
                    {
                        categoriesSkipped.Add(catName);
                    }
                }
                if (set.IsEmpty)
                {
                    return ResultUtil.Err(new
                    {
                        msg = "No valid categories found to bind shared parameter.",
                        detail = new { requestedCategories = categories, categoriesSkipped }
                    });
                }

                // 4) Binding
                var categoriesFinal = new System.Collections.Generic.List<string>();

                CategorySet finalSet = set;
                bool merged = false;
                if (hasExistingBinding && existingBinding != null && mergeExistingCategories)
                {
                    try
                    {
                        var union = app.Create.NewCategorySet();
                        try { AddCategories(union, existingBinding.Categories); } catch { }
                        AddCategories(union, set);
                        if (!union.IsEmpty)
                        {
                            finalSet = union;
                            merged = true;
                        }
                    }
                    catch { /* keep requested set */ }
                }

                try
                {
                    foreach (Category c in finalSet)
                    {
                        try
                        {
                            var n = c != null ? c.Name : null;
                            if (!string.IsNullOrWhiteSpace(n)) categoriesFinal.Add(n);
                        }
                        catch { }
                    }
                }
                catch { }

                string bindingTypeRequested = isInstance ? "instance" : "type";
                string bindingTypeUsed = bindingTypeRequested;
                bool bindingTypeMismatch = false;

                Binding binding = null;
                if (hasExistingBinding && existingBinding != null && !forceBindingType)
                {
                    if (existingBinding is InstanceBinding)
                    {
                        bindingTypeUsed = "instance";
                        bindingTypeMismatch = bindingTypeRequested != bindingTypeUsed;
                        binding = (Binding)app.Create.NewInstanceBinding(finalSet);
                    }
                    else if (existingBinding is TypeBinding)
                    {
                        bindingTypeUsed = "type";
                        bindingTypeMismatch = bindingTypeRequested != bindingTypeUsed;
                        binding = (Binding)app.Create.NewTypeBinding(finalSet);
                    }
                }

                if (binding == null)
                {
                    binding = isInstance ? (Binding)app.Create.NewInstanceBinding(finalSet) : (Binding)app.Create.NewTypeBinding(finalSet);
                }

                // Parameter group (prefer preserving existing when not specified)
                Autodesk.Revit.DB.ForgeTypeId groupTypeId = null;
                Autodesk.Revit.DB.BuiltInParameterGroup pg = Autodesk.Revit.DB.BuiltInParameterGroup.PG_TEXT;
                string parameterGroupUsed = null;
                string parameterGroupSource = null;

                if (parameterGroupProvided && !string.IsNullOrWhiteSpace(parameterGroupStr))
                {
                    groupTypeId = ResolveGroupTypeIdOrNull(parameterGroupStr);
                    if (groupTypeId != null)
                    {
                        parameterGroupUsed = groupTypeId.TypeId;
                        parameterGroupSource = "payload(groupTypeId)";
                    }
                    else
                    {
                        pg = ParseParameterGroup(parameterGroupStr);
                        parameterGroupUsed = pg.ToString();
                        parameterGroupSource = "payload(builtInParameterGroup)";
                    }
                }
                else
                {
                    try
                    {
                        var defForGroup = hasExistingBinding && existingDefInMap != null ? existingDefInMap : def;
                        var g0 = defForGroup != null ? defForGroup.ParameterGroup : Autodesk.Revit.DB.BuiltInParameterGroup.INVALID;
                        if (g0 != Autodesk.Revit.DB.BuiltInParameterGroup.INVALID)
                        {
                            pg = g0;
                            parameterGroupUsed = pg.ToString();
                            parameterGroupSource = "existingBinding(def.ParameterGroup)";
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(parameterGroupUsed))
                    {
                        pg = Autodesk.Revit.DB.BuiltInParameterGroup.PG_TEXT;
                        parameterGroupUsed = pg.ToString();
                        parameterGroupSource = "default(PG_TEXT)";
                    }
                }

                // Use existing definition key when possible
                Definition defForMap = hasExistingBinding && existingDefInMap != null ? existingDefInMap : def;

                using (var scope = new FailureCaptureScope(uiapp, suppressWarnings: true, rollbackOnError: true))
                using (var tx = new Transaction(doc, "Add Shared Project Parameter"))
                {
                    tx.Start();
                    bool ok;
                    if (groupTypeId != null)
                    {
                        ok = map.Insert(defForMap, binding, groupTypeId);
                        if (!ok) ok = map.ReInsert(defForMap, binding, groupTypeId);
                    }
                    else
                    {
                        ok = map.Insert(defForMap, binding, pg);
                        if (!ok) ok = map.ReInsert(defForMap, binding, pg);
                    }
                    if (!ok)
                    {
                        tx.RollBack();
                        return ResultUtil.Err(new
                        {
                            msg = "Failed to insert or re-insert parameter binding.",
                            detail = new { name, guid = resolvedGuid, parameterGroup = parameterGroupUsed }
                        });
                    }
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        return ResultUtil.Err(new
                        {
                            msg = "Transaction did not commit.",
                            detail = new
                            {
                                transactionStatus = status.ToString(),
                                name,
                                guid = resolvedGuid,
                                parameterGroup = parameterGroupUsed,
                                issues = scope.Issues
                            }
                        });
                    }
                }

                return ResultUtil.Ok(new
                {
                    name,
                    guid = resolvedGuid,
                    groupName,
                    parameterGroup = parameterGroupUsed,
                    parameterGroupSource,
                    isInstance,
                    bindingTypeRequested,
                    bindingTypeUsed,
                    bindingTypeMismatch,
                    mergedExistingCategories = merged,
                    hasExistingBinding,
                    categoriesBound,
                    categoriesFinal,
                    categoriesSkipped
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"Add shared project parameter failed: {ex.Message}");
            }
        }

        private static Definition FindDefinition(DefinitionGroup group, string name, Guid? guid)
        {
            foreach (Definition d in group.Definitions)
            {
                if (!string.Equals(d?.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (guid == null) return d;
                var ed = d as ExternalDefinition;
                if (ed != null && ed.GUID == guid.Value) return d;
            }
            return null;
        }

        private static Autodesk.Revit.DB.ForgeTypeId ResolveSpecTypeIdOrDefault(string s)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Text"] = "Autodesk.Revit.DB.SpecTypeId.String.Text",
                ["YesNo"] = "Autodesk.Revit.DB.SpecTypeId.Boolean.YesNo",
                ["Length"] = "Autodesk.Revit.DB.SpecTypeId.Length",
                ["Area"] = "Autodesk.Revit.DB.SpecTypeId.Area",
                ["Volume"] = "Autodesk.Revit.DB.SpecTypeId.Volume",
                ["Number"] = "Autodesk.Revit.DB.SpecTypeId.Number",
                ["Real"] = "Autodesk.Revit.DB.SpecTypeId.Number",
                ["Double"] = "Autodesk.Revit.DB.SpecTypeId.Number",
                ["実数"] = "Autodesk.Revit.DB.SpecTypeId.Number",
                ["数値"] = "Autodesk.Revit.DB.SpecTypeId.Number",
            };
            string path;
            if (!map.TryGetValue((s ?? string.Empty).Trim(), out path)) path = map["Text"];
            var fdt = TryResolveForgeTypeIdByPath(path);
            return fdt ?? Autodesk.Revit.DB.SpecTypeId.String.Text;
        }

        private static Autodesk.Revit.DB.ForgeTypeId TryResolveForgeTypeIdByPath(string path)
        {
            try
            {
                var parts = path.Split('.');
                if (parts.Length < 5) return null;

                // Resolve base type name: e.g. "Autodesk.Revit.DB.SpecTypeId" or "Autodesk.Revit.DB.GroupTypeId"
                string baseTypeName = string.Join(".", parts[0], parts[1], parts[2], parts[3]);
                var type = Type.GetType(baseTypeName);
                if (type == null)
                {
                    try
                    {
                        var asm = typeof(Autodesk.Revit.DB.Element).Assembly;
                        type = asm != null ? asm.GetType(baseTypeName) : null;
                    }
                    catch { type = null; }
                }
                if (type == null) return null;

                object current = null;
                for (int i = 4; i < parts.Length; i++)
                {
                    var name = parts[i];
                    var pi = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (pi != null)
                    {
                        current = pi.GetValue(null, null);
                        if (current is Autodesk.Revit.DB.ForgeTypeId) return (Autodesk.Revit.DB.ForgeTypeId)current;
                        if (current != null) type = current.GetType();
                        continue;
                    }
                    var nested = type.GetNestedType(name, System.Reflection.BindingFlags.Public);
                    if (nested != null) { type = nested; continue; }
                    return null;
                }
                return current as Autodesk.Revit.DB.ForgeTypeId;
            }
            catch { return null; }
        }

        private static Autodesk.Revit.DB.ForgeTypeId ResolveGroupTypeIdOrNull(string s)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return null;

                var raw = s.Trim();

                // Allow common localized UI labels (best-effort)
                // Note: Label differs by locale; this is a pragmatic mapping for Japanese UI.
                if (raw == "機械設備")
                {
                    raw = "Mechanical";
                }

                // Allow "GroupTypeId.Text" or fully qualified "Autodesk.Revit.DB.GroupTypeId.Text"
                string path;
                if (raw.StartsWith("GroupTypeId.", StringComparison.OrdinalIgnoreCase))
                {
                    path = "Autodesk.Revit.DB." + raw;
                }
                else if (raw.StartsWith("Autodesk.Revit.DB.GroupTypeId.", StringComparison.OrdinalIgnoreCase))
                {
                    path = raw;
                }
                else if (raw.StartsWith("PG_", StringComparison.OrdinalIgnoreCase))
                {
                    // Common BuiltInParameterGroup -> GroupTypeId mapping (best-effort)
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PG_TEXT"] = "Autodesk.Revit.DB.GroupTypeId.Text",
                        ["PG_DATA"] = "Autodesk.Revit.DB.GroupTypeId.Data",
                        ["PG_IDENTITY_DATA"] = "Autodesk.Revit.DB.GroupTypeId.IdentityData",
                        ["PG_CONSTRAINTS"] = "Autodesk.Revit.DB.GroupTypeId.Constraints",
                        ["PG_DIMENSIONS"] = "Autodesk.Revit.DB.GroupTypeId.Dimensions",
                        ["PG_MATERIALS"] = "Autodesk.Revit.DB.GroupTypeId.Materials",
                        ["PG_GEOMETRY"] = "Autodesk.Revit.DB.GroupTypeId.Geometry",
                        ["PG_PHASING"] = "Autodesk.Revit.DB.GroupTypeId.Phasing",
                        ["PG_STRUCTURAL"] = "Autodesk.Revit.DB.GroupTypeId.Structural",
                        ["PG_GRAPHICS"] = "Autodesk.Revit.DB.GroupTypeId.Graphics"
                    };

                    if (!map.TryGetValue(raw, out path)) return null;
                }
                else
                {
                    // Allow bare property name (e.g. "Text", "Data")
                    path = "Autodesk.Revit.DB.GroupTypeId." + raw;
                }

                return TryResolveForgeTypeIdByPath(path);
            }
            catch
            {
                return null;
            }
        }

        private static Autodesk.Revit.DB.BuiltInParameterGroup ParseParameterGroup(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Autodesk.Revit.DB.BuiltInParameterGroup.PG_TEXT;
            try { return (Autodesk.Revit.DB.BuiltInParameterGroup)Enum.Parse(typeof(Autodesk.Revit.DB.BuiltInParameterGroup), s, true); } catch { return Autodesk.Revit.DB.BuiltInParameterGroup.PG_TEXT; }
        }

        private static bool TryFindExistingBinding(BindingMap map, Definition def, out Definition existingDef, out ElementBinding existingBinding)
        {
            existingDef = null;
            existingBinding = null;

            if (map == null || def == null) return false;

            Guid? gDef = null;
            try
            {
                var ed = def as ExternalDefinition;
                if (ed != null && ed.GUID != Guid.Empty) gDef = ed.GUID;
            }
            catch { }

            string name = def.Name ?? string.Empty;

            var it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                var d = it.Key as Definition;
                if (d == null) continue;

                bool match = string.Equals(d.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase);
                if (!match && gDef.HasValue)
                {
                    try
                    {
                        var e2 = d as ExternalDefinition;
                        if (e2 != null && e2.GUID == gDef.Value) match = true;
                    }
                    catch { }
                }

                if (!match) continue;

                existingDef = d;
                existingBinding = it.Current as ElementBinding;
                return true;
            }

            return false;
        }

        private static void AddCategories(CategorySet dest, CategorySet src)
        {
            if (dest == null || src == null) return;
            foreach (Category c in src)
            {
                if (c == null) continue;
                try { dest.Insert(c); } catch { }
            }
        }
    }
}
