// ================================================================
// File: Commands/ElementOps/Material/ThermalAssetHelpers.cs
// Helpers for editable Thermal assets and unit conversion
// Target: .NET Framework 4.8 / Revit 2023+
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

using ARDB = Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands.ElementOps.Material
{
    internal static class ThermalAssetUtil
    {
        public static ARDB.PropertySetElement EnsureEditable(
            ARDB.Document doc,
            ARDB.Material material,
            out ARDB.ThermalAsset asset,
            out bool duplicated,
            string preferredBaseName = null)
        {
            duplicated = false;

            var tid = material.ThermalAssetId;
            if (tid == null || tid == ARDB.ElementId.InvalidElementId)
                throw new InvalidOperationException("The material has no Thermal asset assigned.");

            var pse = doc.GetElement(tid) as ARDB.PropertySetElement
                     ?? throw new InvalidOperationException("Thermal PropertySetElement not found.");

            asset = pse.GetThermalAsset()
                    ?? throw new InvalidOperationException("ThermalAsset not found.");

            try
            {
                // ラウンドトリップで書き込み可否を確認（読み取り専用アセットはここで例外になることが多い）
                pse.SetThermalAsset(asset);
                return pse;
            }
            catch
            {
                return DuplicateAndRebind(doc, material, pse, out asset, out duplicated, preferredBaseName);
            }
        }

        public static ARDB.PropertySetElement DuplicateAndRebind(
            ARDB.Document doc,
            ARDB.Material material,
            ARDB.PropertySetElement sourcePse,
            out ARDB.ThermalAsset newAsset,
            out bool duplicated,
            string preferredBaseName = null)
        {
            duplicated = true;
            var baseName = preferredBaseName ?? ((sourcePse.Name ?? "ThermalAsset") + "_Editable");
            var newName = baseName;
            int i = 1;

            // 一意な名前を生成
            while (new ARDB.FilteredElementCollector(doc)
                   .OfClass(typeof(ARDB.PropertySetElement))
                   .Cast<ARDB.PropertySetElement>()
                   .Any(x => string.Equals(x.Name ?? string.Empty, newName, StringComparison.OrdinalIgnoreCase)))
            {
                newName = $"{baseName}_{i++}";
            }

            var dup = sourcePse.Duplicate(doc, newName) as ARDB.PropertySetElement
                      ?? throw new InvalidOperationException("Failed to duplicate Thermal asset.");

            newAsset = dup.GetThermalAsset()
                       ?? throw new InvalidOperationException("Failed to obtain duplicated ThermalAsset.");

            // Thermal アセットをマテリアルに再バインド
            material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, dup.Id);

            return dup;
        }

        public static bool NearlyEqual(double a, double b, double epsilon = 1e-9)
        {
            var diff = Math.Abs(a - b);
            if (diff <= epsilon)
                return true;
            var maxAbs = Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
            return diff <= epsilon * maxAbs;
        }
    }

    internal static class ThermalUnitUtil
    {
        /// <summary>
        /// value を W/(m·K) に正規化する。units が省略された場合はすでに SI とみなす。
        /// 対応例: "W/(m·K)", "W/(m*K)", "W/mK", "WperMK",
        ///        "BTU/(h·ft·°F)" 等。
        /// </summary>
        public static double ToWPerMeterK(double v, string units)
        {
            var u = (units ?? string.Empty)
                .Replace(" ", "")
                // 中黒/ミドルドットの表記揺れを吸収（'·' と '・' を同一視）
                .Replace("・", "·")
                .ToLowerInvariant();
            if (string.IsNullOrEmpty(u) ||
                u == "w/(m·k)" || u == "w/(m*k)" || u == "w/mk" || u == "wpermk")
            {
                return v;
            }

            if (u == "btu/(h·ft·°f)" || u == "btu/(h·ft·f)" ||
                u == "btu/(hftf)" || u == "btu/hr-ft-f" || u == "btuperhourfootf")
            {
                // 1 BTU/(h·ft·°F) → W/(m·K)
                return v * 1.730734908136483;
            }

            throw new ArgumentException("units must be 'W/(m·K)' or 'BTU/(h·ft·°F)' (or compatible variants).");
        }
    }

    /// <summary>
    /// ThermalAsset に対して複数の物性値（熱伝導率・密度・比熱）をまとめて設定するコマンド。
    /// CommandName: set_material_thermal_properties
    /// </summary>
    public class SetMaterialThermalPropertiesCommand : IRevitCommandHandler
    {
        public string CommandName => "set_material_thermal_properties";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(new ElementId(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null)
                return new { ok = false, msg = "Material が見つかりません (materialId/uniqueId)。" };

            var props = p["properties"] as JObject;
            if (props == null || !props.HasValues)
                return new { ok = false, msg = "properties オブジェクトが必要です。" };

            string kUnits = p.Value<string>("conductivityUnits") ?? "W/(m·K)";
            string rhoUnits = p.Value<string>("densityUnits") ?? "kg/m3";
            string cUnits = p.Value<string>("specificHeatUnits") ?? "J/(kg·K)";

            using (var tx = new ARDB.Transaction(doc, "Set Material Thermal Properties"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    bool duplicated;
                    ARDB.ThermalAsset asset;
                    var pse = ThermalAssetUtil.EnsureEditable(
                        doc,
                        material,
                        out asset,
                        out duplicated,
                        preferredBaseName: (material.Name ?? "Mat") + "_Thermal");

                    double? setK = null, setRho = null, setC = null;

                    if (props.TryGetValue("ThermalConductivity", out var vK) ||
                        props.TryGetValue("thermalConductivity", out vK) ||
                        props.TryGetValue("lambda", out vK))
                    {
                        // 1) ユーザー入力を W/(m·K) に正規化
                        var wPerMK = ThermalUnitUtil.ToWPerMeterK(vK.Value<double>(), kUnits);
                        setK = ARDB.UnitUtils.ConvertToInternalUnits(
                            wPerMK,
                            ARDB.UnitTypeId.WattsPerMeterKelvin);

                        // 2) ThermalAsset.ThermalConductivity は W/(m·K) を期待すると仮定し、そのまま書き込む
                        // ThermalAsset has no Duplicate(); use existing instance
                        asset.ThermalConductivity = setK.Value;
                    }

                    if (props.TryGetValue("Density", out var vRho) ||
                        props.TryGetValue("density", out vRho))
                    {
                        // SI 入力前提（kg/m3）。必要であれば ToKgPerM3 を拡張。
                        setRho = vRho.Value<double>();
                        asset.Density = setRho.Value;
                    }

                    if (props.TryGetValue("SpecificHeat", out var vC) ||
                        props.TryGetValue("specificHeat", out vC) ||
                        props.TryGetValue("Cp", out vC))
                    {
                        // SI 入力前提（J/(kg·K)）。必要であれば ToJPerKgK を拡張。
                        setC = vC.Value<double>();
                        asset.SpecificHeat = setC.Value;
                    }

                    pse.SetThermalAsset(asset);

                    // 最低限の検証（ThermalConductivity のみ）
                    var verify = pse.GetThermalAsset();
                    bool needDup = setK.HasValue &&
                                   (verify == null || !ThermalAssetUtil.NearlyEqual(verify.ThermalConductivity, setK.Value));

                    if (needDup)
                    {
                        pse = ThermalAssetUtil.DuplicateAndRebind(
                            doc,
                            material,
                            pse,
                            out asset,
                            out duplicated,
                            preferredBaseName: (material.Name ?? "Mat") + "_Thermal");

                        if (setK.HasValue) asset.ThermalConductivity = setK.Value;
                        if (setRho.HasValue) asset.Density = setRho.Value;
                        if (setC.HasValue) asset.SpecificHeat = setC.Value;

                        pse.SetThermalAsset(asset);
                    }

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        materialId = material.Id.IntegerValue,
                        uniqueId = material.UniqueId,
                        updated = new
                        {
                            ThermalConductivity_W_per_mK = setK,
                            Density_kg_per_m3 = setRho,
                            SpecificHeat_J_per_kgK = setC
                        },
                        duplicatedAsset = duplicated
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }

    /// <summary>
    /// PropertySetElement（Thermal asset 含む）のパラメータ値を直接更新する汎用コマンド。
    /// 主に PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY などの物性パラメータ用。
    /// </summary>
    public class UpdatePropertySetElementParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_property_set_element_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            Element elem = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) elem = doc.GetElement(new ElementId(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) elem = doc.GetElement(uniqueId);

            if (elem == null)
                return new { ok = false, msg = "Element が見つかりません (elementId/uniqueId)。" };

            string builtInName = p.Value<string>("builtInName");
            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(builtInName) && string.IsNullOrWhiteSpace(paramName))
                return new { ok = false, msg = "builtInName または paramName のいずれかが必要です。" };

            if (!p.TryGetValue("value", out var valToken))
                return new { ok = false, msg = "value が必要です。" };

            Parameter param = null;
            string resolvedBy = null;

            if (!string.IsNullOrWhiteSpace(builtInName))
            {
                try
                {
                    var bip = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtInName, ignoreCase: true);
                    param = elem.get_Parameter(bip);
                    if (param != null) resolvedBy = "builtInName";
                }
                catch
                {
                    // ignore and fall through to paramName
                }
            }

            if (param == null && !string.IsNullOrWhiteSpace(paramName))
            {
                param = elem.LookupParameter(paramName);
                if (param != null) resolvedBy = "paramName";
            }

            if (param == null)
                return new { ok = false, msg = "Parameter not found (name/builtIn)." };

            if (param.IsReadOnly)
                return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' は読み取り専用です。" };

            using (var tx = new ARDB.Transaction(doc, "Update PropertySetElement Parameter"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double:
                            param.Set(valToken.Value<double>());
                            break;
                        case StorageType.Integer:
                            param.Set(valToken.Value<int>());
                            break;
                        case StorageType.String:
                            param.Set(valToken.Value<string>() ?? string.Empty);
                            break;
                        case StorageType.ElementId:
                            param.Set(new ElementId(valToken.Value<int>()));
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported StorageType for update.");
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                elementId = elem.Id.IntegerValue,
                uniqueId = elem.UniqueId,
                parameterName = param.Definition?.Name,
                resolvedBy,
                storageType = param.StorageType.ToString(),
                value = valToken
            };
        }
    }
}
