#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Commands.ScheduleOps;

namespace RevitMCPAddin.Core
{
    internal static class HtmlScheduleImportQueueService
    {
        private const int ScanIntervalSeconds = 15;
        private const int RePromptMinutes = 5;
        private static DateTime _lastScanUtc = DateTime.MinValue;
        private static int _prompting = 0;

        public static void OnIdling(UIApplication? uiapp)
        {
            if (uiapp == null) return;
            if (System.Threading.Interlocked.CompareExchange(ref _prompting, 0, 0) != 0) return;

            var now = DateTime.UtcNow;
            if (_lastScanUtc != DateTime.MinValue && (now - _lastScanUtc).TotalSeconds < ScanIntervalSeconds)
                return;
            _lastScanUtc = now;

            var entry = TryLoadNextDueEntry();
            if (entry == null) return;
            if (System.Threading.Interlocked.Exchange(ref _prompting, 1) != 0) return;

            try
            {
                ProcessEntry(uiapp, entry);
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"HtmlScheduleImportQueueService.ProcessEntry failed: {ex.Message}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _prompting, 0);
            }
        }

        public static int GetQueuedCountForActiveDocument(UIApplication? uiapp)
        {
            var docGuid = TryGetActiveDocGuid(uiapp);
            if (string.IsNullOrWhiteSpace(docGuid))
                return 0;

            return EnumerateQueuedEntries(docGuid, dueOnly: false).Count();
        }

        public static bool TryProcessNextNowForActiveDocument(UIApplication? uiapp, out string message)
        {
            message = string.Empty;
            if (uiapp == null)
            {
                message = "UIApplication が取得できません。";
                return false;
            }

            if (System.Threading.Interlocked.CompareExchange(ref _prompting, 0, 0) != 0)
            {
                message = "別のキュー確認処理を実行中です。";
                return false;
            }

            var docGuid = TryGetActiveDocGuid(uiapp);
            if (string.IsNullOrWhiteSpace(docGuid))
            {
                message = "アクティブ文書の docGuid を取得できません。";
                return false;
            }

            var entry = TryLoadNextEntry(docGuid, dueOnly: false);
            if (entry == null)
            {
                message = "この文書にキュー済みリクエストはありません。";
                return false;
            }

            if (System.Threading.Interlocked.Exchange(ref _prompting, 1) != 0)
            {
                message = "別のキュー確認処理を実行中です。";
                return false;
            }

            try
            {
                ProcessEntry(uiapp, entry);
                message = string.Equals(entry.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? "キュー済み変更を反映しました。"
                    : string.Equals(entry.Status, "deleted", StringComparison.OrdinalIgnoreCase)
                        ? "キューを削除しました。"
                        : string.Equals(entry.LastMessage, "closed-by-revit", StringComparison.OrdinalIgnoreCase)
                            ? "キュー確認ダイアログを閉じました。キューは保持されています。"
                        : "キュー済み変更の確認ダイアログを表示しました。";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _prompting, 0);
            }
        }

        private static void ProcessEntry(UIApplication uiapp, HtmlScheduleImportQueueEntry entry)
        {
            var doc = FindOpenDocumentByGuid(uiapp, entry.DocGuid);
            if (doc == null)
            {
                entry.LastMessage = "document-not-open";
                entry.NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(RePromptMinutes);
                entry.LastPromptUtc = DateTimeOffset.UtcNow;
                SaveEntry(entry);
                return;
            }

            var td = new TaskDialog("Queued HTML Import")
            {
                MainInstruction = string.IsNullOrWhiteSpace(entry.ScheduleName)
                    ? "キュー中の HTML 経由変更リクエストを処理しますか？"
                    : $"集計表「{entry.ScheduleName}」のキュー済み変更リクエストを処理しますか？",
                MainContent =
                    $"Document: {entry.DocTitle}\n" +
                    $"Requested by: {(string.IsNullOrWhiteSpace(entry.RequestedBy) ? "(unknown)" : entry.RequestedBy)}\n" +
                    $"Workbook: {entry.UploadedFileName}\n" +
                    $"Changed cells (preview): {entry.ChangedCellCount}\n\n" +
                    "「今すぐ反映」: ただちに反映\n" +
                    "「5分後に再確認」: キューを保持して後で再確認\n" +
                    "「キューを削除」: キューを破棄\n" +
                    "「閉じる」: ダイアログを閉じるだけ",
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close,
                TitleAutoPrefix = false
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "今すぐ反映");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "5分後に再確認");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "キューを削除");

            var result = td.Show();
            entry.LastPromptUtc = DateTimeOffset.UtcNow;

            if (result == TaskDialogResult.CommandLink1)
            {
                ExecuteImport(uiapp, entry);
                return;
            }

            if (result == TaskDialogResult.CommandLink2)
            {
                entry.Status = "queued";
                entry.AttemptCount++;
                entry.NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(RePromptMinutes);
                entry.LastMessage = "deferred-by-revit";
                SaveEntry(entry);
                return;
            }

