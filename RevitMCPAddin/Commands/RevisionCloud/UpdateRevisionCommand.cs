// File: RevitMCPAddin/Commands/RevisionCloud/UpdateRevisionCommand.cs
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// 既存の Revision 要素の情報（説明、発行者、受領者、日付、番号シーケンス）を更新します
    /// </summary>
    public class UpdateRevisionCommand : IRevitCommandHandler
    {
        public string CommandName => "update_revision";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // Revision 要素解決（revisionId または uniqueId）
            Autodesk.Revit.DB.Revision revElem = null;
            int revIdInt = p.Value<int?>("revisionId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (revIdInt > 0) revElem = doc.GetElement(new ElementId(revIdInt)) as Autodesk.Revit.DB.Revision;
            else if (!string.IsNullOrWhiteSpace(uniqueId)) revElem = doc.GetElement(uniqueId) as Autodesk.Revit.DB.Revision;
            if (revElem == null) return new { ok = false, msg = (revIdInt > 0 ? $"Revision not found: {revIdInt}" : $"Revision not found by uniqueId: '{uniqueId}'") };

            using (var tx = new Transaction(doc, "Update Revision"))
            {
                try
                {
                    tx.Start();

                    // 説明（Description）
                    if (p.TryGetValue("description", out JToken descTok))
                        revElem.Description = descTok.Type == JTokenType.Null ? string.Empty : descTok.Value<string>();

                    // 発行者（IssuedBy）
                    if (p.TryGetValue("issuedBy", out JToken issuedByTok))
                        revElem.IssuedBy = issuedByTok.Type == JTokenType.Null ? string.Empty : issuedByTok.Value<string>();

                    // 受領者（IssuedTo）
                    if (p.TryGetValue("issuedTo", out JToken issuedToTok))
                        revElem.IssuedTo = issuedToTok.Type == JTokenType.Null ? string.Empty : issuedToTok.Value<string>();

                    // 日付（RevisionDate は string）
                    if (p.TryGetValue("revisionDate", out JToken dateTok))
                        revElem.RevisionDate = dateTok.Type == JTokenType.Null ? string.Empty : dateTok.Value<string>();

                    // 発行フラグ（Issued）
                    if (p.TryGetValue("issued", out JToken issuedTok) && issuedTok.Type == JTokenType.Boolean)
                        revElem.Issued = issuedTok.Value<bool>();

                    // 番号シーケンス（Id 直接 or 名前解決）
                    if (p.TryGetValue("revisionNumberingSequenceId", out JToken seqTok) && seqTok.Type == JTokenType.Integer)
                    {
                        TrySetSequenceIdSafe(revElem, new ElementId(seqTok.Value<int>()));
                    }
                    else if (p.TryGetValue("revisionNumberingSequenceName", out JToken nameTok) && nameTok.Type == JTokenType.String)
                    {
                        var seqId = FindSequenceIdByName(doc, nameTok.Value<string>());
                        if (seqId == ElementId.InvalidElementId)
                        {
                            tx.RollBack();
                            return new { ok = false, msg = $"Revision numbering sequence not found by name." };
                        }
                        TrySetSequenceIdSafe(revElem, seqId);
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new { ok = true, revisionId = revElem.Id.IntegerValue };
        }

        private static ElementId FindSequenceIdByName(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            try
            {
                var seqs = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.GetType().Name.IndexOf("RevisionNumberingSequence", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                foreach (var e in seqs)
                {
                    if (string.Equals(e.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase)) return e.Id;
                }
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        private static void TrySetSequenceIdSafe(Autodesk.Revit.DB.Revision rev, ElementId newSeqId)
        {
            if (newSeqId == null || newSeqId == ElementId.InvalidElementId) return;
            try
            {
                if (rev.RevisionNumberingSequenceId != null && rev.RevisionNumberingSequenceId.IntegerValue == newSeqId.IntegerValue) return;
                rev.RevisionNumberingSequenceId = newSeqId;
            }
            catch { /* setter 非対応環境ではスキップ */ }
        }
    }
}
