// ================================================================
// File: Commands/Rooms/PlaceRoomInCircuitCommand.cs
// Desc: 指定レベルの回路Indexに部屋を配置（または任意XY点に配置）
// API:  method = "place_room_in_circuit"
// ================================================================

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rooms
{
    public class PlaceRoomInCircuitCommand : IRevitCommandHandler
    {
        public string CommandName => "place_room_in_circuit";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                    return new { ok = false, msg = "アクティブドキュメントがありません。", code = "NO_DOCUMENT" };

                var p = (JObject)(cmd.Params ?? new JObject());
                int levelId = Get(p, "levelId", 0);
                if (levelId == 0)
                    return new { ok = false, msg = "levelId が必要です。", code = "ARG_MISSING" };

                var level = doc.GetElement(new ElementId(levelId)) as Level;
                if (level == null)
                    return new { ok = false, msg = $"levelId={levelId} は Level ではありません。", code = "BAD_LEVEL" };

                string name = Get<string>(p, "name", null);
                string number = Get<string>(p, "number", null);
                string dept = Get<string>(p, "dept", null);
                int? circuitIndexOpt = Get<int?>(p, "circuitIndex", null);

                Autodesk.Revit.DB.Architecture.Room room = null;
                string placedBy = null;

                using (var t = new Transaction(doc, "[MCP] Place Room"))
                {
                    t.Start();

                    if (circuitIndexOpt.HasValue)
                    {
                        var topo = doc.get_PlanTopology(level);
                        if (topo == null)
                            return new { ok = false, msg = "PlanTopology を取得できません。フェーズ/ビュー条件を確認してください。", code = "NO_TOPOLOGY" };

                        int want = circuitIndexOpt.Value;
                        int idx = 0;
                        PlanCircuit target = null;
                        foreach (PlanCircuit c in topo.Circuits)
                        {
                            if (idx == want) { target = c; break; }
                            idx++;
                        }
                        if (target == null)
                            return new { ok = false, msg = $"circuitIndex={want} が範囲外です。", code = "INDEX_OUT_OF_RANGE" };

                        if (target.IsRoomLocated)
                            return new { ok = false, msg = "指定回路には既に部屋が配置済みです。", code = "ALREADY_HAS_ROOM" };

                        // 置ける/置けないの判定は例外で判断
                        room = doc.Create.NewRoom(null, target);
                        placedBy = "circuit";
                    }
                    else
                    {
                        // 任意点配置（UVはレベル平面のXY）
                        var point = p["point"] as JObject;
                        if (point == null)
                            return new { ok = false, msg = "circuitIndex か point のいずれかが必要です。", code = "ARG_MISSING" };

                        var (xFt, yFt, zFt) = ReadPointFeet(point);
                        var uv = new UV(xFt, yFt);
                        room = doc.Create.NewRoom(level, uv);
                        placedBy = "point";
                    }

                    if (room == null)
                        return new { ok = false, msg = "部屋の配置に失敗しました。", code = "ROOM_CREATE_FAILED" };

                    // 名前・番号・その他パラメータ設定
                    if (!string.IsNullOrEmpty(name))
                    {
                        var pName = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                        if (pName != null && !pName.IsReadOnly) pName.Set(name);
                    }
                    if (!string.IsNullOrEmpty(number))
                    {
                        var pNum = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                        if (pNum != null && !pNum.IsReadOnly) pNum.Set(number);
                    }
                    if (!string.IsNullOrEmpty(dept))
                    {
                        var pDept = room.LookupParameter("Department") ?? room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                        if (pDept != null && !pDept.IsReadOnly) pDept.Set(dept);
                    }

                    t.Commit();
                }

                return new
                {
                    ok = true,
                    roomId = room?.Id?.IntegerValue ?? 0,
                    placedBy,
                    msg = "room placed"
                };
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                RevitLogger.Error($"place_room_in_circuit arg error: {ex}");
                return new { ok = false, msg = ex.Message, code = "ARG_ERROR" };
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                RevitLogger.Error($"place_room_in_circuit invalid op: {ex}");
                return new { ok = false, msg = ex.Message, code = "INVALID_OPERATION" };
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"place_room_in_circuit error: {ex}");
                return new { ok = false, msg = ex.Message, code = "EXCEPTION" };
            }
        }

        // ---------- Helpers ----------

        private static T Get<T>(JObject p, string key, T def)
        {
            try
            {
                var tok = p[key];
                if (tok == null || tok.Type == JTokenType.Null) return def;
                return tok.Value<T>();
            }
            catch { return def; }
        }

        private static (double xFt, double yFt, double zFt) ReadPointFeet(JObject pt)
        {
            double x = pt.Value<double?>("x") ?? 0.0;
            double y = pt.Value<double?>("y") ?? 0.0;
            double z = pt.Value<double?>("z") ?? 0.0;
            string units = (pt.Value<string>("units") ?? "mm").Trim().ToLowerInvariant();

            if (units == "ft")
            {
                return (x, y, z);
            }
            else if (units == "m")
            {
                return (
                    UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Meters),
                    UnitUtils.ConvertToInternalUnits(y, UnitTypeId.Meters),
                    UnitUtils.ConvertToInternalUnits(z, UnitTypeId.Meters)
                );
            }
            else
            {
                return (
                    UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Millimeters),
                    UnitUtils.ConvertToInternalUnits(y, UnitTypeId.Millimeters),
                    UnitUtils.ConvertToInternalUnits(z, UnitTypeId.Millimeters)
                );
            }
        }
    }
}
