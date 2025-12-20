#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    /// <summary>
    /// get_fill_patterns
    /// プロジェクト内の FillPatternElement 一覧を取得します。
    /// params:
    ///   {
    ///     "target": "Drafting" | "Model" | "Any",  // 任意。省略時は Any
    ///     "solidOnly": true|false,                 // 任意。true=ソリッドのみ, false=ソリッド以外, 省略=全て
    ///     "nameContains": "Solid"                  // 任意。部分一致（大文字小文字無視）
    ///   }
    /// </summary>
    public class GetFillPatternsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_fill_patterns";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            string? targetStr = p.Value<string>("target");
            string? nameContains = p.Value<string>("nameContains");
            bool? solidOnlyOpt = p.Value<bool?>("solidOnly");

            FillPatternTarget? targetFilter = null;
            if (!string.IsNullOrWhiteSpace(targetStr) && !string.Equals(targetStr, "Any", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<FillPatternTarget>(targetStr, ignoreCase: true, out var t))
                {
                    targetFilter = t;
                }
            }

            var col = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .ToList();

            var items = new List<object>();

            foreach (var fpElem in col)
            {
                var pat = fpElem.GetFillPattern();
                if (pat == null) continue;

                if (targetFilter.HasValue && pat.Target != targetFilter.Value)
                    continue;

                if (solidOnlyOpt.HasValue)
                {
                    if (solidOnlyOpt.Value && !pat.IsSolidFill) continue;
                    if (!solidOnlyOpt.Value && pat.IsSolidFill) continue;
                }

                if (!string.IsNullOrWhiteSpace(nameContains))
                {
                    if (fpElem.Name == null ||
                        fpElem.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                items.Add(new
                {
                    elementId = fpElem.Id.IntegerValue,
                    name = fpElem.Name,
                    isSolid = pat.IsSolidFill,
                    target = pat.Target.ToString()
                });
            }

            return new
            {
                ok = true,
                totalCount = items.Count,
                items
            };
        }
    }
}

