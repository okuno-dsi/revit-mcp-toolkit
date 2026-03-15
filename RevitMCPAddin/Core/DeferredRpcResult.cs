#nullable enable

namespace RevitMCPAddin.Core
{
    internal sealed class DeferredRpcResult
    {
        public static DeferredRpcResult Instance { get; } = new DeferredRpcResult();

        private DeferredRpcResult()
        {
        }
    }
}
