// ================================================================
// File: Commands/Space/GetSpaceParamsCommand.cs
// - UnitHelperの長さ/面積/体積/角度変換を使用
// - DataType(ForgeTypeId) を Definition.GetDataType().TypeId で併記
// - 返却形は { ok, totalCount, parameters } に統一
// - paging: skip / count （count=0 でメタのみ）
// Revit 2023 / .NET Framework 4.8 / C# 8
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using MechSpace = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Space
{
    public class GetSpaceParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, message = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            // -------- required: elementId --------
            if (!p.TryGetValue("elementId", out var eidToken))
                return new { ok = false, message = "Parameter 'elementId' is required." };
            int elementId = eidToken.Value<int>();

            var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as MechSpace;
            if (space == null)
                return new { ok = false, message = $"Space not found: {elementId}" };

            // -------- paging --------
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            // -------- core enumeration --------
            var allParams = space.Parameters
                .Cast<Parameter>()
                .Select(prm =>
                {
                    // ----- convert value by storage + spec -----
                    object val = null;
                    string unitLabel = null; // optional unit label for doubles

                    switch (prm.StorageType)
                    {
                        case StorageType.Double:
                            switch (UnitHelper.ClassifyDoubleParameter(prm))
                            {
                                case UnitHelper.DoubleKind.Length:
                                    val = Math.Round(UnitHelper.InternalToMm(prm.AsDouble(), doc), 3);
                                    unitLabel = "mm";
                                    break;

                                case UnitHelper.DoubleKind.Area:
                                    val = Math.Round(UnitHelper.InternalToSqm(prm.AsDouble()), 6);
                                    unitLabel = "m2";
                                    break;

                                case UnitHelper.DoubleKind.Volume:
                                    val = Math.Round(UnitHelper.InternalToCubicMeters(prm.AsDouble()), 6);
                                    unitLabel = "m3";
                                    break;

                                case UnitHelper.DoubleKind.Angle:
                                    val = Math.Round(UnitHelper.InternalToDeg(prm.AsDouble()), 6);
                                    unitLabel = "deg";
                                    break;

                                default:
                                    // 未分類のDoubleは内部値をそのまま返す（互換・デバッグ用）
                                    val = new
                                    {
                                        rawInternal = prm.AsDouble(),
                                        unit = "internal",
                                        note = "Unknown/unsupported unit; not auto-converted."
                                    };
                                    break;
                            }
                            break;

                        case StorageType.Integer:
                            val = prm.AsInteger();
                            break;

                        case StorageType.String:
                            val = prm.AsString() ?? string.Empty;
                            break;

                        case StorageType.ElementId:
                            val = prm.AsElementId().IntValue();
                            break;
                    }

                    // ----- dataType : ForgeTypeId(TypeId) -----
                    // Revit 2023+: Definition.GetDataType() が null の可能性あり
                    string dataType = null;
                    try
                    {
                        var f = prm.Definition?.GetDataType();
                        // ForgeTypeId.TypeId は string（例: "autodesk.spec.aec:length-1.0.0"）
                        dataType = f?.TypeId;
                    }
                    catch
                    {
                        dataType = null;
                    }

                    // ----- shape result -----
                    var item = new
                    {
                        name = prm.Definition?.Name ?? "(no name)",
                        id = prm.Id.IntValue(),
                        storageType = prm.StorageType.ToString(),
                        isReadOnly = prm.IsReadOnly,
                        dataType,            // ★追加：Forge Spec TypeId
                        value = val,
                        unit = unitLabel     // Doubleの場合のみ "mm"/"m2"/"m3"/"deg" を設定
                    };

                    return item;
                })
                // 空文字列などは除外（人間可読性のため）
                .Where(x => x.value != null && (!(x.value is string s) || !string.IsNullOrEmpty(s)))
                .ToList();

            int totalCount = allParams.Count;

            // meta-only: count=0（skip=0）のとき件数のみ返す
            if (skip == 0 && p.ContainsKey("count") && (p.Value<int?>("count") ?? -1) == 0)
                return new { ok = true, totalCount, parameters = Array.Empty<object>() };

            var parameters = allParams.Skip(skip).Take(count).ToList();

            return new { ok = true, totalCount, parameters };
        }
    }
}


