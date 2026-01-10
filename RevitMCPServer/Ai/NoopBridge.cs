#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RevitMcpServer.Ai
{
    public interface IAgentBridge
    {
        Task<string> SummarizeAsync(string prompt, CancellationToken ct = default);
        IAsyncEnumerable<string> SummarizeStreamAsync(string prompt, CancellationToken ct = default);
    }

    public sealed class NoopBridge : IAgentBridge
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<string> SummarizeStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
