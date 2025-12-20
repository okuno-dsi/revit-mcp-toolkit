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
    }

    public class DialogRecord
    {
        public string dialogId { get; set; }    // TaskDialogShowingEventArgs.DialogId
        public string message { get; set; }     // TaskDialogShowingEventArgs.Message
    }

    public class CommandIssues
    {
        public List<FailureRecord> failures { get; private set; }
        public List<DialogRecord> dialogs { get; private set; }

        public CommandIssues()
        {
            failures = new List<FailureRecord>();
            dialogs = new List<DialogRecord>();
        }
    }
}