            if (result == TaskDialogResult.CommandLink3)
            {
                entry.Status = "deleted";
                entry.DeletedUtc = DateTimeOffset.UtcNow;
                entry.LastMessage = "deleted-by-revit";
                SaveEntry(entry);
                TaskDialog.Show("Queued HTML Import", "キューを削除しました。\n反映しなかったことを送信者に別途連絡してください。");
                return;
            }

            entry.Status = "queued";
            entry.NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(RePromptMinutes);
            entry.LastMessage = "closed-by-revit";
            SaveEntry(entry);
        }

        private static void ExecuteImport(UIApplication uiapp, HtmlScheduleImportQueueEntry entry)
        {
            var projectDir = string.IsNullOrWhiteSpace(entry.ProjectFolderPath)
                ? GetProjectFolder(entry.DocTitle, entry.DocGuid)
                : entry.ProjectFolderPath;
            var reportDir = Path.Combine(projectDir, "Reports");
            var auditDir = Path.Combine(projectDir, "Audit");
            Directory.CreateDirectory(reportDir);
            Directory.CreateDirectory(auditDir);

            var baseName = Path.GetFileNameWithoutExtension(entry.UploadedFileName ?? "schedule_import");
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "schedule_import";
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var reportPath = Path.Combine(reportDir, $"{baseName}_import_report_{stamp}.csv");
            var auditJsonPath = Path.Combine(auditDir, $"{baseName}_changes_{stamp}.json");

            var previewCmd = new RequestCommand
            {
                Command = "preview_schedule_roundtrip_excel",
                Params = new JObject
                {
                    ["docGuid"] = entry.DocGuid,
                    ["docTitle"] = entry.DocTitle,
                    ["filePath"] = entry.UploadedFilePath
                }
            };

            var previewHandler = new PreviewScheduleRoundtripExcelCommand();
            var previewResult = previewHandler.Execute(uiapp, previewCmd);
            var previewRoot = previewResult != null ? JObject.FromObject(previewResult) : new JObject();
            var previewOk = previewRoot.Value<bool?>("ok") == true;
            if (!previewOk)
            {
                var previewMessage = FirstNonBlank(
                    previewRoot.Value<string>("detail"),
                    previewRoot.Value<string>("msg"),
                    "preview-failed");
                entry.Status = "queued";
                entry.AttemptCount++;
                entry.NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(RePromptMinutes);
                entry.LastMessage = previewMessage;
                SaveEntry(entry);
                RevitLogger.Warn($"HtmlScheduleImportQueueService.ExecuteImport preview failed: {previewMessage}");
                TaskDialog.Show("Queued HTML Import", "反映前の再確認に失敗しました。5分後に再確認します。\n" + previewMessage);
                return;
            }

            var changedCellCount = previewRoot.Value<int?>("changedCellCount") ?? 0;
            var failedCellCount = previewRoot.Value<int?>("failedCellCount") ?? 0;
            if (changedCellCount == 0 && failedCellCount == 0)
            {
                entry.Status = "completed";
                entry.CompletedUtc = DateTimeOffset.UtcNow;
                entry.LastMessage = "already-reflected";
                SaveEntry(entry);
                TaskDialog.Show("Queued HTML Import", "差分はありません。既に反映済みです。");
                return;
            }

            var cmd = new RequestCommand
            {
                Command = "import_schedule_roundtrip_excel",
                Params = new JObject
                {
                    ["docGuid"] = entry.DocGuid,
                    ["docTitle"] = entry.DocTitle,
                    ["filePath"] = entry.UploadedFilePath,
                    ["reportPath"] = reportPath,
                    ["auditJsonPath"] = auditJsonPath,
                    ["expectedValues"] = JToken.FromObject(entry.ExpectedValues ?? new List<HtmlScheduleImportExpectedValue>())
                }
            };

            var handler = new ImportScheduleRoundtripExcelCommand();
            var result = handler.Execute(uiapp, cmd);
            var root = result != null ? JObject.FromObject(result) : new JObject();
            var ok = root.Value<bool?>("ok") == true;

            if (ok)
            {
                var conflictCount = root.Value<int?>("conflictCount") ?? 0;
                entry.Status = "completed";
                entry.CompletedUtc = DateTimeOffset.UtcNow;
                entry.ReportPath = root.Value<string>("reportPath") ?? reportPath;
                entry.AuditJsonPath = root.Value<string>("auditJsonPath") ?? auditJsonPath;
                entry.LastMessage = conflictCount > 0 ? $"completed-with-conflicts:{conflictCount}" : "completed";
                SaveEntry(entry);
                TaskDialog.Show(
                    "Queued HTML Import",
                    conflictCount > 0
                        ? $"キュー済み変更の反映は完了しましたが、{conflictCount} 件は Revit 側の値が preview 時から変わっていたため反映しませんでした。"
                        : "キュー済み変更の反映が完了しました。");
                return;
            }

            entry.Status = "queued";
            entry.AttemptCount++;
            entry.NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(RePromptMinutes);
            entry.LastMessage = FirstNonBlank(
                root.Value<string>("detail"),
                root.Value<string>("msg"),
                "import-failed");
            SaveEntry(entry);
            RevitLogger.Warn($"HtmlScheduleImportQueueService.ExecuteImport import failed: {entry.LastMessage}");
            TaskDialog.Show("Queued HTML Import", "反映に失敗しました。5分後に再確認します。\n" + entry.LastMessage);
        }

        private static HtmlScheduleImportQueueEntry? TryLoadNextDueEntry()
            => TryLoadNextEntry(docGuid: null, dueOnly: true);

        private static HtmlScheduleImportQueueEntry? TryLoadNextEntry(string? docGuid, bool dueOnly)
        {
            try
            {
                return EnumerateQueuedEntries(docGuid, dueOnly)
                    .OrderBy(x => x.NextPromptUtc)
                    .ThenBy(x => x.CreatedUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<HtmlScheduleImportQueueEntry> EnumerateQueuedEntries(string? docGuid, bool dueOnly)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit_MCP", "Projects");
            if (!Directory.Exists(root))
                return Enumerable.Empty<HtmlScheduleImportQueueEntry>();

            var now = DateTimeOffset.UtcNow;
            return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty), "Queue", StringComparison.OrdinalIgnoreCase))
                .Select(LoadEntry)
                .Where(x => x != null
                            && string.Equals(x.Status, "queued", StringComparison.OrdinalIgnoreCase)
                            && (!dueOnly || x.NextPromptUtc <= now)
                            && (string.IsNullOrWhiteSpace(docGuid) || string.Equals(x.DocGuid, docGuid, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x!);
        }

        private static HtmlScheduleImportQueueEntry? LoadEntry(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                var entry = JsonConvert.DeserializeObject<HtmlScheduleImportQueueEntry>(text);
                if (entry == null) return null;
                entry.QueueFilePath = path;
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveEntry(HtmlScheduleImportQueueEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.QueueFilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(entry.QueueFilePath) ?? string.Empty);
            File.WriteAllText(entry.QueueFilePath, JsonConvert.SerializeObject(entry, Formatting.Indented));
        }

        private static Document? FindOpenDocumentByGuid(UIApplication uiapp, string docGuid)
        {
            if (string.IsNullOrWhiteSpace(docGuid)) return null;
            foreach (Document doc in uiapp.Application.Documents)
            {
                try
                {
                    var key = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _);
                    if (string.Equals(key, docGuid, StringComparison.OrdinalIgnoreCase))
                        return doc;
                }
                catch
                {
                }
            }
            return null;
        }

        private static string? TryGetActiveDocGuid(UIApplication? uiapp)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return null;

            try
            {
                return DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _);
            }
            catch
            {
                return null;
            }
        }

        private static string GetProjectFolder(string docTitle, string docGuid)
        {
            return Paths.ResolveManagedProjectFolder(docTitle, docGuid)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit_MCP", "Projects", "Project_unknown");
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value!.Trim();
            }

            return string.Empty;
        }

        private sealed class HtmlScheduleImportQueueEntry
        {
            public string QueueId { get; set; } = string.Empty;
            public string QueueFilePath { get; set; } = string.Empty;
            public string PreviewToken { get; set; } = string.Empty;
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public string ScheduleName { get; set; } = string.Empty;
            public string UploadedFilePath { get; set; } = string.Empty;
            public string UploadedFileName { get; set; } = string.Empty;
            public string RequestedBy { get; set; } = string.Empty;
            public string ProjectFolderPath { get; set; } = string.Empty;
            public int ChangedCellCount { get; set; }
            public int EditableColumnCount { get; set; }
            public List<HtmlScheduleImportExpectedValue> ExpectedValues { get; set; } = new List<HtmlScheduleImportExpectedValue>();
            public string Status { get; set; } = "queued";
            public int AttemptCount { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset NextPromptUtc { get; set; }
            public DateTimeOffset LastPromptUtc { get; set; }
            public DateTimeOffset CompletedUtc { get; set; }
            public DateTimeOffset DeletedUtc { get; set; }
            public string LastMessage { get; set; } = string.Empty;
            public string ReportPath { get; set; } = string.Empty;
            public string AuditJsonPath { get; set; } = string.Empty;
        }

        private sealed class HtmlScheduleImportExpectedValue
        {
            public int Row { get; set; }
            public int OutputColumnNumber { get; set; }
            public int ElementId { get; set; }
            public string Header { get; set; } = string.Empty;
            public string ParameterName { get; set; } = string.Empty;
            public string ExpectedComparable { get; set; } = string.Empty;
            public string ExpectedDisplay { get; set; } = string.Empty;
            public string ImportedComparable { get; set; } = string.Empty;
        }
    }
}
