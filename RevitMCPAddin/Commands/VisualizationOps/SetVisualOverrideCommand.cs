// RevitMCPAddin/Commands/VisualizationOps/SetVisualOverrideCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    /// <summary>
    /// Override graphics for elements in a view (Surface+Cut solid fill, transparency, line colors).
    /// - Accepts elementId (single) or elementIds (multiple).
    /// - If the view has a View Template, the command performs no change and returns a VIEW_TEMPLATE_LOCK hint.
    /// </summary>
    public class SetVisualOverrideCommand : IRevitCommandHandler
    {
        public string CommandName => "set_visual_override";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // �r���[�����i����݊�: viewId ������/0 �̏ꍇ�̓A�N�e�B�u�O���t�B�b�N�r���[���̗p�j
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            View view = null;
            ElementId viewId = ElementId.InvalidElementId;
            if (reqViewId > 0)
            {
                viewId = Autodesk.Revit.DB.ElementIdCompat.From(reqViewId);
                view = doc.GetElement(viewId) as View;
            }
            if (view == null)
            {
                // ActiveGraphicalView �� null �̏ꍇ�� ActiveView (��O���t�B�b�N�͏��O)
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);
                if (view != null) viewId = view.Id;
            }

            // �I�v�V����: �����I�Ɉ��S��3D�r���[���쐬���ēK�p
            bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true;
            if (view == null && autoWorkingView)
            {
                using (var tx = new Transaction(doc, "Create Working 3D (SetVisualOverride)"))
                {
                    try
                    {
                        tx.Start();
                        var vtf = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                        if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                        var v3d = View3D.CreateIsometric(doc, vtf.Id);
                        v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                        tx.Commit();
                        view = v3d;
                        viewId = v3d.Id;
                        try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        // �Ō�̎�i: �����I�G���[�ő��ԋp�iUI�X�^�b�N�������j
                        return new
                        {
                            ok = false,
                            errorCode = "ERR_NO_VIEW",
                            msg = "�K�p��r���[�������ł��܂���ł����BviewId ���w�肵�Ă��������B",
                            detail = ex.Message
                        };
                    }
                }
            }
            if (view == null)
            {
                return new
                {
                    ok = false,
                    errorCode = "ERR_NO_VIEW",
                    msg = "�K�p��r���[�������ł��܂���ł����BviewId ���w�肵�Ă��������B"
                };
            }

            // ========== View Template ���o�Fdetach�I�v�V���� ==========
            bool templateApplied = view.ViewTemplateId != ElementId.InvalidElementId;
            int? templateViewId = templateApplied ? (int?)view.ViewTemplateId.IntValue() : null;
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (templateApplied && detachTemplate)
            {
                using (var tx = new Transaction(doc, "Detach View Template (SetVisualOverride)"))
                {
                    try
                    {
                        tx.Start();
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        tx.Commit();
                        templateApplied = false;
                        templateViewId = null;
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        RevitLogger.Warn("Template detach failed.", ex);
                    }
                }
            }
            if (templateApplied)
            {
                // View Template �K�p�r���[�ł͕`��ύX���s���Ȃ���
                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    count = 0,
                    skipped = new[] { new { reason = "View has a template; detach view template before calling set_visual_override." } },
                    errors = new object[0],
                    color = (object)null,
                    transparency = (int?)null,
                    templateApplied = true,
                    templateViewId,
                    skippedDueToTemplate = true,
                    errorCode = "VIEW_TEMPLATE_LOCK",
                    message = "View has a template; detach view template before calling set_visual_override.",
                    appliedTo = "skipped"
                };
            }

            // �v�fID�i�P��/�����j
            var elementIds = new List<ElementId>();
            if (p["elementId"] != null)
            {
                elementIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId")));
            }
            else if (p["elementIds"] != null && p["elementIds"].Any())
            {
                elementIds = p["elementIds"].Select(x => Autodesk.Revit.DB.ElementIdCompat.From((int)x)).ToList();
            }
            else
            {
                throw new InvalidOperationException("�p�����[�^ 'elementId' �܂��� 'elementIds' ���w�肵�Ă��������B");
            }

            // �F�E���ߓx�i�ȗ����͐�, 40%�j
            byte r = (byte)(p.Value<int?>("r") ?? 255);
            byte g = (byte)(p.Value<int?>("g") ?? 0);
            byte b = (byte)(p.Value<int?>("b") ?? 0);
            int transparency = p.Value<int?>("transparency") ?? 40;
            if (transparency < 0) transparency = 0;
            if (transparency > 100) transparency = 100;
            var color = new Color(r, g, b);

            // Solid Fill �p�^�[���i�L���b�V�����p�j
            var solidPatternId = SolidFillCache.GetOrBuild(doc);

            var ogs = new OverrideGraphicSettings();
            // ��
            ogs.SetProjectionLineColor(color);
            ogs.SetCutLineColor(color);
            // Surface�i�O�i�E�w�i�j
            ogs.SetSurfaceForegroundPatternId(solidPatternId);
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetSurfaceForegroundPatternVisible(true);
            ogs.SetSurfaceBackgroundPatternId(solidPatternId);
            ogs.SetSurfaceBackgroundPatternColor(color);
            ogs.SetSurfaceBackgroundPatternVisible(true);
            // Cut�i�O�i�E�w�i�j
            ogs.SetCutForegroundPatternId(solidPatternId);
            ogs.SetCutForegroundPatternColor(color);
            ogs.SetCutForegroundPatternVisible(true);
            ogs.SetCutBackgroundPatternId(solidPatternId);
            ogs.SetCutBackgroundPatternColor(color);
            ogs.SetCutBackgroundPatternVisible(true);
            // ���ߓx
            ogs.SetSurfaceTransparency(transparency);

            // �o�b�`�K�p + Regenerate/Refresh
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 800));
            int maxMillisPerTx = Math.Max(500, Math.Min(10000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var errors = new List<object>();
            int applied = 0;
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;
            while (nextIndex < elementIds.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var tx = new Transaction(doc, "Set Visual Override (batched)"))
                {
                    try
                    {
                        tx.Start();
                        int end = Math.Min(elementIds.Count, nextIndex + batchSize);
                        for (int i = nextIndex; i < end; i++)
                        {
                            var id = elementIds[i];
                            try
                            {
                                view.SetElementOverrides(id, ogs);
                                applied++;
                            }
                            catch (Exception ex)
                            {
                                RevitLogger.Error($"SetElementOverrides failed for {id.IntValue()}", ex);
                                errors.Add(new { elementId = id.IntValue(), reason = ex.Message });
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        errors.Add(new { reason = $"transaction failed: {ex.Message}" });
                        break;
                    }
                }
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break;
            }

            return new
            {
                ok = true,
                viewId = viewId.IntValue(),
                count = applied,
                errors,
                color = new { r, g, b },
                transparency,
                templateApplied = false,
                templateViewId = (int?)null,
                skippedDueToTemplate = false,
                appliedTo = "view",
                completed = nextIndex >= elementIds.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds
            };
        }

        private static class SolidFillCache
        {
            private static readonly object _gate = new object();
            private static readonly Dictionary<int, ElementId> _map = new Dictionary<int, ElementId>();

            public static ElementId GetOrBuild(Document doc)
            {
                if (doc == null) return ElementId.InvalidElementId;
                int key = doc.GetHashCode();
                lock (_gate)
                {
                    if (_map.TryGetValue(key, out var id) && id != null && id != ElementId.InvalidElementId)
                        return id;
                    var fps = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>();
                    foreach (var f in fps)
                    {
                        try
                        {
                            var fp = f.GetFillPattern();
                            if (fp != null && fp.IsSolidFill)
                            {
                                _map[key] = f.Id;
                                return f.Id;
                            }
                        }
                        catch
                        {
                        }
                    }
                    _map[key] = ElementId.InvalidElementId;
                    return ElementId.InvalidElementId;
                }
            }
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {i++}";
            }
            return name;
        }
    }
}



