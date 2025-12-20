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
                    ? f.GetFailingElementIds().Select(x => x.IntegerValue).ToArray()
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
            // TaskDialogのみログ化（必要なら OverrideResult で自動Dismiss可）
            TaskDialogShowingEventArgs t = e as TaskDialogShowingEventArgs;
            if (t != null)
            {
                Issues.dialogs.Add(new DialogRecord
                {
                    dialogId = t.DialogId,
                    message = t.Message
                });

                // 自動Dismiss例（必要時のみ有効化）
                // if (t.DialogId == "Dialog_Revit_IdenticalInstances")
                //     t.OverrideResult(1); // OK ボタンコード
            }
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
