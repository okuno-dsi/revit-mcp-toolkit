// ================================================================
// File: Core/Failures/FailureCaptureScope.cs (C# 8.0 safe)
// 概要: コマンド実行中だけ FailuresProcessing / DialogBoxShowing を購読し、
//       警告・エラー・ダイアログを収集（必要に応じて警告はUI非表示で削除）
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitMCPAddin.Core.Failures;

namespace RevitMCPAddin.Core
{
    // 明示的に System.IDisposable を実装（名前の衝突を回避）
    public sealed class FailureCaptureScope : System.IDisposable
    {
        private readonly UIApplication _uiapp;
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly bool _suppressWarnings;
        private readonly bool _rollbackOnError;

        public CommandIssues Issues { get; private set; }

        private readonly EventHandler<FailuresProcessingEventArgs> _failHandler;
        private readonly EventHandler<DialogBoxShowingEventArgs> _dlgHandler;

        private bool _disposed;

        public FailureCaptureScope(
            UIApplication uiapp,
            bool suppressWarnings,
            bool rollbackOnError)
        {
            if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));

            _uiapp = uiapp;
            _app = uiapp.Application;
            _suppressWarnings = suppressWarnings;
            _rollbackOnError = rollbackOnError;

            Issues = new CommandIssues();

            _failHandler = new EventHandler<FailuresProcessingEventArgs>(OnFailuresProcessing);
            _dlgHandler = new EventHandler<DialogBoxShowingEventArgs>(OnDialogShowing);

            _app.FailuresProcessing += _failHandler;
            _uiapp.DialogBoxShowing += _dlgHandler;
        }

        // =========================
        //  FailuresProcessing
        // =========================
        private void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            var msgs = fa.GetFailureMessages();
            bool hasError = false;

            foreach (FailureMessageAccessor f in msgs)
            {
                FailureDefinitionId defId = f.GetFailureDefinitionId();
                FailureSeverity sev = f.GetSeverity();
                string message = f.GetDescriptionText();
                int[] ids = (f.GetFailingElementIds() != null)
                    ? f.GetFailingElementIds().Select(x => x.IntValue()).ToArray()
                    : new int[0];

                Issues.failures.Add(new FailureRecord
                {
                    id = defId.ToString(),
                    idGuid = defId.Guid.ToString(),
                    severity = sev.ToString(),
                    message = message,
                    elementIds = ids
                });

                if (sev == FailureSeverity.Warning && _suppressWarnings)
                {
                    // 警告はUIに出さず削除
                    fa.DeleteWarning(f);
                }
                if (sev == FailureSeverity.Error)
                {
                    hasError = true;
                }
            }

            if (hasError && _rollbackOnError)
            {
                FailureHandlingOptions opts = fa.GetFailureHandlingOptions();
                opts.SetClearAfterRollback(true);
                fa.SetFailureHandlingOptions(opts);
                e.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
                return;
            }

            e.SetProcessingResult(FailureProcessingResult.Continue);
        }

        // =========================
        //  DialogBoxShowing
        // =========================
        private void OnDialogShowing(object sender, DialogBoxShowingEventArgs e)
        {
            string dialogId = string.Empty;
            try { dialogId = e.DialogId ?? string.Empty; } catch { dialogId = string.Empty; }

            string message = string.Empty;
            string dialogType = e.GetType().Name;
            string title = string.Empty;
            string mainInstruction = string.Empty;
            string expandedContent = string.Empty;
            string footer = string.Empty;
            try
            {
                var td = e as TaskDialogShowingEventArgs;
                if (td != null)
                {
                    dialogType = "TaskDialog";
                    message = td.Message ?? string.Empty;
                    title = TryGetStringProp(td, "Title");
                    mainInstruction = TryGetStringProp(td, "MainInstruction");
                    expandedContent = TryGetStringProp(td, "ExpandedContent");
                    footer = TryGetStringProp(td, "FooterText");
                }
                else
                {
                    message = TryGetStringProp(e, "Message");
                }
            }
            catch { /* ignore */ }

            DialogCaptureItem capItem = null;
            try
            {
                var capRes = DialogCaptureUtil.TryCaptureActiveDialogs();
                capItem = DialogCaptureUtil.PickPrimaryCapture(capRes);
            }
            catch { /* ignore */ }

            bool dismissed = false;
            int overrideResult = 0;
            try
            {
                overrideResult = 1;
                e.OverrideResult(overrideResult);
                dismissed = true;
            }
            catch { /* ignore */ }

            try
            {
                Issues.dialogs.Add(new DialogRecord
                {
                    dialogId = dialogId,
                    message = message,
                    dialogType = dialogType,
                    title = title,
                    mainInstruction = mainInstruction,
                    expandedContent = expandedContent,
                    footer = footer,
                    capturePath = capItem != null ? capItem.path : string.Empty,
                    captureRisk = capItem != null ? capItem.risk : string.Empty,
                    ocrText = capItem != null ? capItem.ocrText : string.Empty,
                    ocrEngine = capItem != null ? capItem.ocrEngine : string.Empty,
                    ocrStatus = capItem != null ? capItem.ocrStatus : string.Empty,
                    dismissed = dismissed,
                    overrideResult = overrideResult
                });
            }
            catch { /* ignore */ }
        }

        private static string TryGetStringProp(object obj, string name)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(name)) return string.Empty;
                var prop = obj.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string))
                    return (string)(prop.GetValue(obj, null) ?? string.Empty);
            }
            catch { }
            return string.Empty;
        }

        // =========================
        //  Dispose パターン（明示実装）
        // =========================

        // 明示インターフェース実装にして、確実に System.IDisposable.Dispose を満たす
        void System.IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~FailureCaptureScope()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // イベント購読の解除は IDisposable の責務
                try
                {
                    if (_app != null && _failHandler != null)
                        _app.FailuresProcessing -= _failHandler;
                }
                catch { /* ignore */ }

                try
                {
                    if (_uiapp != null && _dlgHandler != null)
                        _uiapp.DialogBoxShowing -= _dlgHandler;
                }
                catch { /* ignore */ }
            }
            _disposed = true;
        }
    }
}

