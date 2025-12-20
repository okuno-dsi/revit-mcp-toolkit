// ================================================================
// File: Commands/ViewOps/SaveApply3DViewSettingsHandlers.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 3Dビューの状態（姿勢・Crop/Section・Transform・FOV）を保存/適用
// Updates :
//  - deriveCropFromViewExtents (default: true):
//      Crop/Section が無い場合でも、UIView.GetZoomCorners() から可視AABBを推定し
//      “仮Crop” を保存（cropActive=falseで安全適用）。derivedCrop=true を付与。
//  - focalLengthMm: 透視ビュー時は極力取得（名称一致 + パラメータ走査のフォールバック）
//  - 適用側は堅牢処理（反転抑止、テンプレ警告、FOV復元、ZoomToFit等）を維持
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    // ============================ SAVE ============================
    public class Save3DViewSettingsHandler : IRevitCommandHandler
    {
        public string CommandName => "save_3d_view_settings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = cmd.Params as JObject ?? new JObject();
                if (!p.ContainsKey("viewId"))
                    return new { ok = false, msg = "viewId が指定されていません" };

                var uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                    return new { ok = false, msg = "アクティブドキュメントがありません" };
                var doc = uidoc.Document;

                var view = doc.GetElement(new ElementId(p.Value<int>("viewId"))) as View3D;
                if (view == null)
                    return new { ok = false, msg = "指定されたビューは3Dビューではありません" };

                // オプション：可視AABB→仮Crop（既定: true）
                bool deriveCropFromViewExtents = p.Value<bool?>("deriveCropFromViewExtents") ?? true;

                var ori = view.GetOrientation();

                // ---- Crop 既存値 ----
                var cropBox = SafeGetCropBox(view);
                bool cropActive = SafeGet(() => view.CropBoxActive, false);
                bool cropVisible = SafeGet(() => view.CropBoxVisible, false);
                var ct = cropBox != null ? (cropBox.Transform ?? Transform.Identity) : Transform.Identity;

                // ---- Section 既存値 ----
                bool sectionActive = SafeGet(() => view.IsSectionBoxActive, false);
                BoundingBoxXYZ? sectionBox = null;
                Transform st = Transform.Identity;
                if (sectionActive)
                {
                    sectionBox = SafeGet(() => view.GetSectionBox(), null);
                    if (sectionBox == null) sectionActive = false;
                    else st = sectionBox.Transform ?? Transform.Identity;
                }

                // ---- 透視FOV（focalLengthMm） ----
                double? focalLengthMm = null;
                if (view.IsPerspective && TryGetFocalLengthMmAggressive(view, out var flMm))
                    focalLengthMm = flMm;

                // ---- 仮Cropの導出（Crop/Sectionが無い場合のみ）----
                bool derivedCrop = false;
                if (deriveCropFromViewExtents && cropBox == null && !sectionActive)
                {
                    var uiView = uidoc.GetOpenUIViews()?.FirstOrDefault(v => v.ViewId == view.Id);
                    if (uiView != null && TryDerivePseudoCropFromUIView(uiView, view, out var pseudoCt, out var pseudoMin, out var pseudoMax))
                    {
                        ct = pseudoCt;
                        cropBox = new BoundingBoxXYZ { Transform = ct, Min = pseudoMin, Max = pseudoMax };
                        cropActive = false;  // 仮Cropは無効フラグで保存（適用は安全化してから）
                        cropVisible = false;
                        derivedCrop = true;
                    }
                }

                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    isPerspective = view.IsPerspective,

                    // Orientation
                    eyeX = ori.EyePosition.X,
                    eyeY = ori.EyePosition.Y,
                    eyeZ = ori.EyePosition.Z,
                    forwardX = ori.ForwardDirection.X,
                    forwardY = ori.ForwardDirection.Y,
                    forwardZ = ori.ForwardDirection.Z,
                    upX = ori.UpDirection.X,
                    upY = ori.UpDirection.Y,
                    upZ = ori.UpDirection.Z,

                    // Crop
                    cropBoxMin = cropBox != null ? new { X = cropBox.Min.X, Y = cropBox.Min.Y, Z = cropBox.Min.Z } : null,
                    cropBoxMax = cropBox != null ? new { X = cropBox.Max.X, Y = cropBox.Max.Y, Z = cropBox.Max.Z } : null,
                    cropActive,
                    cropVisible,
                    cropTransform = cropBox != null ? new
                    {
                        origin = new { X = ct.Origin.X, Y = ct.Origin.Y, Z = ct.Origin.Z },
                        basisX = new { X = ct.BasisX.X, Y = ct.BasisX.Y, Z = ct.BasisX.Z },
                        basisY = new { X = ct.BasisY.X, Y = ct.BasisY.Y, Z = ct.BasisY.Z },
                        basisZ = new { X = ct.BasisZ.X, Y = ct.BasisZ.Y, Z = ct.BasisZ.Z }
                    } : null,

                    // Section
                    sectionActive,
                    sectionBoxMin = (sectionActive && sectionBox != null) ? new { X = sectionBox.Min.X, Y = sectionBox.Min.Y, Z = sectionBox.Min.Z } : null,
                    sectionBoxMax = (sectionActive && sectionBox != null) ? new { X = sectionBox.Max.X, Y = sectionBox.Max.Y, Z = sectionBox.Max.Z } : null,
                    sectionTransform = (sectionActive && sectionBox != null) ? new
                    {
                        origin = new { X = st.Origin.X, Y = st.Origin.Y, Z = st.Origin.Z },
                        basisX = new { X = st.BasisX.X, Y = st.BasisX.Y, Z = st.BasisX.Z },
                        basisY = new { X = st.BasisY.X, Y = st.BasisY.Y, Z = st.BasisY.Z },
                        basisZ = new { X = st.BasisZ.X, Y = st.BasisZ.Y, Z = st.BasisZ.Z }
                    } : null,

                    // Perspective zoom feel
                    focalLengthMm,

                    // 付加情報
                    derivedCrop = derivedCrop ? (bool?)true : null
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "save_3d_view_settings 実行中に例外: " + ex.Message };
            }
        }

        private static T SafeGet<T>(Func<T> f, T fallback) { try { return f(); } catch { return fallback; } }
        private static BoundingBoxXYZ? SafeGetCropBox(View3D v) { try { return v.CropBox; } catch { return null; } }

        // 透視の焦点距離（mm）を積極取得：名称一致＋全パラメータ走査フォールバック
        private static bool TryGetFocalLengthMmAggressive(View3D v, out double mm)
        {
            mm = 0;
            // 名前一致（多言語）
            var nameCandidates = new[] { "Focal Length", "焦点距離" };
            foreach (var name in nameCandidates)
            {
                var p = v.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    mm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
                    return true;
                }
            }
            // フォールバック：Double & 名前ヒューリスティック
            foreach (Parameter prm in v.Parameters)
            {
                if (prm?.StorageType == StorageType.Double)
                {
                    try
                    {
                        var n = prm.Definition?.Name ?? "";
                        if (n.IndexOf("focal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.Contains("焦点"))
                        {
                            mm = UnitUtils.ConvertFromInternalUnits(prm.AsDouble(), UnitTypeId.Millimeters);
                            return true;
                        }
                    }
                    catch { /* continue */ }
                }
            }
            return false;
        }

        // 可視AABB→仮Crop導出（UIView.GetZoomCorners()）。Z厚みは安全値で確保。
        private static bool TryDerivePseudoCropFromUIView(UIView ui, View3D v, out Transform t, out XYZ bbMin, out XYZ bbMax)
        {
            t = Transform.Identity; bbMin = bbMax = XYZ.Zero;
            try
            {
                // Revit 2023+: 引数なし & IList<XYZ> 返し
                IList<XYZ> corners = ui.GetZoomCorners();
                if (corners == null || corners.Count < 2) return false;

                XYZ c1 = corners[0];
                XYZ c2 = corners[1];

                // ビュー座標系（right, up, forward）を構築
                var o = v.GetOrientation();
                var fwd = o.ForwardDirection.Normalize();
                var up = o.UpDirection.Normalize();
                var right = fwd.CrossProduct(up).Normalize();
                up = right.CrossProduct(fwd).Normalize();

                // ワールド→ビューフレームの逆変換
                var frame = Transform.Identity;
                frame.Origin = XYZ.Zero;
                frame.BasisX = right; frame.BasisY = up; frame.BasisZ = fwd;
                var inv = frame.Inverse;

                var p1 = inv.OfPoint(c1);
                var p2 = inv.OfPoint(c2);

                double minX = Math.Min(p1.X, p2.X);
                double maxX = Math.Max(p1.X, p2.X);
                double minY = Math.Min(p1.Y, p2.Y);
                double maxY = Math.Max(p1.Y, p2.Y);

                // Zは可視範囲が取れないので安全厚みを与える（±100m相当の十分大きさ）
                const double ZPAD = 100000.0;
                double minZ = Math.Min(p1.Z, p2.Z) - ZPAD;
                double maxZ = Math.Max(p1.Z, p2.Z) + ZPAD;
                if (Math.Abs(minZ - maxZ) < 1e-6) { minZ -= 1; maxZ += 1; }

                t = frame; // AABBはフレーム座標でmin/max
                bbMin = new XYZ(minX, minY, minZ);
                bbMax = new XYZ(maxX, maxY, maxZ);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ============================ APPLY ============================
    public class Apply3DViewSettingsHandler : IRevitCommandHandler
    {
        public string CommandName => "apply_3d_view_settings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var warnings = new List<string>();
            var skips = new List<object>();

            try
            {
                var p = cmd.Params as JObject ?? new JObject();

                // 必須
                string[] keys = {
                    "viewId","isPerspective",
                    "eyeX","eyeY","eyeZ",
                    "forwardX","forwardY","forwardZ",
                    "upX","upY","upZ",
                    "cropActive","cropVisible","sectionActive"
                };
                foreach (var k in keys) if (!p.ContainsKey(k)) return new { ok = false, msg = $"必要なパラメータ '{k}' が不足しています" };

                // オプション
                bool snapCropToView = p.Value<bool?>("snapCropToView") ?? true;
                bool allowPerspectiveCrop = p.Value<bool?>("allowPerspectiveCrop") ?? false;
                bool createNewViewOnTypeMismatch = p.Value<bool?>("createNewViewOnTypeMismatch") ?? true;
                bool switchActive = p.Value<bool?>("switchActive") ?? false;
                bool fitOnApply = p.Value<bool?>("fitOnApply") ?? true;

                var uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                    return new { ok = false, msg = "アクティブドキュメントがありません" };
                var doc = uidoc.Document;

                int inputViewId = p.Value<int>("viewId");
                var baseView = doc.GetElement(new ElementId(inputViewId)) as View3D;
                if (baseView == null)
                    return new { ok = false, msg = "指定されたビューは3Dビューではありません" };

                bool wantPerspective = p.Value<bool>("isPerspective");

                if (!TryReadXYZ(p, "eyeX", "eyeY", "eyeZ", out XYZ eye)) return new { ok = false, msg = "eyeX/eyeY/eyeZ の形式が不正です" };
                if (!TryReadXYZ(p, "forwardX", "forwardY", "forwardZ", out XYZ f0)) return new { ok = false, msg = "forwardX/forwardY/forwardZ の形式が不正です" };
                if (!TryReadXYZ(p, "upX", "upY", "upZ", out XYZ u0)) return new { ok = false, msg = "upX/upY/uz の形式が不正です" };

                bool sectionActive = p.Value<bool>("sectionActive");
                bool cropActive = p.Value<bool>("cropActive");
                bool cropVisible = p.Value<bool>("cropVisible");

                // Crop
                XYZ? cbMin = null, cbMax = null; Transform ct = Transform.Identity;
                if (HasObject(p, "cropBoxMin") && HasObject(p, "cropBoxMax") && HasObject(p, "cropTransform"))
                {
                    if (!TryReadXYZ((JObject)p["cropBoxMin"], out cbMin)) return new { ok = false, msg = "cropBoxMin の形式が不正です" };
                    if (!TryReadXYZ((JObject)p["cropBoxMax"], out cbMax)) return new { ok = false, msg = "cropBoxMax の形式が不正です" };
                    if (!TryReadTransform((JObject)p["cropTransform"], out ct)) return new { ok = false, msg = "cropTransform の形式が不正です" };
                }
                else
                {
                    cropActive = false; cropVisible = false;
                    warnings.Add("crop 情報が無い/不完全のため、Crop 適用はスキップしました。");
                }

                // Section
                XYZ? sbMin = null, sbMax = null; Transform st = Transform.Identity;
                if (sectionActive)
                {
                    if (!(HasObject(p, "sectionBoxMin") && HasObject(p, "sectionBoxMax") && HasObject(p, "sectionTransform")))
                        return new { ok = false, msg = "sectionActive=true の場合、sectionBoxMin/sectionBoxMax/sectionTransform が必要です" };
                    if (!TryReadXYZ((JObject)p["sectionBoxMin"], out sbMin)) return new { ok = false, msg = "sectionBoxMin の形式が不正です" };
                    if (!TryReadXYZ((JObject)p["sectionBoxMax"], out sbMax)) return new { ok = false, msg = "sectionBoxMax の形式が不正です" };
                    if (!TryReadTransform((JObject)p["sectionTransform"], out st)) return new { ok = false, msg = "sectionTransform の形式が不正です" };
                }

                // 透視の焦点距離（mm）
                double? focalLengthMm = (p["focalLengthMm"]?.Type == JTokenType.Float || p["focalLengthMm"]?.Type == JTokenType.Integer)
                    ? (double?)p.Value<double>("focalLengthMm") : null;

                using (var tx = new Transaction(doc, "Apply 3D View Settings"))
                {
                    tx.Start();

                    var view = baseView;
                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                        warnings.Add($"ビュー '{view.Name}' はテンプレートが適用されています。ロックされた設定は変更できません。");

                    // 1) 透視/等測 揃える
                    bool createdNew = false;
                    if (view.IsPerspective != wantPerspective && createNewViewOnTypeMismatch)
                    {
                        view = CreateViewWithPerspective(doc, baseView, wantPerspective, out createdNew);
                        if (createdNew && switchActive) uidoc.ActiveView = view;
                    }
                    else if (view.IsPerspective != wantPerspective)
                    {
                        warnings.Add("isPerspective が一致しません（createNewViewOnTypeMismatch=false のため、種別変更は行いません）。");
                    }

                    // 2) 直交正規化（右手系）
                    OrthonormalizeForwardUp(f0, u0, out var forward, out var up);
                    BuildViewFrame(forward, up, out var right, out var upN, out var fwdN);

                    // 3) Orientation 先に適用
                    TrySetOrientation(view, eye, upN, fwdN, out var om1);
                    if (om1 != null) warnings.Add(om1);
                    doc.Regenerate();

                    // 3.1) 透視：焦点距離（mm）→ 設定
                    if (view.IsPerspective && focalLengthMm.HasValue)
                    {
                        if (!TrySetFocalLengthMm(view, focalLengthMm.Value, out var flmsg))
                            warnings.Add(flmsg);
                    }

                    // 4) Crop/Section Transform 安全化 + スナップ
                    if (cbMin != null && cbMax != null)
                    {
                        ct = MakeSafeFrame(ct);
                        if (snapCropToView) ct = SnapTransformToView(ct, right, upN, fwdN);
                        EnsureMinMax(ref cbMin!, ref cbMax!);
                    }
                    if (sectionActive && sbMin != null && sbMax != null)
                    {
                        st = MakeSafeFrame(st);
                        if (snapCropToView) st = SnapTransformToView(st, right, upN, fwdN);
                        EnsureMinMax(ref sbMin!, ref sbMax!);
                    }

                    // 4.1) 透視Cropの扱い
                    bool isPersp = view.IsPerspective;
                    if (isPersp && !allowPerspectiveCrop)
                    {
                        cropActive = false; cropVisible = false;
                        if (cbMin != null && cbMax != null)
                            warnings.Add("透視ビューではCropを適用しません（allowPerspectiveCrop=false）。");
                    }

                    // 5) Crop 適用
                    if (cbMin != null && cbMax != null)
                    {
                        try
                        {
                            var cropBox = new BoundingBoxXYZ { Transform = ct, Min = cbMin, Max = cbMax };
                            view.CropBox = cropBox;
                            view.CropBoxActive = cropActive;
                            view.CropBoxVisible = cropVisible;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("Crop 適用をスキップ: " + ex.Message);
                            skips.Add(new { kind = "crop", reason = ex.Message });
                        }
                    }

                    // 6) Section 適用
                    try
                    {
                        if (sectionActive && sbMin != null && sbMax != null)
                        {
                            var sectionBox = new BoundingBoxXYZ { Transform = st, Min = sbMin, Max = sbMax };
                            view.SetSectionBox(sectionBox);
                            view.IsSectionBoxActive = true;
                        }
                        else
                        {
                            view.IsSectionBoxActive = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("Section 適用をスキップ: " + ex.Message);
                        skips.Add(new { kind = "section", reason = ex.Message });
                    }

                    doc.Regenerate();

                    // 7) Orientation 再適用（等測再計算対策）
                    TrySetOrientation(view, eye, upN, fwdN, out var om2);
                    if (om2 != null) warnings.Add(om2);

                    tx.Commit();

                    // 8) 画面フレーミング：UIView.ZoomToFit()（fitOnApply=true 既定）
                    if (fitOnApply)
                    {
                        try
                        {
                            var ui = uidoc.GetOpenUIViews()?.FirstOrDefault(v => v.ViewId == view.Id);
                            ui?.ZoomToFit();
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("UIView.ZoomToFit() をスキップ: " + ex.Message);
                        }
                    }

                    uidoc.RefreshActiveView();

                    return new
                    {
                        ok = true,
                        viewId = view.Id.IntegerValue,
                        warnings = warnings.Count > 0 ? warnings : null,
                        skips = skips.Count > 0 ? skips : null
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "apply_3d_view_settings 実行中に例外: " + ex.Message };
            }
        }

        // ===== helpers =====
        private static bool HasObject(JObject p, string key) => p[key] != null && p[key].Type == JTokenType.Object;

        private static bool TryReadXYZ(JObject p, string xKey, string yKey, string zKey, out XYZ xyz)
        {
            xyz = XYZ.Zero;
            try
            {
                if (!p.ContainsKey(xKey) || !p.ContainsKey(yKey) || !p.ContainsKey(zKey)) return false;
                xyz = new XYZ(p.Value<double>(xKey), p.Value<double>(yKey), p.Value<double>(zKey));
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadXYZ(JObject obj, out XYZ xyz)
        {
            xyz = XYZ.Zero;
            try
            {
                if (obj == null) return false;
                if (!obj.ContainsKey("X") || !obj.ContainsKey("Y") || !obj.ContainsKey("Z")) return false;
                xyz = new XYZ(obj.Value<double>("X"), obj.Value<double>("Y"), obj.Value<double>("Z"));
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadTransform(JObject t, out Transform tr)
        {
            tr = Transform.Identity;
            try
            {
                if (t == null) return false;
                if (!(t["origin"] is JObject) || !(t["basisX"] is JObject) || !(t["basisY"] is JObject) || !(t["basisZ"] is JObject)) return false;

                if (!TryReadXYZ((JObject)t["origin"], out XYZ o)) return false;
                if (!TryReadXYZ((JObject)t["basisX"], out XYZ bx)) return false;
                if (!TryReadXYZ((JObject)t["basisY"], out XYZ by)) return false;
                if (!TryReadXYZ((JObject)t["basisZ"], out XYZ bz)) return false;

                tr = Transform.Identity;
                tr.Origin = o; tr.BasisX = bx; tr.BasisY = by; tr.BasisZ = bz;
                return true;
            }
            catch { return false; }
        }

        private static View3D CreateViewWithPerspective(Document doc, View3D baseView, bool wantPerspective, out bool createdNew)
        {
            createdNew = false;
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
            var newView = wantPerspective ? View3D.CreatePerspective(doc, vft.Id) : View3D.CreateIsometric(doc, vft.Id);
            try { newView.Name = GenSafeName(doc, baseView.Name, wantPerspective ? "_persp" : "_iso"); } catch { }
            try { newView.DisplayStyle = baseView.DisplayStyle; newView.DetailLevel = baseView.DetailLevel; } catch { }
            createdNew = true;
            return newView;
        }

        private static string GenSafeName(Document doc, string baseName, string suffix)
        {
            string name = baseName + suffix; int i = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                   .Any(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { i++; name = $"{baseName}{suffix}_{i}"; }
            return name;
        }

        private static void OrthonormalizeForwardUp(XYZ f0, XYZ u0, out XYZ f, out XYZ u)
        {
            f = (f0 == null || f0.GetLength() < 1e-9) ? XYZ.BasisY : f0.Normalize();
            var uTilde = u0 - f.DotProduct(u0) * f;
            if (uTilde == null || uTilde.GetLength() < 1e-9)
            {
                var aux = Math.Abs(f.DotProduct(XYZ.BasisZ)) > 0.9 ? XYZ.BasisY : XYZ.BasisZ;
                uTilde = aux - f.DotProduct(aux) * f;
            }
            u = uTilde.Normalize();
            var r = f.CrossProduct(u);
            if (r == null || r.GetLength() < 1e-9)
            {
                var aux = Math.Abs(f.DotProduct(XYZ.BasisZ)) > 0.9 ? XYZ.BasisY : XYZ.BasisZ;
                u = (aux - f.DotProduct(aux) * f).Normalize();
                r = f.CrossProduct(u);
            }
            if (r.DotProduct(f.CrossProduct(u)) < 0) u = u.Negate();
        }

        private static void BuildViewFrame(XYZ forward, XYZ up, out XYZ right, out XYZ upN, out XYZ fwdN)
        {
            fwdN = forward.Normalize();
            right = fwdN.CrossProduct(up).Normalize();
            upN = right.CrossProduct(fwdN).Normalize();
        }

        private static Transform MakeSafeFrame(Transform t)
        {
            var bx = SafeNorm(t.BasisX);
            var by = t.BasisY - bx.DotProduct(t.BasisY) * bx; by = SafeNorm(by);
            var bz = bx.CrossProduct(by);
            if (bz == null || bz.GetLength() < 1e-9)
            {
                var aux = Math.Abs(bx.DotProduct(XYZ.BasisZ)) > 0.9 ? XYZ.BasisY : XYZ.BasisZ;
                by = (aux - bx.DotProduct(aux) * bx).Normalize();
                bz = bx.CrossProduct(by);
            }
            if (bx.CrossProduct(by).DotProduct(bz) < 0) { by = by.Negate(); bz = bx.CrossProduct(by); }
            var tr = Transform.Identity;
            tr.Origin = t.Origin; tr.BasisX = bx; tr.BasisY = by; tr.BasisZ = SafeNorm(bz);
            return tr;
        }

        private static XYZ SafeNorm(XYZ v) => (v == null || v.GetLength() < 1e-9) ? XYZ.BasisX : v.Normalize();

        private static Transform SnapTransformToView(Transform t, XYZ right, XYZ up, XYZ forward)
        {
            if (t == null) return Transform.Identity;
            XYZ ProjectToBasis(XYZ v, XYZ ex, XYZ ey, XYZ ez)
                => ex.Multiply(v.DotProduct(ex)).Add(ey.Multiply(v.DotProduct(ey))).Add(ez.Multiply(v.DotProduct(ez)));
            var tr = Transform.Identity;
            tr.Origin = t.Origin;
            tr.BasisX = ProjectToBasis(t.BasisX, right, up, forward);
            tr.BasisY = ProjectToBasis(t.BasisY, right, up, forward);
            tr.BasisZ = ProjectToBasis(t.BasisZ, right, up, forward);
            return MakeSafeFrame(tr);
        }

        private static bool TrySetOrientation(View3D view, XYZ eye, XYZ up, XYZ forward, out string? msg)
        {
            msg = null;
            try { view.SetOrientation(new ViewOrientation3D(eye, up, forward)); return true; }
            catch (Exception ex) { msg = "Orientation の適用をスキップ: " + ex.Message; return false; }
        }

        // 透視の焦点距離（mm）設定（名称は多言語想定）
        private static bool TrySetFocalLengthMm(View3D v, double mm, out string? msg)
        {
            msg = null;
            try
            {
                var candidates = new[] { "Focal Length", "焦点距離" };
                foreach (var name in candidates)
                {
                    var prm = v.LookupParameter(name);
                    if (prm != null && prm.StorageType == StorageType.Double && !prm.IsReadOnly)
                    {
                        var feet = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
                        if (!prm.Set(feet)) { msg = $"焦点距離 '{name}' の設定に失敗しました。"; return false; }
                        return true;
                    }
                }
                msg = "焦点距離パラメータが見つかりません（環境差）。";
                return false;
            }
            catch (Exception ex)
            {
                msg = "焦点距離の設定をスキップ: " + ex.Message;
                return false;
            }
        }

        private static void EnsureMinMax(ref XYZ? min, ref XYZ? max)
        {
            if (min == null || max == null) return;
            double minX = Math.Min(min.X, max.X), minY = Math.Min(min.Y, max.Y), minZ = Math.Min(min.Z, max.Z);
            double maxX = Math.Max(min.X, max.X), maxY = Math.Max(min.Y, max.Y), maxZ = Math.Max(min.Z, max.Z);
            if (Math.Abs(minX - maxX) < 1e-9) maxX = minX + 1e-6;
            if (Math.Abs(minY - maxY) < 1e-9) maxY = minY + 1e-6;
            if (Math.Abs(minZ - maxZ) < 1e-9) maxZ = minZ + 1e-6;
            min = new XYZ(minX, minY, minZ); max = new XYZ(maxX, maxY, maxZ);
        }
    }
}
