// File: Commands/DocumentOps/GetOpenDocumentsCommand.cs
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    /// <summary>
    /// 開いているすべての Document を列挙し、リンク関係とワークシェアリング情報を含めて返却。
    /// 互換フィールド  : title, path, projectName
    /// 追加フィールド  : isLinked, role("host"|"link"|"both"|"standalone"),
    ///                    isWorkshared, linkCount, links[], linkedInto[]
    /// links/linkedInto : { title, path, isWorkshared }
    /// </summary>
    public class GetOpenDocumentsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_open_documents";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var app = uiapp.Application;
            var allDocs = new List<Document>();
            foreach (Document d in app.Documents) allDocs.Add(d);

            // --- 1) 順方向（host -> links）/ 逆方向（link -> hosts）マップを構築 ---
            var hostToLinks = new Dictionary<Document, List<Document>>();
            var linkToHosts = new Dictionary<Document, List<Document>>();

            foreach (var hostDoc in allDocs)
            {
                var linkDocs = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .ToElements()
                    .OfType<RevitLinkInstance>()
                    .Select(li => li.GetLinkDocument())
                    .Where(ld => ld != null)
                    .Distinct()
                    .ToList();

                hostToLinks[hostDoc] = linkDocs;

                foreach (var ldoc in linkDocs)
                {
                    List<Document> hosts;
                    if (!linkToHosts.TryGetValue(ldoc, out hosts))
                    {
                        hosts = new List<Document>();
                        linkToHosts[ldoc] = hosts;
                    }
                    hosts.Add(hostDoc);
                }
            }

            // --- 2) 互換性配慮のリンク判定（Revit 2023 では反射で IsLinked を読む） ---
            bool IsLinked(Document d)
            {
                try
                {
                    var pi = typeof(Document).GetProperty("IsLinked");
                    if (pi != null && pi.PropertyType == typeof(bool))
                        return (bool)pi.GetValue(d, null);
                }
                catch { /* ignore */ }
                return false;
            }

            // Worksharing 判定（例外を飲み込んで false）
            bool SafeIsWorkshared(Document d)
            {
                try { return d.IsWorkshared; }
                catch { return false; }
            }

            string SafePath(Document d)
            {
                try
                {
                    var p = d.PathName;
                    if (string.IsNullOrWhiteSpace(p))
                        return "(未保存/クラウド/一時ファイル)";
                    return p;
                }
                catch { return "(取得不可)"; }
            }

            string SafeProjectName(Document d)
            {
                try
                {
                    var n = d.ProjectInformation != null ? d.ProjectInformation.Name : null;
                    if (!string.IsNullOrWhiteSpace(n)) return n;
                }
                catch { /* ignore */ }
                return d.Title ?? "";
            }

            // --- 3) 返却配列を作成（※List<object>に統一） ---
            var docs = new List<object>();
            var activeDoc = uiapp?.ActiveUIDocument?.Document;

            foreach (var d in allDocs)
            {
                bool linkedFlag = IsLinked(d);

                // このドキュメントが読み込んでいるリンク一覧
                var links = new List<object>();
                List<Document> loadedLinks;
                if (hostToLinks.TryGetValue(d, out loadedLinks) && loadedLinks != null && loadedLinks.Count > 0)
                {
                    foreach (var ld in loadedLinks)
                    {
                        links.Add(new
                        {
                            title = ld.Title,
                            path = SafePath(ld),
                            isWorkshared = SafeIsWorkshared(ld)
                        });
                    }
                }

                // このドキュメントを読み込んでいるホスト一覧
                var linkedInto = new List<object>();
                List<Document> hostList;
                if (linkToHosts.TryGetValue(d, out hostList) && hostList != null && hostList.Count > 0)
                {
                    foreach (var h in hostList)
                    {
                        linkedInto.Add(new
                        {
                            title = h.Title,
                            path = SafePath(h),
                            isWorkshared = SafeIsWorkshared(h)
                        });
                    }
                }

                // 役割のラベル化
                string role;
                bool isHost = links.Count > 0;
                if (linkedFlag && isHost) role = "both";
                else if (linkedFlag) role = "link";
                else if (isHost) role = "host";
                else role = "standalone";

                // A surrogate GUID for cross-port matching
                string guid = null;
                try { guid = d?.ProjectInformation?.UniqueId; } catch { }
                if (string.IsNullOrWhiteSpace(guid))
                {
                    try { var sig = ($"{d?.Title}|{SafePath(d)}"); guid = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sig)); } catch { guid = string.Empty; }
                }
                docs.Add(new
                {
                    title = d.Title,
                    path = SafePath(d),
                    projectName = SafeProjectName(d),
                    active = (activeDoc == d),
                    guid = guid,

                    // 追加情報
                    isLinked = linkedFlag,
                    role = role,                 // "host" | "link" | "both" | "standalone"
                    isWorkshared = SafeIsWorkshared(d),
                    linkCount = links.Count,
                    links = links,                // このドキュメントが読み込んでいるリンク
                    linkedInto = linkedInto           // このドキュメントを読み込んでいるホスト群
                });
            }

            return new { ok = true, count = docs.Count, documents = docs };
        }
    }
}