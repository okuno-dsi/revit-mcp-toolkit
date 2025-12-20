#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Autodesk.Revit.UI.DockablePanes;

namespace RevitMCPAddin.RevitUI
{
    public class DebugPaneIntrospectionCommand : IRevitCommandHandler
    {
        public string CommandName => "debug_dump_dockable_panes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var types = new List<object>();

            // 候補タイプを全部集める
            var t1 = typeof(DockablePanes).GetNestedType("BuiltInDockablePanes", BindingFlags.Public);
            var t2 = typeof(BuiltInDockablePanes);
            var asmTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => {
                    try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                })
                .Where(t => t.IsClass && t.Name.Equals("BuiltInDockablePanes", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var all = new List<Type>();
            if (t1 != null) all.Add(t1);
            if (t2 != null) all.Add(t2);
            foreach (var t in asmTypes)
            {
                if (!all.Contains(t)) all.Add(t);
            }

            foreach (var t in all)
            {
                var rec = new JObject();
                rec["assembly"] = t.Assembly.GetName().Name ?? "";
                rec["namespace"] = t.Namespace ?? "";
                rec["typeName"] = t.FullName ?? t.Name;
                rec["isEnum"] = t.IsEnum;

                var fields = new JArray();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var fr = new JObject();
                    fr["field"] = f.Name;
                    try
                    {
                        var v = f.GetValue(null);
                        fr["valueType"] = v == null ? "<null>" : v.GetType().FullName;
                        // もし Guid プロパティがあれば拾う
                        try
                        {
                            var gp = v?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
                            if (gp != null && gp.GetValue(v) is Guid g) fr["guidProp"] = g.ToString("D");
                        }
                        catch { /* ignore */ }
                    }
                    catch (Exception ex)
                    {
                        fr["valueType"] = "<ex: " + ex.GetType().Name + ">";
                    }
                    fields.Add(fr);
                }
                rec["fields"] = fields;
                types.Add(rec);
            }

            return new { ok = true, types };
        }
    }
}
