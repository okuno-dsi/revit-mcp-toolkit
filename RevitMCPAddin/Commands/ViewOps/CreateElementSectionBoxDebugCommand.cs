// File: Commands/ViewOps/CreateElementSectionBoxDebugCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// JSON-RPC: create_element_sectionbox_debug
    /// SectionBox 専用のデバッグ用コマンド。
    /// - 指定要素ごとに ViewFamily.Section の断面ビューを作成。
    /// - 使用した方向ベクトルや BoundingBox の座標（world / view local）を返す。
    /// - Elevation へのフォールバックは行わない（失敗時は skipped として返す）。
    /// </summary>
    public class CreateElementSectionBoxDebugCommand : IRevitCommandHandler
    {
        public string CommandName => "create_element_sectionbox_debug";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();

            // ターゲット要素 ID
            var elementIds = new List<ElementId>();
            var idsToken = p["elementIds"] as JArray;
            if (idsToken != null)
            {
                foreach (var t in idsToken)
                {
                    try
                    {
                        int id = t.Type == JTokenType.Object
                            ? ((JObject)t).Value<int>("elementId")
                            : t.Value<int>();
                        if (id > 0)
                            elementIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(id));
                    }
                    catch { }
                }
            }

            bool fromSelection = p.Value<bool?>("fromSelection") ?? false;
            if (elementIds.Count == 0 && fromSelection && uidoc != null)
            {
                try
                {
                    elementIds.AddRange(uidoc.Selection.GetElementIds());
                }
                catch { }
            }

            elementIds = elementIds
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .GroupBy(id => id.IntValue())
                .Select(g => g.First())
                .ToList();

            if (elementIds.Count == 0)
                return new { ok = false, msg = "elementIds が空であり、選択要素も取得できませんでした。" };

            // ViewFamilyType.Section を解決
            var vftSection = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);

            if (vftSection == null)
                return new { ok = false, msg = "ViewFamily.Section の ViewFamilyType が見つかりません。" };

            int viewScale = p.Value<int?>("viewScale") ?? 50;
            if (viewScale <= 0) viewScale = 50;

            double cropMarginMm = p.Value<double?>("cropMargin_mm") ?? 200.0;
            if (cropMarginMm < 0) cropMarginMm = 0.0;

            double offsetMm = p.Value<double?>("offsetDistance_mm") ?? 1500.0;
            if (offsetMm < 0) offsetMm = 0.0;

            var created = new List<object>();
            var skipped = new List<object>();

            using (var tx = new Transaction(doc, "Create Element SectionBox Debug Views"))
            {
                tx.Start();

                foreach (var eid in elementIds)
                {
                    try
                    {
                        var elem = doc.GetElement(eid);
                        if (elem == null)
                        {
                            skipped.Add(new { elementId = eid.IntValue(), reason = "要素が見つかりませんでした。" });
                            continue;
                        }

                        var bb = elem.get_BoundingBox(null);
                        if (bb == null)
                        {
                            skipped.Add(new { elementId = eid.IntValue(), reason = "BoundingBox を取得できませんでした。" });
                            continue;
                        }

                        // BB が極端に小さい場合は、SectionBox 生成は不安定なのでスキップ
                        var diag = bb.Max - bb.Min;
                        if (diag == null || diag.GetLength() < UnitHelper.MmToFt(1.0))
                        {
                            skipped.Add(new
                            {
                                elementId = eid.IntValue(),
                                reason = "BoundingBox が極端に小さいため、SectionBox を生成しませんでした。"
                            });
                            continue;
                        }

                        // ビュー方向: カテゴリを考慮した安定ロジック
                        XYZ viewDir = GetPreferredViewDirection(elem);

                        // Up / Right ベクトルを構築
                        XYZ up = XYZ.BasisZ;
                        if (Math.Abs(viewDir.DotProduct(up)) > 0.99)
                            up = XYZ.BasisX;

                        // IMPORTANT: Keep a right-handed coordinate system for the section box transform.
                        // BasisX × BasisY must point to BasisZ (= viewDir). Otherwise ViewSection.CreateSection may throw.
                        XYZ right = up.CrossProduct(viewDir).Normalize();
                        up = viewDir.CrossProduct(right).Normalize();

                        // 原点（断面ビューの位置）: 要素 BB 中心から viewDir 方向に offset だけ手前側へ
                        XYZ center = (bb.Min + bb.Max) * 0.5;
                        double offsetFt = UnitHelper.MmToFt(offsetMm);
                        XYZ origin = center - viewDir * offsetFt;

                        var transform = Transform.Identity;
                        transform.Origin = origin;
                        transform.BasisX = right;
                        transform.BasisY = up;
                        transform.BasisZ = viewDir;

                        // 要素 BB の 8 点をビュー座標系へ変換してローカル AABB を求める
                        var corners = new[]
                        {
                            new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                            new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                            new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                            new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                            new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                            new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                            new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                            new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                        };

                        var inv = transform.Inverse;

                        double minX = double.MaxValue, maxX = double.MinValue;
                        double minY = double.MaxValue, maxY = double.MinValue;
                        double minZ = double.MaxValue, maxZ = double.MinValue;

                        foreach (var c in corners)
                        {
                            var lc = inv.OfPoint(c);
                            if (lc.X < minX) minX = lc.X;
                            if (lc.X > maxX) maxX = lc.X;
                            if (lc.Y < minY) minY = lc.Y;
                            if (lc.Y > maxY) maxY = lc.Y;
                            if (lc.Z < minZ) minZ = lc.Z;
                            if (lc.Z > maxZ) maxZ = lc.Z;
                        }

                        double marginFt = UnitHelper.MmToFt(cropMarginMm);

                        minX -= marginFt; maxX += marginFt;
                        minY -= marginFt; maxY += marginFt;
                        minZ -= marginFt; maxZ += marginFt;

                        // Revit の制約に合わせて、各軸に最小厚みを強制する
                        double minThicknessFt = UnitHelper.MmToFt(10.0);
                        EnsureMinThickness(ref minX, ref maxX, minThicknessFt);
                        EnsureMinThickness(ref minY, ref maxY, minThicknessFt);
                        EnsureMinThickness(ref minZ, ref maxZ, minThicknessFt);

                        // Z 方向は Revit の SectionView の制約に合わせて、
                        // ローカル Z=0 を断面位置とし、正の方向に depth を取るように変換する。
                        double depthFt = maxZ - minZ;
                        if (depthFt < UnitHelper.MmToFt(10.0))
                        {
                            depthFt = UnitHelper.MmToFt(10.0);
                        }

                        // もともとのローカル座標における minZ を Transform.Origin に反映させ、
                        // 新しいローカル系では minZ'=0, maxZ'=depthFt となるように調整。
                        transform.Origin = transform.Origin + viewDir * minZ;
                        minZ = 0.0;
                        maxZ = depthFt;

                        var box = new BoundingBoxXYZ
                        {
                            Transform = transform,
                            Min = new XYZ(minX, minY, minZ),
                            Max = new XYZ(maxX, maxY, maxZ)
                        };

                        ViewSection view = null;
                        try
                        {
                            view = ViewSection.CreateSection(doc, vftSection.Id, box);
                        }
                        catch (Exception exCreate)
                        {
                            skipped.Add(new
                            {
                                elementId = eid.IntValue(),
                                reason = "ViewSection.CreateSection 例外: " + exCreate.GetType().Name +
                                         (string.IsNullOrEmpty(exCreate.Message) ? "" : " - " + exCreate.Message),
                                debug = new
                                {
                                    worldBounds = new
                                    {
                                        min = ToPointMm(bb.Min),
                                        max = ToPointMm(bb.Max)
                                    },
                                    localBounds = new
                                    {
                                        min = ToPointMm(new XYZ(minX, minY, minZ)),
                                        max = ToPointMm(new XYZ(maxX, maxY, maxZ))
                                    },
                                    viewBasis = new
                                    {
                                        origin = ToPointMm(origin),
                                        dir = ToVector(viewDir),
                                        up = ToVector(up),
                                        right = ToVector(right)
                                    }
                                }
                            });
                            continue;
                        }

                        if (view == null)
                        {
                            skipped.Add(new
                            {
                                elementId = eid.IntValue(),
                                reason = "ViewSection.CreateSection が null を返しました。",
                                debug = new
                                {
                                    worldBounds = new
                                    {
                                        min = ToPointMm(bb.Min),
                                        max = ToPointMm(bb.Max)
                                    },
                                    localBounds = new
                                    {
                                        min = ToPointMm(new XYZ(minX, minY, minZ)),
                                        max = ToPointMm(new XYZ(maxX, maxY, maxZ))
                                    },
                                    viewBasis = new
                                    {
                                        origin = ToPointMm(origin),
                                        dir = ToVector(viewDir),
                                        up = ToVector(up),
                                        right = ToVector(right)
                                    }
                                }
                            });
                            continue;
                        }

                        view.Scale = viewScale;

                        // デバッグ用に名前を分かりやすく
                        try
                        {
                            string baseName = "DbgSection_" + eid.IntValue();
                            view.Name = MakeUniqueViewName(doc, baseName);
                        }
                        catch { }

                        created.Add(new
                        {
                            ok = true,
                            elementId = eid.IntValue(),
                            viewId = view.Id.IntValue(),
                            viewName = view.Name,
                            worldBounds = new
                            {
                                min = ToPointMm(bb.Min),
                                max = ToPointMm(bb.Max)
                            },
                            localBounds = new
                            {
                                min = ToPointMm(new XYZ(minX, minY, minZ)),
                                max = ToPointMm(new XYZ(maxX, maxY, maxZ))
                            },
                            viewBasis = new
                            {
                                origin = ToPointMm(origin),
                                dir = ToVector(viewDir),
                                up = ToVector(up),
                                right = ToVector(right)
                            }
                        });
                    }
                    catch (Exception exElem)
                    {
                        skipped.Add(new
                        {
                            elementId = eid.IntValue(),
                            reason = exElem.GetType().Name +
                                     (string.IsNullOrEmpty(exElem.Message) ? "" : " - " + exElem.Message)
                        });
                    }
                }

                tx.Commit();
            }

            return new
            {
                ok = created.Count > 0,
                msg = $"Created {created.Count} section view(s).",
                items = created,
                skipped
            };
        }

        private static object ToPointMm(XYZ p)
        {
            return new
            {
                x_ft = p.X,
                y_ft = p.Y,
                z_ft = p.Z,
                x_mm = UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters),
                y_mm = UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters),
                z_mm = UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters)
            };
        }

        private static object ToVector(XYZ v)
        {
            return new { x = v.X, y = v.Y, z = v.Z };
        }

        /// <summary>
        /// 要素カテゴリに応じて、SectionView 用の安定したビュー方向を決定する。
        /// FamilyInstance（ドア/窓など）は FacingOrientation、壁は壁法線、それ以外はグローバル Y+ を使う。
        /// </summary>
        private static XYZ GetPreferredViewDirection(Element elem)
        {
            // FamilyInstance（ドアや窓など）
            var fi = elem as FamilyInstance;
            if (fi != null)
            {
                try
                {
                    var facing = fi.FacingOrientation;
                    if (facing != null && !facing.IsZeroLength())
                    {
                        return facing.Normalize();
                    }
                }
                catch
                {
                    // fall through
                }
            }

            // 壁 / カーテンウォール
            var wall = elem as Wall;
            if (wall != null)
            {
                try
                {
                    var locCurve = wall.Location as LocationCurve;
                    var curve = locCurve != null ? locCurve.Curve : null;
                    if (curve != null)
                    {
                        XYZ tangent;
                        var line = curve as Line;
                        if (line != null)
                        {
                            tangent = line.Direction;
                        }
                        else
                        {
                            var p0 = curve.Evaluate(0.0, true);
                            var p1 = curve.Evaluate(1.0, true);
                            tangent = (p1 - p0);
                        }

                        tangent = new XYZ(tangent.X, tangent.Y, 0.0);
                        if (!tangent.IsZeroLength())
                        {
                            tangent = tangent.Normalize();
                            // 上方向 Z と外積を取って壁法線を求める
                            var normal = tangent.CrossProduct(XYZ.BasisZ);
                            if (!normal.IsZeroLength())
                            {
                                return normal.Normalize();
                            }
                        }
                    }
                }
                catch
                {
                    // fall through
                }
            }

            // 既定: グローバル Y+
            var defaultDir = new XYZ(0, 1, 0);
            if (defaultDir.IsZeroLength())
            {
                defaultDir = XYZ.BasisY;
            }
            return defaultDir.Normalize();
        }

        /// <summary>
        /// 与えられた軸方向の min/max に対して、最小厚みを保証する。
        /// 現在の中心は維持したまま、必要に応じて min/max を拡張する。
        /// </summary>
        private static void EnsureMinThickness(ref double min, ref double max, double minThickness)
        {
            if (double.IsNaN(min) || double.IsNaN(max))
            {
                return;
            }

            double current = max - min;
            if (current < minThickness)
            {
                double center = (min + max) * 0.5;
                double half = minThickness * 0.5;
                min = center - half;
                max = center + half;
            }
        }

        /// <summary>
        /// 他ファイルの実装と同様のユニーク名生成ヘルパ。
        /// </summary>
        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 2;
            while (new FilteredElementCollector(doc)
                   .OfClass(typeof(View))
                   .Cast<View>()
                   .Any(v => !v.IsTemplate && string.Equals(v.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = baseName + " (" + i.ToString() + ")";
                i++;
            }
            return name;
        }
    }
}


