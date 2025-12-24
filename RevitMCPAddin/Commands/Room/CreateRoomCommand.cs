// ================================================================
// File: Commands/Room/CreateRoomCommand.cs  (統合版: Safe+Strict)
// Revit 2023 / .NET Framework 4.8
//
// ポイント:
//  - levelId または levelName でレベル解決（両対応）
//  - 既存Roomの事前チェック（既定: 有効）→ 重複作成回避（mode="existing"）
//  - strictEnclosure（既定: true）で囲い無し/面積ゼロは Rollback→エラー
//  - 失敗はすべて即 Rollback & エラー返却（ok:false, code, msg, detail, metrics）
//  - タグは autoTag（既定: true）, tagTypeId指定可（失敗は握りつぶす）
//  - 入力XYは mm（InputPointReader）/ 返却に location {xMm,yMm} と units メタを含む
//
// 追加パラメータ（すべて任意）:
//  - levelId:int | levelName:string
//  - autoTag:bool = true
//  - tagTypeId:int = 0
//  - strictEnclosure:bool = true
//  - minAreaMm2:double = 1e-3
//  - checkExisting:bool = true
// ================================================================
#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class CreateRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "create_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return new { ok = false, code = "NO_ACTIVE_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            // -------- レベル解決（levelId or levelName）--------
            Level? level = null;
            var levelId = p.Value<int?>("levelId");
            if (levelId.HasValue && levelId.Value > 0)
            {
                level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId.Value)) as Level;
            }
            else
            {
                var levelName = (p.Value<string>("levelName") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(lv => string.Equals(lv.Name, levelName, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (level == null)
                return new { ok = false, code = "NOT_FOUND_LEVEL", msg = "Level not found." };

            // -------- 位置（mm）取得 --------
            if (!InputPointReader.TryReadXYMm(p, out var xMm, out var yMm))
                return new { ok = false, code = "INVALID_INPUT", msg = "x, y (mm) are required. (x/y, location.{x,y}, point.{x,y}, or [x,y])" };

            // 既定パラメータ
            bool autoTag = p.Value<bool?>("autoTag") ?? true;
            int tagTypeIdParam = p.Value<int?>("tagTypeId") ?? 0;
            bool strictEnclosure = p.Value<bool?>("strictEnclosure") ?? true;
            double minAreaMm2 = p.Value<double?>("minAreaMm2") ?? 1e-3;
            bool checkExisting = p.Value<bool?>("checkExisting") ?? true;

            // -------- 既存Room事前チェック（重複作成回避）--------
            if (checkExisting)
            {
                try
                {
                    var xyz = new XYZ(UnitHelper.MmToFt(xMm), UnitHelper.MmToFt(yMm), 0.0);
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType();

                    foreach (var el in rooms)
                    {
                        if (el is Autodesk.Revit.DB.Architecture.Room rr)
                        {
                            try
                            {
                                if (rr.IsPointInRoom(xyz))
                                {
                                    return new
                                    {
                                        ok = true,
                                        mode = "existing",
                                        elementId = rr.Id.IntValue(),
                                        name = rr.Name,
                                        location = new { xMm, yMm },
                                        units = UnitHelper.DefaultUnitsMeta()
                                    };
                                }
                            }
                            catch { /* IsPointInRoom が例外を出す場合は無視 */ }
                        }
                    }
                }
                catch { /* 事前チェック失敗は致命ではないので続行 */ }
            }

            // -------- ルーム作成トランザクション --------
            using (var tx = new Transaction(doc, "Create Room"))
            {
                tx.Start();

                // FailureHandlingOptions (centralized)
                try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { }

                Autodesk.Revit.DB.Architecture.Room? room = null;
                try
                {
                    var uv = UnitHelper.MmToInternalUV(xMm, yMm, doc);
                    room = doc.Create.NewRoom(level, uv);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException oce)
                {
                    try { tx.RollBack(); } catch { }
                    return new { ok = false, code = "OP_CANCELED", msg = "Room placement canceled.", detail = oce.Message };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { }
                    return new { ok = false, code = "CREATE_FAILED", msg = "Room placement failed.", detail = ex.Message };
                }

                // 生成後の妥当性チェック（Locationなど）
                var locPt = (room?.Location as LocationPoint)?.Point;
                if (room == null || locPt == null)
                {
                    try { tx.RollBack(); } catch { }
                    return new { ok = false, code = "INVALID_LOCATION", msg = "Room placement failed: invalid location or null room." };
                }

                // 囲いチェック（strictEnclosure=true なら面積ゼロ相当は失敗に）
                if (strictEnclosure)
                {
                    double areaFt2 = room.Area; // 0 の場合あり
                    double areaMm2 = UnitHelper.Ft2ToMm2(areaFt2);
                    if (areaMm2 <= minAreaMm2)
                    {
                        try { tx.RollBack(); } catch { }
                        return new
                        {
                            ok = false,
                            code = "NOT_ENCLOSED",
                            msg = "Room placement failed: not enclosed (area is zero or below threshold).",
                            metrics = new { areaMm2 }
                        };
                    }
                }

                // -------- タグ（ベストエフォート）--------
                int tagIdOut = 0;
                if (autoTag)
                {
                    try
                    {
                        var view = uidoc?.ActiveView;
                        if (view != null)
                        {
                            FamilySymbol? symbol = null;
                            if (tagTypeIdParam > 0)
                            {
                                symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tagTypeIdParam)) as FamilySymbol;
                            }
                            if (symbol == null)
                            {
                                symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(s => s.Category != null && s.Category.Id.IntValue() == (int)BuiltInCategory.OST_RoomTags);
                            }

                            if (symbol != null)
                            {
                                if (!symbol.IsActive) symbol.Activate();
                                XYZ head = (room.Location as LocationPoint)?.Point ?? XYZ.Zero;
                                var reference = new Reference(room);
                                var tag = IndependentTag.Create(doc, symbol.Id, view.Id, reference, false, TagOrientation.Horizontal, head);
                                if (tag != null) tagIdOut = tag.Id.IntValue();
                            }
                        }
                    }
                    catch
                    {
                        // タグ作成失敗は握りつぶす（Room自体の成功は維持）
                    }
                }

                var txStatus = tx.Commit();
                if (txStatus != TransactionStatus.Committed)
                {
                    return new
                    {
                        ok = false,
                        code = "TX_NOT_COMMITTED",
                        msg = "Transaction did not commit.",
                        detail = new { transactionStatus = txStatus.ToString() }
                    };
                }

                return new
                {
                    ok = true,
                    mode = "created",
                    elementId = room.Id.IntValue(),
                    name = room.Name,
                    tagId = (tagIdOut > 0 ? (int?)tagIdOut : null),
                    location = new { xMm, yMm },
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
        }

        // ---- 警告抑制用（モーダルを塞がないようにする）----
        private sealed class SuppressWarningsPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                try
                {
                    foreach (var f in a.GetFailureMessages())
                    {
                        // エラーは流さず、警告レベルはすべて削除
                        if (f.GetSeverity() == FailureSeverity.Warning)
                            a.DeleteWarning(f);
                    }
                }
                catch { /* 失敗しても無視 */ }

                return FailureProcessingResult.Continue;
            }
        }
    }
}


