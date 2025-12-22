// ================================================================
// File: Commands/DocumentOps/GetProjectInfoCommand.cs
// 概要 : Revit の Project Information（プロジェクト情報）を取得
// 返却 : { ok, elementId, uniqueId, projectName, projectNumber, clientName,
//         status, issueDate, address, site:{placeName, latitude, longitude, timeZone},
//         parameters:[{ name, id, storageType, isReadOnly, units, value, display }] }
// 備考 : Double は SpecType に応じて mm / m² / m³ / deg へ変換し units を付与
// 対応 : Revit 2023 / .NET Framework 4.8 / C# 8
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.DocumentOps
{
    public class GetProjectInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            try
            {
                var pi = doc.ProjectInformation;
                if (pi == null)
                {
                    return new { ok = false, msg = "ProjectInformation 要素が見つかりません。" };
                }

                // 代表的フィールド（トップレベルに昇格）
                string projectName = SafeGetString(pi, BuiltInParameter.PROJECT_NAME);
                string projectNumber = SafeGetString(pi, BuiltInParameter.PROJECT_NUMBER);
                string clientName = SafeGetString(pi, BuiltInParameter.CLIENT_NAME);
                string status = SafeGetString(pi, BuiltInParameter.PROJECT_STATUS);
                string issueDate = SafeGetString(pi, BuiltInParameter.PROJECT_ISSUE_DATE);
                string address = SafeGetString(pi, BuiltInParameter.PROJECT_ADDRESS);

                // すべてのパラメータを列挙
                var paramItems = new List<object>();
                foreach (Parameter p in pi.Parameters)
                {
                    // 型と読み取り専用フラグ
                    var storage = p.StorageType;
                    bool isReadOnly = p.IsReadOnly;

                    // 表示用（プロジェクトの書式に従う文字列）
                    string display = p.AsValueString() ?? p.AsString() ?? string.Empty;

                    // 変換後の値と単位
                    object valueObj;
                    string units = null;

                    switch (storage)
                    {
                        case StorageType.String:
                            valueObj = p.AsString() ?? string.Empty;
                            break;

                        case StorageType.Integer:
                            valueObj = p.AsInteger();
                            break;

                        case StorageType.ElementId:
                            valueObj = p.AsElementId()?.IntValue() ?? 0;
                            break;

                        case StorageType.Double:
                            ConvertDoubleBySpec(p, out valueObj, out units);
                            break;

                        default:
                            valueObj = null;
                            break;
                    }

                    paramItems.Add(new
                    {
                        name = p.Definition?.Name ?? "(Unnamed)",
                        id = p.Id.IntValue(),
                        storageType = storage.ToString(),
                        isReadOnly,
                        units,
                        value = valueObj,
                        display
                    });
                }

                // サイト情報（あれば）
                var site = doc.SiteLocation;
                object siteObj = null;
                if (site != null)
                {
                    siteObj = new
                    {
                        placeName = site.PlaceName,
                        latitude = site.Latitude,     // ラジアンではなく度数法（Revitは度単位を返す）
                        longitude = site.Longitude,
                        timeZone = site.TimeZone     // UTC からの時間差（例: 9.0）
                    };
                }

                return new
                {
                    ok = true,
                    elementId = pi.Id.IntValue(),
                    uniqueId = pi.UniqueId,
                    projectName,
                    projectNumber,
                    clientName,
                    status,
                    issueDate,
                    address,
                    site = siteObj,
                    // 単位表記の目安（Double の換算方針）
                    inputUnits = new { Length = "mm", Area = "m2", Volume = "m3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" },
                    parameters = paramItems
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static string SafeGetString(ProjectInfo pi, BuiltInParameter bip)
        {
            try
            {
                var p = pi.get_Parameter(bip);
                if (p == null) return string.Empty;

                // 文字列優先、なければ表示値
                var s = p.AsString();
                if (!string.IsNullOrEmpty(s)) return s;
                var v = p.AsValueString();
                return v ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static void ConvertDoubleBySpec(Parameter p, out object value, out string units)
        {
            // Revit 2023 以降: Parameter.Definition.GetDataType() で SpecTypeId を取得
            // SpecType に応じて UI 親和の単位へ変換
            value = null;
            units = null;

            ForgeTypeId spec;
            try
            {
                spec = p.Definition.GetDataType();
            }
            catch
            {
                // 取得できない場合はディスプレイ文字列を返す
                value = p.AsValueString() ?? p.AsDouble().ToString();
                return;
            }

            double raw = p.AsDouble();

            if (spec == SpecTypeId.Length)
            {
                value = Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.Millimeters), 3);
                units = "mm";
            }
            else if (spec == SpecTypeId.Area)
            {
                value = Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters), 3);
                units = "m2";
            }
            else if (spec == SpecTypeId.Volume)
            {
                value = Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters), 3);
                units = "m3";
            }
            else if (spec == SpecTypeId.Angle)
            {
                value = Math.Round(raw * 180.0 / Math.PI, 6);
                units = "deg";
            }
            else
            {
                // その他の SpecType は内部値（ft 等）のまま
                value = raw;
                units = null; // 未知
            }
        }
    }
}

