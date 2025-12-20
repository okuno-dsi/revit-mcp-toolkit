// ================================================================
// File: Commands/DocumentOps/UpdateProjectInfoCommand.cs
// 概要 : Revit の Project Information（プロジェクト情報）を書き換え
// 使い方 : { method: "update_project_info", params:{ projectName:"...", projectNumber:"...", clientName:"...", status:"...", issueDate:"...", address:"..." } }
// 備考 : 文字列パラメータのみ対応。存在しない/読み取り専用の項目は安全にスキップ。
// 対応 : Revit 2023 / .NET Framework 4.8 / C# 8
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    public class UpdateProjectInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "update_project_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var pi = doc.ProjectInformation;
            if (pi == null)
            {
                return new { ok = false, msg = "ProjectInformation 要素が見つかりません。" };
            }

            // 入力パラメータ（文字列のみ）
            var p = cmd.Params;
            string? projectName = TryGetString(p, "projectName");
            string? projectNumber = TryGetString(p, "projectNumber");
            string? clientName = TryGetString(p, "clientName");
            string? status = TryGetString(p, "status");
            string? issueDate = TryGetString(p, "issueDate");
            string? address = TryGetString(p, "address");

            if (projectName == null && projectNumber == null && clientName == null && status == null && issueDate == null && address == null)
            {
                return new { ok = false, msg = "更新対象のフィールドがありません。projectName / projectNumber / clientName / status / issueDate / address のいずれかを指定してください。" };
            }

            int updated = 0;
            try
            {
                using (var tx = new Transaction(doc, "Update Project Info"))
                {
                    tx.Start();

                    if (projectName != null)
                        updated += TrySetString(pi, BuiltInParameter.PROJECT_NAME, projectName) ? 1 : 0;
                    if (projectNumber != null)
                        updated += TrySetString(pi, BuiltInParameter.PROJECT_NUMBER, projectNumber) ? 1 : 0;
                    if (clientName != null)
                        updated += TrySetString(pi, BuiltInParameter.CLIENT_NAME, clientName) ? 1 : 0;
                    if (status != null)
                        updated += TrySetString(pi, BuiltInParameter.PROJECT_STATUS, status) ? 1 : 0;
                    if (issueDate != null)
                        updated += TrySetString(pi, BuiltInParameter.PROJECT_ISSUE_DATE, issueDate) ? 1 : 0;
                    if (address != null)
                        updated += TrySetString(pi, BuiltInParameter.PROJECT_ADDRESS, address) ? 1 : 0;

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }

            return new { ok = true, updated };
        }

        private static string? TryGetString(Newtonsoft.Json.Linq.JObject? p, string key)
        {
            try
            {
                var tok = p?[key];
                if (tok == null || tok.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                    return null;
                var s = tok.ToString();
                return s;
            }
            catch { return null; }
        }

        private static bool TrySetString(ProjectInfo pi, BuiltInParameter bip, string value)
        {
            try
            {
                var prm = pi.get_Parameter(bip);
                if (prm == null || prm.IsReadOnly) return false;
                if (prm.StorageType == StorageType.String)
                {
                    return prm.Set(value);
                }
                // Fallback: AsValueString を許容するタイプはほぼ無いが、念のため
                return prm.Set(value);
            }
            catch { return false; }
        }
    }
}

