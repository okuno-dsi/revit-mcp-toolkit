using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    internal class ProceedWithWarningsFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            try
            {
                var msgs = a.GetFailureMessages();
                foreach (var m in msgs)
                {
                    var sev = m.GetSeverity();
                    if (sev == FailureSeverity.Warning)
                    {
                        // Delete warnings to avoid blocking commits
                        a.DeleteWarning(m);
                    }
                }
            }
            catch { }
            return FailureProcessingResult.Continue;
        }
    }
}

