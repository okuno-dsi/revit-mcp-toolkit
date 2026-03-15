using System.Collections.Concurrent;

internal sealed class ExcelMcpSessionStore
{
    private readonly ConcurrentDictionary<string, ExcelMcpSessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public ExcelMcpSessionState Create(string protocolVersion)
    {
        var session = new ExcelMcpSessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersion)
                ? ExcelMcpProtocol.DefaultProtocolVersion
                : protocolVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            IsInitialized = false,
        };
        _sessions[session.SessionId] = session;
        return session;
    }

    public bool TryGet(string? sessionId, out ExcelMcpSessionState state)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryGetValue(sessionId, out state!))
        {
            state.LastSeenAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        state = null!;
        return false;
    }

    public bool MarkInitialized(string sessionId)
    {
        if (!TryGet(sessionId, out var state))
            return false;

        state.IsInitialized = true;
        state.LastSeenAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    public bool Delete(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;
        return _sessions.TryRemove(sessionId, out _);
    }
}

internal sealed class ExcelMcpSessionState
{
    public string SessionId { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = ExcelMcpProtocol.DefaultProtocolVersion;
    public bool IsInitialized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
