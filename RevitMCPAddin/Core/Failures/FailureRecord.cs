// ================================================================
// File: Core/Failures/FailureRecord.cs
// ================================================================
using System.Collections.Generic;

namespace RevitMCPAddin.Core.Failures
{
    public class FailureRecord
    {
        public string id { get; set; }          // FailureDefinitionId.ToString()
        public string idGuid { get; set; }      // FailureDefinitionId.Guid
        public string severity { get; set; }    // Warning / Error
        public string message { get; set; }     // GetDescriptionText()
        public int[] elementIds { get; set; }   // 影響要素 ElementId (int)

        // Additive fields (non-breaking): failure handling / suppression diagnostics
        public string action { get; set; }      // e.g. delete_warning / resolve / rollback
        public bool whitelisted { get; set; }   // true when matched an enabled whitelist rule
        public string ruleId { get; set; }      // whitelist rule id (if any)
    }

    public class DialogRecord
    {
        public string dialogId { get; set; }    // TaskDialogShowingEventArgs.DialogId
        public string message { get; set; }     // TaskDialogShowingEventArgs.Message

        // Additive fields (non-breaking): dialog suppression diagnostics
        public string dialogType { get; set; }  // TaskDialog / DialogBox / etc
        public string title { get; set; }
        public string mainInstruction { get; set; }
        public string expandedContent { get; set; }
        public string footer { get; set; }
        public string capturePath { get; set; }
        public string captureRisk { get; set; }
        public string ocrText { get; set; }
        public string ocrEngine { get; set; }
        public string ocrStatus { get; set; }
        public bool dismissed { get; set; }
        public int overrideResult { get; set; }
    }

    public class CommandIssues
    {
        public List<FailureRecord> failures { get; private set; }
        public List<DialogRecord> dialogs { get; private set; }

        // Additive fields (non-breaking): rollback/tx diagnostics
        public bool rollbackRequested { get; set; }
        public string rollbackReason { get; set; }

        public CommandIssues()
        {
            failures = new List<FailureRecord>();
            dialogs = new List<DialogRecord>();
            rollbackRequested = false;
            rollbackReason = string.Empty;
        }
    }
}
