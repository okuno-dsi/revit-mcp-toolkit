// ================================================================
// File: Commands/Room/GetRoomOpeningsCommand.cs  (Revit 2023 / .NET 4.8)
// Fixes : FromRoom/ToRoom はインデクサではなく getter メソッドを使用
//         → fi.get_FromRoom(targetPhase) / fi.get_ToRoom(targetPhase)
//         Room 型は Autodesk.Revit.DB.Architecture.Room を明示
// Phase 解決優先度:
//   params.phaseId → Room の "ROOM_PHASE" → ActiveView の Phase → Doc の最後の Phase
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture; // Room
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomOpeningsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_openings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("roomId", out var idTok))
                return ResultUtil.Err("roomId を指定してください。");

            var room = doc.GetElement(new ElementId(idTok.Value<int>())) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return ResultUtil.Err("Room が見つかりません。");

            // ---------- Phase の解決 ----------
            ElementId phaseId = ElementId.InvalidElementId;

            // 1) 明示指定
            if (p.TryGetValue("phaseId", out var phaseTok))
                phaseId = new ElementId(phaseTok.Value<int>());

            // 2) Room の 'ROOM_PHASE' パラメータ
            if (phaseId == ElementId.InvalidElementId)
                phaseId = room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() ?? ElementId.InvalidElementId;

            // 3) アクティブビューのフェーズ
            if (phaseId == ElementId.InvalidElementId)
                phaseId = uidoc?.ActiveView?.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId;

            // 4) ドキュメントの最後のフェーズ（フォールバック）
            if (phaseId == ElementId.InvalidElementId)
            {
                Phase fallback = null;
                foreach (Phase ph in doc.Phases) fallback = ph;
                if (fallback != null) phaseId = fallback.Id;
            }

            var targetPhase = doc.GetElement(phaseId) as Phase;
            if (targetPhase == null)
                return ResultUtil.Err("対象フェーズ(phaseId)を解決できませんでした。");

            // ---------- 対象カテゴリ ----------
            var cats = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_SpecialityEquipment
            };

            var result = new Dictionary<string, List<object>>
            {
                ["Doors"] = new List<object>(),
                ["Windows"] = new List<object>(),
                ["PlumbingFixtures"] = new List<object>(),
                ["SpecialityEquipment"] = new List<object>()
            };

            foreach (var bic in cats)
            {
                var insts = new FilteredElementCollector(doc)
                    .OfCategory(bic).WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .ToList();

                foreach (var fi in insts)
                {
                    Autodesk.Revit.DB.Architecture.Room from = null, to = null;
                    try { from = fi.get_FromRoom(targetPhase); } catch { /* ignore */ }
                    try { to = fi.get_ToRoom(targetPhase); } catch { /* ignore */ }

                    bool hit = (from?.Id == room.Id) || (to?.Id == room.Id);
                    if (!hit) continue;

                    var lp = fi.Location as LocationPoint;
                    var pos = lp?.Point;

                    var dto = new
                    {
                        elementId = fi.Id.IntegerValue,
                        uniqueId = fi.UniqueId ?? "",
                        typeId = fi.GetTypeId().IntegerValue,
                        category = bic.ToString(),
                        direction = (from?.Id == room.Id && to?.Id == room.Id) ? "through" :
                                    (from?.Id == room.Id) ? "from" :
                                    (to?.Id == room.Id) ? "to" : "unknown",
                        location = pos == null ? null : new
                        {
                            x = Math.Round(UnitHelper.FtToMm(pos.X), 3),
                            y = Math.Round(UnitHelper.FtToMm(pos.Y), 3),
                            z = Math.Round(UnitHelper.FtToMm(pos.Z), 3)
                        }
                    };

                    switch (bic)
                    {
                        case BuiltInCategory.OST_Doors: result["Doors"].Add(dto); break;
                        case BuiltInCategory.OST_Windows: result["Windows"].Add(dto); break;
                        case BuiltInCategory.OST_PlumbingFixtures: result["PlumbingFixtures"].Add(dto); break;
                        case BuiltInCategory.OST_SpecialityEquipment: result["SpecialityEquipment"].Add(dto); break;
                    }
                }
            }

            return ResultUtil.Ok(new
            {
                room = new { roomId = room.Id.IntegerValue, name = room.Name ?? "" },
                phase = new { phaseId = targetPhase.Id.IntegerValue, name = targetPhase.Name ?? "" },
                openings = result,
                units = UnitHelper.DefaultUnitsMeta()
            });
        }
    }
}
