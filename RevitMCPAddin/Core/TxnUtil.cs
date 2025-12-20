using Autodesk.Revit.DB;

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
                opts = opts.SetFailuresPreprocessor(new ProceedWithWarningsFailuresPreprocessor());
                opts = opts.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(opts);
            }
            catch { }
        }
    }
}

