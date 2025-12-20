// ================================================================
// File: Core/UnitHelper.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 単位⇄数値 変換の統一ハブ（取得・設定・幾何・レスポンスメタ）
// Policy : 既定は SI 正規化（Length=mm, Area=m2, Volume=m3, Angle=deg）
//          人間可読は Parameter.AsValueString() を display として常に併記
//          アドイン設定 (UnitSettings) と per-command の unitsMode をサポート
// Modes  : SI | Project | Raw | Both
// Notes  : 互換性のため、薄いラッパ関数を用意（MmToInternal 系 など）
// ================================================================
#nullable enable
using System;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>外部（SI/Project/Raw）表現の標準型</summary>
    public sealed class ExternalNumeric
    {
        /// <summary>SI 正規化値（Length:mm / Area:m2 / Volume:m3 / Angle:deg）。未判定は null</summary>
        public double? Value { get; set; }
        /// <summary>単位ラベル（"mm"/"m2"/"m3"/"deg"/"raw"/"ElementId"/"project"）</summary>
        public string Unit { get; set; }
        /// <summary>人間可読の表示文字列（AsValueString）。取得不可なら null</summary>
        public string Display { get; set; }
        /// <summary>内部（Revit）生値：Length=ft, Area=ft2, Volume=ft3, Angle=rad。未判定は null</summary>
        public double? Raw { get; set; }
    }

    // UnitsMode はプロジェクトの他所で定義されている想定。
    // もし未定義なら下行のコメントを外して利用してください。
    // public enum UnitsMode { SI, Project, Raw, Both }

    public static class UnitHelper
    {
        // ----------------------------
        // レスポンス共通メタ
        // ----------------------------
        public static object DefaultUnitsMeta() => new { Length = "mm", Area = "m2", Volume = "m3", Angle = "deg" };

        public static object InputUnitsMeta()
        {
            // ユーザー入力・AI入出力側の既定（SI正規化）
            // 長さ=mm, 面積=mm2（または m2 にしたい場合はここを変えてOK）, 体積=mm3（or m3）, 角度=deg
            return new
            {
                Length = "mm",
                Area = "mm2",
                Volume = "mm3",
                Angle = "deg"
            };
        }

        public static object InternalUnitsMeta()
        {
            // Revit 内部単位（固定）：長さ=ft, 面積=ft2, 体積=ft3, 角度=rad
            return new
            {
                Length = "ft",
                Area = "ft2",
                Volume = "ft3",
                Angle = "rad"
            };
        }


        // ----------------------------
        // 単位ラベル
        // ----------------------------
        public static string UnitLabel(ForgeTypeId? spec)
        {
            if (spec == null) return "raw";
            if (spec.Equals(SpecTypeId.Length)) return "mm";
            if (spec.Equals(SpecTypeId.Area)) return "m2";
            if (spec.Equals(SpecTypeId.Volume)) return "m3";
            if (spec.Equals(SpecTypeId.Angle)) return "deg";
            return "raw";
        }

        // ----------------------------
        // モード解決（per-command > settings > SI）
        // ----------------------------
        public static UnitsMode ResolveUnitsMode(Document doc, JObject cmdParams)
        {
            // 1) per-command override
            try
            {
                var modeStr = cmdParams?.Value<string>("unitsMode");
                if (!string.IsNullOrWhiteSpace(modeStr) &&
                    Enum.TryParse<UnitsMode>(modeStr, true, out var m))
                    return m;
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ResolveUnitsMode per-command override parse failed: {ex.Message}");
            }

            // 2) add-in settings
            try { return UnitSettingsManager.Current.DefaultMode; }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ResolveUnitsMode settings read failed: {ex.Message}");
            }

            // 3) fallback
            return UnitsMode.SI;
        }

        // ----------------------------
        // Parameter → ExternalNumeric（SI 既定）
        // ----------------------------
        public static ExternalNumeric ToExternal(Parameter p, bool includeDisplay = true, bool includeRaw = true, int siDigits = 3)
        {
            var ex = new ExternalNumeric();

            if (includeDisplay)
            {
                try { ex.Display = p.AsValueString(); } catch { ex.Display = null; }
            }

            switch (p.StorageType)
            {
                case StorageType.Double:
                    {
                        var spec = TryGetSpec(p);
                        var unit = UnitLabel(spec);
                        ex.Unit = unit;

                        double raw = SafeAsDouble(p);
                        if (spec != null)
                        {
                            ex.Value = ConvertOutBySpec(raw, spec, siDigits);
                            if (includeRaw) ex.Raw = raw;
                        }
                        else
                        {
                            ex.Value = null;
                            ex.Unit = "raw";
                            if (includeRaw) ex.Raw = raw;
                        }
                        break;
                    }
                case StorageType.Integer:
                    {
                        ex.Value = p.AsInteger();
                        ex.Unit = null;
                        ex.Raw = null;
                        break;
                    }
                case StorageType.String:
                    {
                        ex.Value = null;
                        ex.Unit = null;
                        ex.Raw = null;
                        ex.Display ??= SafeAsString(p);
                        break;
                    }
                case StorageType.ElementId:
                    {
                        var id = p.AsElementId();
                        ex.Value = id?.IntegerValue ?? 0;
                        ex.Unit = "ElementId";
                        ex.Raw = null;
                        break;
                    }
                default:
                    {
                        ex.Value = null;
                        ex.Unit = null;
                        ex.Raw = null;
                        break;
                    }
            }
            return ex;
        }

        // ----------------------------
        // Parameter → 汎用マップ（mode: SI/Project/Raw/Both）
        // ----------------------------
        public static object MapParameter(Parameter p, Document doc, UnitsMode mode,
            bool includeDisplay = true, bool includeRaw = true, int siDigits = 3)
        {
            string name = p.Definition?.Name ?? string.Empty;
            string storage = p.StorageType.ToString();
            bool readOnly = p.IsReadOnly;

            string display = null;
            if (includeDisplay)
            {
                try { display = p.AsValueString(); } catch { display = null; }
            }

            var spec = TryGetSpec(p);
            string dataType = spec?.TypeId;

            // raw（内部値：Doubleのみ）
            double? raw = (p.StorageType == StorageType.Double) ? SafeAsDouble(p) : (double?)null;

            // SI 値
            double? valueSi = null; string unitSi = null;
            if (p.StorageType == StorageType.Double && spec != null)
            {
                valueSi = ConvertOutBySpec(raw ?? 0, spec, siDigits);
                unitSi = UnitLabel(spec);
            }

            // Project 値
            double? valuePrj = null;
            if (p.StorageType == StorageType.Double && spec != null)
                valuePrj = ToExternalProject(doc, raw ?? 0, spec);

            switch (mode)
            {
                case UnitsMode.Project:
                    return new
                    {
                        name,
                        id = p.Id.IntegerValue,
                        storageType = storage,
                        isReadOnly = readOnly,
                        dataType,
                        value = valuePrj,
                        unit = "project",
                        display,
                        raw = includeRaw ? raw : null
                    };

                case UnitsMode.Raw:
                    return new
                    {
                        name,
                        id = p.Id.IntegerValue,
                        storageType = storage,
                        isReadOnly = readOnly,
                        dataType,
                        value = raw,
                        unit = "raw",
                        display,
                        raw
                    };

                case UnitsMode.Both:
                    return new
                    {
                        name,
                        id = p.Id.IntegerValue,
                        storageType = storage,
                        isReadOnly = readOnly,
                        dataType,
                        valueSi = valueSi,
                        unitSi = unitSi,
                        valueProject = valuePrj,
                        unitProject = "project",
                        display,
                        raw = includeRaw ? raw : null
                    };

                case UnitsMode.SI:
                default:
                    if (p.StorageType == StorageType.Double)
                    {
                        return new
                        {
                            name,
                            id = p.Id.IntegerValue,
                            storageType = storage,
                            isReadOnly = readOnly,
                            dataType,
                            value = valueSi,
                            unit = unitSi,
                            display,
                            raw = includeRaw ? raw : null
                        };
                    }
                    else if (p.StorageType == StorageType.Integer)
                    {
                        return new
                        {
                            name,
                            id = p.Id.IntegerValue,
                            storageType = storage,
                            isReadOnly = readOnly,
                            dataType,
                            value = p.AsInteger(),
                            unit = (string?)null,
                            display,
                            raw = (double?)null
                        };
                    }
                    else if (p.StorageType == StorageType.String)
                    {
                        return new
                        {
                            name,
                            id = p.Id.IntegerValue,
                            storageType = storage,
                            isReadOnly = readOnly,
                            dataType,
                            value = (double?)null,
                            unit = (string?)null,
                            display = display ?? SafeAsString(p),
                            raw = (double?)null
                        };
                    }
                    else if (p.StorageType == StorageType.ElementId)
                    {
                        return new
                        {
                            name,
                            id = p.Id.IntegerValue,
                            storageType = storage,
                            isReadOnly = readOnly,
                            dataType,
                            value = p.AsElementId()?.IntegerValue ?? 0,
                            unit = "ElementId",
                            display,
                            raw = (double?)null
                        };
                    }
                    return new
                    {
                        name,
                        id = p.Id.IntegerValue,
                        storageType = storage,
                        isReadOnly = readOnly,
                        dataType,
                        value = (double?)null,
                        unit = (string?)null,
                        display,
                        raw = (double?)null
                    };
            }
        }

        // 互換オーバーロード（includeUnit を素通し）
        public static object MapParameter(
            Parameter p, Document doc, UnitsMode mode,
            bool includeDisplay, bool includeRaw, int siDigits, bool includeUnit)
            => MapParameter(p, doc, mode, includeDisplay, includeRaw, siDigits);

        // ----------------------------
        // 内部値 → SI（Spec基準）
        // ----------------------------
        public static double? ToExternal(double rawInternal, ForgeTypeId? spec, int siDigits = 3)
            => (spec == null) ? (double?)null : ConvertOutBySpec(rawInternal, spec, siDigits);

        // ----------------------------
        // 内部値 → Project 数値（Units API）
        // ----------------------------
        public static double? ToExternalProject(Document doc, double rawInternal, ForgeTypeId? spec)
        {
            try
            {
                if (spec == null) return rawInternal;
                var unitId = doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId();
                return UnitUtils.ConvertFromInternalUnits(rawInternal, unitId);
            }
            catch { return null; }
        }

        // ----------------------------
        // 外部（SI）→ 内部（Spec基準）
        // ----------------------------
        public static double ToInternal(double v, ForgeTypeId? spec)
        {
            if (spec == null) return v; // 不明 → 生値
            if (spec.Equals(SpecTypeId.Length))
                return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
            if (spec.Equals(SpecTypeId.Area))
                return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.SquareMeters);
            if (spec.Equals(SpecTypeId.Volume))
                return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.CubicMeters);
            if (spec.Equals(SpecTypeId.Angle))
                return DegToInternal(v);
            return v;
        }

        // ----------------------------
        // パラメータ更新（外部 SI 値を受けて内部値で Set）
        // ----------------------------
        public static bool TrySetParameterByExternalValue(Parameter param, object value, out string error)
        {
            error = null;
            if (param == null) { error = "Parameter is null."; return false; }
            if (param.IsReadOnly) { error = $"Parameter '{param.Definition?.Name}' is read-only."; return false; }

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.Set(value?.ToString() ?? string.Empty);

                    case StorageType.Integer:
                        if (value is bool b) return param.Set(b ? 1 : 0);
                        if (TryToInt(value, out int iv)) return param.Set(iv);
                        error = "Expected integer (or boolean) value.";
                        return false;

                    case StorageType.Double:
                        {
                            var spec = TryGetSpec(param);
                            if (!TryToDouble(value, out double dv))
                            {
                                error = "Expected numeric (double) value.";
                                return false;
                            }
                            double internalVal = ToInternal(dv, spec);
                            return param.Set(internalVal);
                        }

                    case StorageType.ElementId:
                        {
                            if (!TryToInt(value, out int eid))
                            {
                                error = "Expected integer ElementId.";
                                return false;
                            }
                            return param.Set(new ElementId(eid));
                        }
                }

                error = "Unsupported StorageType.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ----------------------------
        // 幾何・座標ユーティリティ（既存互換）
        // ----------------------------

        public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        public static XYZ MmToXyz(double xMm, double yMm, double zMm = 0.0)
            => new XYZ(MmToFt(xMm), MmToFt(yMm), MmToFt(zMm));
        public static XYZ MmToXyz((double x, double y, double z) pt)
            => new XYZ(MmToFt(pt.x), MmToFt(pt.y), MmToFt(pt.z));
        public static (double x, double y, double z) XyzToMm(XYZ p)
            => (FtToMm(p.X), FtToMm(p.Y), FtToMm(p.Z));

        // Core.Geometry.Point3D オーバーロード（内部=ft → 外部=mm）
        public static (double x, double y, double z) XyzToMm(RevitMCPAddin.Core.Geometry.Point3D p)
            => (FtToMm(p.X), FtToMm(p.Y), FtToMm(p.Z));

        // mm → ft の Point3D
        public static RevitMCPAddin.Core.Geometry.Point3D MmToPoint3D(double xMm, double yMm, double zMm = 0.0)
            => new RevitMCPAddin.Core.Geometry.Point3D(MmToFt(xMm), MmToFt(yMm), MmToFt(zMm));

        // 互換エイリアス（薄いラッパ）
        public static double MmToInternal(double valueMm, Document doc = null) => MmToFt(valueMm);
        public static double InternalToMm(double valueFt, Document doc = null) => FtToMm(valueFt);
        public static XYZ MmToInternalXYZ(double xMm, double yMm, double zMm, Document doc = null) => MmToXyz(xMm, yMm, zMm);
        public static XYZ MmToInternalXYZ(XYZ mm, Document doc = null) => new XYZ(MmToFt(mm.X), MmToFt(mm.Y), MmToFt(mm.Z));
        public static UV MmToInternalUV(double uMm, double vMm, Document doc = null) => new UV(MmToFt(uMm), MmToFt(vMm));
        public static UV MmToInternalUV(UV mm, Document doc = null) => new UV(MmToFt(mm.U), MmToFt(mm.V));

        // 面積・体積・角度（SI↔internal）
        public static double SqmToInternal(double m2) => UnitUtils.ConvertToInternalUnits(m2, UnitTypeId.SquareMeters);
        public static double InternalToSqm(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);
        public static double CubicMetersToInternal(double m3) => UnitUtils.ConvertToInternalUnits(m3, UnitTypeId.CubicMeters);
        public static double InternalToCubicMeters(double ft3) => UnitUtils.ConvertFromInternalUnits(ft3, UnitTypeId.CubicMeters);
        public static double DegToInternal(double deg) => Math.PI * deg / 180.0;
        public static double InternalToDeg(double rad) => 180.0 * rad / Math.PI;

        // 互換用の別名
        public static double MmToInternalLength(double mm) => MmToFt(mm);
        public static double Ft2ToMm2(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMillimeters);
        public static double Ft2ToM2(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);
        public static double Mm2ToFt2(double mm2) => UnitUtils.ConvertToInternalUnits(mm2, UnitTypeId.SquareMillimeters);
        public static double Ft3ToM3(double ft3) => UnitUtils.ConvertFromInternalUnits(ft3, UnitTypeId.CubicMeters);
        public static double Ft3ToMm3(double ft3) => UnitUtils.ConvertFromInternalUnits(ft3, UnitTypeId.CubicMillimeters);
        public static double Mm3ToFt3(double mm3) => UnitUtils.ConvertToInternalUnits(mm3, UnitTypeId.CubicMillimeters);



        // ============================================================
        // ★ 公開ラッパ（呼び出し側のビルドエラー解消用）
        // ============================================================
        public static ForgeTypeId? GetSpec(Parameter p) => TryGetSpec(p);
        public static bool TryParseInt(object v, out int i) => TryToInt(v, out i);
        public static bool TryParseDouble(object v, out double d) => TryToDouble(v, out d);

        // ============================================================
        // ↓↓↓ ここから “不足していた” 公開メソッドを追加 ↓↓↓
        // ============================================================

        /// <summary>
        /// { name, storageType, isReadOnly, dataType, value(SI), display } を返す。
        /// Double は Spec に基づき mm/m2/m3/deg に正規化。
        /// </summary>
        public static object ParamToSiInfo(Parameter prm, int digits = 3)
        {
            var name = prm.Definition?.Name ?? "";
            var storage = prm.StorageType.ToString();
            var isRO = prm.IsReadOnly;

            // 人間可読
            string display = "";
            try { display = prm.AsValueString() ?? prm.AsString() ?? ""; } catch { display = ""; }

            var spec = TryGetSpec(prm);
            string dataType = null;
            try { dataType = spec?.TypeId; } catch { }

            object value = null;
            try
            {
                switch (prm.StorageType)
                {
                    case StorageType.Double:
                        value = ConvertOutBySpec(SafeAsDouble(prm), spec, digits);
                        break;
                    case StorageType.Integer:
                        value = prm.AsInteger();
                        break;
                    case StorageType.String:
                        value = prm.AsString() ?? "";
                        break;
                    case StorageType.ElementId:
                        value = prm.AsElementId()?.IntegerValue ?? -1;
                        break;
                }
            }
            catch { value = null; }

            // display が空文字のままだが値がある場合は、最後の手段として value.ToString() を表示に使う
            if (string.IsNullOrEmpty(display) && value != null)
            {
                try { display = value.ToString() ?? ""; } catch { display = ""; }
            }

            return new
            {
                name,
                storageType = storage,
                isReadOnly = isRO,
                dataType,
                value,
                display
            };
        }

        /// <summary>
        /// SI 値（JToken: number/string/bool等）から Parameter に設定。
        /// Double は Spec を見て内部値へ変換、他は StorageType に応じて安全に解釈。
        /// </summary>
        public static bool TrySetParameterFromSi(Parameter prm, JToken token, out string reason)
        {
            reason = null;
            if (prm == null) { reason = "Parameter is null."; return false; }
            if (prm.IsReadOnly) { reason = $"Parameter '{prm.Definition?.Name}' is read-only."; return false; }

            try
            {
                switch (prm.StorageType)
                {
                    case StorageType.String:
                        return prm.Set(token?.Value<string>() ?? "");

                    case StorageType.Integer:
                        {
                            if (token != null && token.Type == JTokenType.Boolean)
                                return prm.Set(token.Value<bool>() ? 1 : 0);
                            if (TryToInt(token?.ToObject<object>(), out var iv))
                                return prm.Set(iv);
                            reason = "Expected integer (or boolean) value.";
                            return false;
                        }

                    case StorageType.ElementId:
                        {
                            if (TryToInt(token?.ToObject<object>(), out var iv))
                                return prm.Set(new ElementId(iv));
                            reason = "Expected integer ElementId.";
                            return false;
                        }

                    case StorageType.Double:
                        {
                            if (!TryToDouble(token?.ToObject<object>(), out var dv))
                            {
                                reason = "Expected numeric (double) value.";
                                return false;
                            }
                            var spec = TryGetSpec(prm);
                            var internalVal = ToInternal(dv, spec);
                            return prm.Set(internalVal);
                        }
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }

            reason = "Unsupported StorageType.";
            return false;
        }

        /// <summary>
        /// レベル標高 + CEILING_HEIGHTABOVELEVEL_PARAM を mm で返す。
        /// 天井系コマンドの Z 平坦化などで使用。 
        /// </summary>
        public static double CeilingElevationMm(Document doc, Autodesk.Revit.DB.Ceiling c)
        {
            double baseMm = 0.0;
            try
            {
                var level = doc.GetElement(c.LevelId) as Level;
                baseMm = (level != null) ? UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters) : 0.0;
            }
            catch { /* ignore */ }

            try
            {
                var p = c.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.Double)
                    return baseMm + UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            }
            catch { /* ignore */ }

            return baseMm;
        }

        /// <summary>PlanarFace から曲線ループ群を安全に取得（null/例外に強い）。</summary>
        public static IList<CurveLoop> SafeGetLoops(PlanarFace pf)
        {
            try
            {
                var loops = pf.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0) return new List<CurveLoop>();
                return loops;
            }
            catch
            {
                return new List<CurveLoop>();
            }
        }

        /// <summary>
        /// CurveLoop の面積(ft^2)と周長(ft)をテッセレーション＋平面投影で概算。
        /// （GetCeilingBoundariesCommand の計算と整合） 
        /// </summary>
        public static void ComputeAreaPerimeterFt(CurveLoop loop, PlanarFace pf, out double areaFt2, out double perimFt)
        {
            var pts = new List<XYZ>();
            foreach (var crv in loop)
            {
                var tess = crv.Tessellate();
                if (tess != null && tess.Count > 0)
                {
                    if (pts.Count > 0 && pts[pts.Count - 1].IsAlmostEqualTo(tess[0]) && tess.Count > 1)
                        pts.AddRange(tess.Skip(1));
                    else
                        pts.AddRange(tess);
                }
            }

            // perimeter (ft)
            perimFt = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                perimFt += a.DistanceTo(b);
            }

            // PlanarFace 座標系（XY）で Shoelace 面積（ft^2）
            var o = pf.Origin;
            var xAxis = pf.XVector;
            var yAxis = pf.YVector;
            var poly2 = pts.Select(p =>
            {
                var v = p - o;
                return new XYZ(v.DotProduct(xAxis), v.DotProduct(yAxis), 0);
            }).ToList();

            double area2D = 0.0;
            for (int i = 0; i < poly2.Count; i++)
            {
                var a = poly2[i];
                var b = poly2[(i + 1) % poly2.Count];
                area2D += (a.X * b.Y - a.Y * b.X);
            }
            areaFt2 = Math.Abs(area2D) * 0.5;
        }

        /// <summary>
        /// 曲線（Line/Arc/NURBS など）を mm スケールのエッジ詳細に展開。
        /// zOverrideMm を与えると Z を平坦化。
        /// </summary>
        public static List<object> ExtractEdgeDetailsMm(CurveLoop loop, PlanarFace pf, double? zOverrideMm, int decimals)
        {
            var edges = new List<object>();
            foreach (var crv in loop)
            {
                if (crv is Line ln)
                {
                    var s = ln.GetEndPoint(0);
                    var e = ln.GetEndPoint(1);
                    edges.Add(new
                    {
                        kind = "line",
                        start = ToPtMmPrivate(s, zOverrideMm, decimals),
                        end = ToPtMmPrivate(e, zOverrideMm, decimals),
                        lengthMm = Math.Round(UnitUtils.ConvertFromInternalUnits(ln.ApproximateLength, UnitTypeId.Millimeters), decimals)
                    });
                }
                else if (crv is Arc arc)
                {
                    var s = arc.GetEndPoint(0);
                    var e = arc.GetEndPoint(1);
                    var m = arc.Evaluate(0.5, true);
                    edges.Add(new
                    {
                        kind = "arc",
                        start = ToPtMmPrivate(s, zOverrideMm, decimals),
                        mid = ToPtMmPrivate(m, zOverrideMm, decimals),
                        end = ToPtMmPrivate(e, zOverrideMm, decimals),
                        radiusMm = Math.Round(UnitUtils.ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters), decimals),
                        lengthMm = Math.Round(UnitUtils.ConvertFromInternalUnits(arc.ApproximateLength, UnitTypeId.Millimeters), decimals)
                    });
                }
                else
                {
                    var tess = crv.Tessellate();
                    if (tess != null && tess.Count > 1)
                    {
                        edges.Add(new
                        {
                            kind = "poly",
                            points = tess.Select(pt => ToPtMmPrivate(pt, zOverrideMm, decimals)).ToList(),
                            lengthMm = Math.Round(UnitUtils.ConvertFromInternalUnits(crv.ApproximateLength, UnitTypeId.Millimeters), decimals)
                        });
                    }
                }
            }
            return edges;
        }

        private static object ToPtMmPrivate(XYZ p, double? zOverrideMm, int decimals)
        {
            var zmm = zOverrideMm ?? UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters);
            return new
            {
                x = Math.Round(UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters), decimals),
                y = Math.Round(UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters), decimals),
                z = Math.Round(zmm, decimals)
            };
        }

        // ============================================================
        // 内部：共通実装（Spec 取得/変換/パース等）
        // ============================================================
        private static ForgeTypeId? TryGetSpec(Parameter p)
        {
            var def = p.Definition;
            if (def == null) return null;

            // 1) GetDataType()（Revit 2023+）
            try
            {
                var mi = typeof(Definition).GetMethod("GetDataType", BindingFlags.Instance | BindingFlags.Public);
                if (mi != null)
                {
                    var ftid = mi.Invoke(def, null) as ForgeTypeId;
                    if (ftid != null) return ftid;
                }
            }
            catch { /* 次へ */ }

            // 2) GetDataTypeId()（環境差吸収）
            try
            {
                var mi = typeof(Definition).GetMethod("GetDataTypeId", BindingFlags.Instance | BindingFlags.Public);
                if (mi != null)
                {
                    var ftid = mi.Invoke(def, null) as ForgeTypeId;
                    if (ftid != null) return ftid;
                }
            }
            catch { /* 次へ */ }

            // 3) ParameterType フォールバック（文字列名でマップ）
            try
            {
                var ptProp = def.GetType().GetProperty("ParameterType", BindingFlags.Instance | BindingFlags.Public);
                if (ptProp != null)
                {
                    var ptObj = ptProp.GetValue(def);
                    var name = ptObj?.ToString() ?? string.Empty;
                    if (string.Equals(name, "Length", StringComparison.OrdinalIgnoreCase)) return SpecTypeId.Length;
                    if (string.Equals(name, "Area", StringComparison.OrdinalIgnoreCase)) return SpecTypeId.Area;
                    if (string.Equals(name, "Volume", StringComparison.OrdinalIgnoreCase)) return SpecTypeId.Volume;
                    if (string.Equals(name, "Angle", StringComparison.OrdinalIgnoreCase)) return SpecTypeId.Angle;
                }
            }
            catch { }

            return null;
        }

        private static double ConvertOutBySpec(double rawInternal, ForgeTypeId? spec, int siDigits)
        {
            if (spec == null) return rawInternal;
            if (spec.Equals(SpecTypeId.Length))
                return Round(UnitUtils.ConvertFromInternalUnits(rawInternal, UnitTypeId.Millimeters), siDigits);
            if (spec.Equals(SpecTypeId.Area))
                return Round(UnitUtils.ConvertFromInternalUnits(rawInternal, UnitTypeId.SquareMeters), siDigits);
            if (spec.Equals(SpecTypeId.Volume))
                return Round(UnitUtils.ConvertFromInternalUnits(rawInternal, UnitTypeId.CubicMeters), siDigits);
            if (spec.Equals(SpecTypeId.Angle))
                return Round(InternalToDeg(rawInternal), siDigits);
            return rawInternal; // 未知 Spec → raw
        }

        private static string SafeAsString(Parameter p)
        {
            try { return p.AsString() ?? string.Empty; } catch { return string.Empty; }
        }

        private static double SafeAsDouble(Parameter p)
        {
            try { return p.AsDouble(); } catch { return 0.0; }
        }

        private static double Round(double v, int digits) =>
            Math.Round(v, digits, MidpointRounding.AwayFromZero);

        private static bool TryToInt(object v, out int i)
        {
            if (v is int ii) { i = ii; return true; }
            if (v is long l && l >= int.MinValue && l <= int.MaxValue) { i = (int)l; return true; }
            if (v is double d) { i = (int)d; return true; }
            if (v is float f) { i = (int)f; return true; }
            if (v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) { i = p; return true; }
            i = 0; return false;
        }

        private static bool TryToDouble(object v, out double d)
        {
            if (v is double dd) { d = dd; return true; }
            if (v is float f) { d = f; return true; }
            if (v is int i) { d = i; return true; }
            if (v is long l) { d = l; return true; }
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) { d = p; return true; }
            d = 0; return false;
        }

        // ---- DoubleKind（既存の判定とラベル）----
        public enum DoubleKind
        {
            Unknown = 0,
            Length,
            Area,
            Volume,
            Angle,
            Other
        }

        public static DoubleKind ClassifyDoubleSpec(ForgeTypeId? spec)
        {
            if (spec == null) return DoubleKind.Other;
            if (spec.Equals(SpecTypeId.Length)) return DoubleKind.Length;
            if (spec.Equals(SpecTypeId.Area)) return DoubleKind.Area;
            if (spec.Equals(SpecTypeId.Volume)) return DoubleKind.Volume;
            if (spec.Equals(SpecTypeId.Angle)) return DoubleKind.Angle;
            return DoubleKind.Other;
        }

        public static DoubleKind ClassifyDoubleParameter(Parameter p)
        {
            if (p == null || p.StorageType != StorageType.Double) return DoubleKind.Other;
            return ClassifyDoubleSpec(TryGetSpec(p));
        }

        public static double ToInternal(double v, DoubleKind kind)
        {
            switch (kind)
            {
                case DoubleKind.Length: return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
                case DoubleKind.Area: return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.SquareMeters);
                case DoubleKind.Volume: return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.CubicMeters);
                case DoubleKind.Angle: return DegToInternal(v);
                default: return v;
            }
        }

        public static double ToExternal(double internalVal, DoubleKind kind, int siDigits = 3)
        {
            double v = internalVal;
            switch (kind)
            {
                case DoubleKind.Length: v = UnitUtils.ConvertFromInternalUnits(internalVal, UnitTypeId.Millimeters); break;
                case DoubleKind.Area: v = UnitUtils.ConvertFromInternalUnits(internalVal, UnitTypeId.SquareMeters); break;
                case DoubleKind.Volume: v = UnitUtils.ConvertFromInternalUnits(internalVal, UnitTypeId.CubicMeters); break;
                case DoubleKind.Angle: v = InternalToDeg(internalVal); break;
                default: break;
            }
            return Math.Round(v, siDigits, MidpointRounding.AwayFromZero);
        }

        public static string UnitLabel(DoubleKind kind)
        {
            switch (kind)
            {
                case DoubleKind.Length: return "mm";
                case DoubleKind.Area: return "m2";
                case DoubleKind.Volume: return "m3";
                case DoubleKind.Angle: return "deg";
                default: return "raw";
            }
        }

        // 期待名: ConvertDoubleBySpec(raw, spec, digits)
        // 既存実装: ToExternal(raw, spec, digits) が等価
        public static object ConvertDoubleBySpec(double rawInternal, ForgeTypeId spec, int digits = 3)
        {
            var v = ToExternal(rawInternal, spec, digits);
            return (object)(v ?? rawInternal);
        }

        // 期待名: ToInternalBySpec(user, spec)
        // 既存実装: ToInternal(user, spec) が等価
        public static double ToInternalBySpec(double userExternal, ForgeTypeId spec)
            => ToInternal(userExternal, spec);

        // 期待名: Rad↔Deg の簡易ユーティリティ
        public static double RadToDeg(double rad) => InternalToDeg(rad);
        public static double DegToRad(double deg) => DegToInternal(deg);

        // 期待名: ConvertFromInternalBySpec(raw, spec, target)
        // target: "auto"|"mm"|"m2"|"m3"|"deg"
        // ApplyConditionalColoringCommand 等からの呼び出しに対応
        public static double ConvertFromInternalBySpec(double raw, ForgeTypeId? spec, string target)
        {
            // "auto" は Spec を見て Length→mm, Area→m2, Volume→m3, Angle→deg, その他→raw
            if (target == null) target = "auto";

            if (target.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (spec != null)
                {
                    if (spec.Equals(SpecTypeId.Length))
                        return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Millimeters);
                    if (spec.Equals(SpecTypeId.Area))
                        return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters);
                    if (spec.Equals(SpecTypeId.Volume))
                        return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters);
                    if (spec.Equals(SpecTypeId.Angle))
                        return InternalToDeg(raw);
                }
                return raw;
            }

            // 明示ターゲット
            switch (target.ToLowerInvariant())
            {
                case "mm":
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Millimeters);
                case "m2":
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters);
                case "m3":
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters);
                case "deg":
                    return InternalToDeg(raw);
                default:
                    return raw;
            }
        }
    }
}
