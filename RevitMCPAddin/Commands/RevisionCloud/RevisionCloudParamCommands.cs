// ================================================================
// File: Commands/RevisionCloud/RevisionCloudParamCommands.cs
// Purpose: リビジョンクラウドのインスタンスパラメータ取得・設定
// Target : .NET Framework 4.8 / C# 8 / Revit 2023 API
// Notes  : Doubleは mm 出力、mm→ft 入力変換
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    internal static class RcParamUnits
    {
        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
    }

    /// <summary>
    /// リビジョンクラウドのパラメータ一覧を取得
    /// </summary>
    public class GetRevisionCloudParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_revision_cloud_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;
            int elemId = p.Value<int>("elementId");

            var cloud = doc.GetElement(new ElementId(elemId)) as Autodesk.Revit.DB.RevisionCloud;
            if (cloud == null) return new { ok = false, msg = $"RevisionCloud not found: {elemId}" };

            var list = new List<object>();
            foreach (Parameter param in cloud.Parameters)
            {
                if (param.StorageType == StorageType.None) continue;
                string valueStr = param.AsValueString() ?? param.AsString() ?? "";
                object val = valueStr;

                switch (param.StorageType)
                {
                    case StorageType.Double:
                        try { val = RcParamUnits.FtToMm(param.AsDouble()); }
                        catch { val = param.AsDouble(); }
                        break;
                    case StorageType.Integer:
                        val = param.AsInteger();
                        break;
                    case StorageType.ElementId:
                        val = param.AsElementId().IntegerValue;
                        break;
                }

                list.Add(new
                {
                    name = param.Definition?.Name,
                    id = param.Id.IntegerValue,
                    storageType = param.StorageType.ToString(),
                    isReadOnly = param.IsReadOnly,
                    value = val
                });
            }

            return new
            {
                ok = true,
                elementId = elemId,
                totalCount = list.Count,
                parameters = list
            };
        }
    }

    /// <summary>
    /// リビジョンクラウドのパラメータを設定
    /// </summary>
    public class SetRevisionCloudParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_revision_cloud_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;
            int elemId = p.Value<int>("elementId");
            string paramName = p.Value<string>("paramName");
            var valTok = p["value"];

            if (string.IsNullOrEmpty(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid は必須です。" };
            if (valTok == null) return new { ok = false, msg = "value は必須です。" };

            var cloud = doc.GetElement(new ElementId(elemId)) as Autodesk.Revit.DB.RevisionCloud;
            if (cloud == null) return new { ok = false, msg = $"RevisionCloud not found: {elemId}" };

            var param = ParamResolver.ResolveByPayload(cloud, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' is read-only." };

            using (var tx = new Transaction(doc, $"Set RevisionCloud Param '{paramName}'"))
            {
                tx.Start();
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.String:
                            param.Set(valTok.Type == JTokenType.Null ? "" : valTok.ToString());
                            break;
                        case StorageType.Integer:
                            param.Set(valTok.Value<int>());
                            break;
                        case StorageType.Double:
                            param.Set(RcParamUnits.MmToFt(valTok.Value<double>()));
                            break;
                        case StorageType.ElementId:
                            param.Set(new ElementId(valTok.Value<int>()));
                            break;
                        default:
                            tx.RollBack();
                            return new { ok = false, msg = $"Unsupported StorageType: {param.StorageType}" };
                    }
                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }
}
