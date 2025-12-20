using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    internal class SuppressWarningsPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            try
            {
                var fms = failuresAccessor.GetFailureMessages();
                foreach (var fm in fms)
                {
                    if (fm.GetSeverity() == FailureSeverity.Warning)
                    {
                        try { failuresAccessor.DeleteWarning(fm); } catch { }
                    }
                }
            }
            catch { }
            return FailureProcessingResult.Continue;
        }
    }
}