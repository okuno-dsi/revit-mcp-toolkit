using Autodesk.Revit.DB;
using RevitMCPAddin.Core.Failures;

namespace RevitMCPAddin.Core
{
    public static class TxnUtil
    {
        public static void ConfigureProceedWithWarnings(Transaction tx)
        {
            if (tx == null) return;
            try
            {
                var opts = tx.GetFailureHandlingOptions();

                // When the router has enabled failureHandling for this command, switch to the whitelist-aware preprocessor.
                if (FailureHandlingContext.Enabled)
                    opts = opts.SetFailuresPreprocessor(new FailureHandlingFailuresPreprocessor());
                else
                    opts = opts.SetFailuresPreprocessor(new ProceedWithWarningsFailuresPreprocessor());

                opts = opts.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(opts);
            }
            catch { }
        }
    }
}
