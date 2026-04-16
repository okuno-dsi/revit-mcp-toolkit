using System.Collections.Concurrent;

namespace RevitMCP.A2AAdapter;

public sealed class TaskStore
{
    private readonly ConcurrentDictionary<string, A2ATaskRecord> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public A2ATaskRecord Add(A2ATaskRecord task)
    {
        _tasks[task.Id] = task;
        return task;
    }

    public bool TryGet(string id, out A2ATaskRecord task)
    {
        return _tasks.TryGetValue(id, out task!);
    }

    public IReadOnlyList<A2ATaskRecord> List(string? contextId, string? state, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        IEnumerable<A2ATaskRecord> query = _tasks.Values;
        if (!string.IsNullOrWhiteSpace(contextId))
            query = query.Where(x => string.Equals(x.ContextId, contextId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(x => string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase));

        return query
            .OrderByDescending(x => x.UpdatedUtc, StringComparer.OrdinalIgnoreCase)
            .Take(pageSize)
            .ToList();
    }
}
