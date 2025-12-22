using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GeneralOps
{
    /// <summary>
    /// 指定 ElementId 群を選択状態にする。
    /// method: "select_elements"
    /// params:
    ///   elementIds?: int[]   // 優先
    ///   elementId?:  int     // 単一指定の糖衣
    ///   zoomTo?:     bool    // true なら最初の有効要素へズーム（表示）
    /// </summary>
    public class SelectElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "select_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = (JObject)(cmd.Params ?? new JObject());
            // Optional execution guard
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            // 入力の正規化：elementIds[] または elementId
            var ids = new List<int>();
            if (p.TryGetValue("elementIds", out var arrTok) && arrTok is JArray arr && arr.Count > 0)
            {
                ids.AddRange(arr.Values<int>());
            }
            else if (p.TryGetValue("elementId", out var oneTok))
            {
                ids.Add(oneTok.Value<int>());
            }

            if (ids.Count == 0)
            {
                return new { ok = false, msg = "elementIds もしくは elementId を指定してください。" };
            }

            bool zoomTo = p.Value<bool?>("zoomTo") ?? false;

            var found = new List<ElementId>();
            var notFound = new List<int>();
            var notSelectable = new List<int>();

            foreach (var i in ids.Distinct())
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(i));
                if (e == null)
                {
                    notFound.Add(i);
                    continue;
                }

                // ビュー固有要素が別ビューのもの等は選択不可になる場合がある
                // Revit は SetElementIds 実行時に無効要素は無視されるため、ここで軽くフィルタ
                if (e.ViewSpecific)
                {
                    // オーナービューと現在ビューが違うなら選択不可の可能性が高い
                    if (e.OwnerViewId != ElementId.InvalidElementId &&
                        e.OwnerViewId != doc.ActiveView?.Id)
                    {
                        notSelectable.Add(i);
                        continue;
                    }
                }

                // カテゴリ無し・タイプ要素等も選択できない場合がある
                // ここではインスタンス要素中心に通す（Revit 側で弾かれても結果に反映）
                found.Add(e.Id);
            }

            // 選択をセット（存在しないIDは無視される）
            try
            {
                uidoc.Selection.SetElementIds(found);
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    msg = "選択の設定に失敗しました。",
                    details = ex.Message
                };
            }

            // 任意ズーム
            if (zoomTo && found.Count > 0)
            {
                try { uidoc.ShowElements(found[0]); }
                catch { /* ベストエフォート */ }
            }

            return new
            {
                ok = true,
                requested = ids.Count,
                selected = found.Count,
                elementIds = found.Select(eid => eid.IntValue()).ToList(),
                notFound,
                notSelectable
            };
        }
    }
}


