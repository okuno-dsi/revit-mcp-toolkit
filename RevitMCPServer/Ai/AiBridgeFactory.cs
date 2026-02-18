#nullable enable
using RevitMcpServer.Config;

namespace RevitMcpServer.Ai
{
    public sealed class AiBridgeFactory
    {
        private readonly Settings _settings;

        public AiBridgeFactory(Settings settings) => _settings = settings;

        public IAgentBridge Create()
        {
            switch (_settings.Ai.Provider)
            {
                // : case AiProvider.OpenAi: return new OpenAiBridge(...);
                // : case AiProvider.Gemini: return new GeminiBridge(...);
                // : case AiProvider.Cli:    return new LocalCliBridge(...);
                default: return new NoopBridge();
            }
        }
    }
}
