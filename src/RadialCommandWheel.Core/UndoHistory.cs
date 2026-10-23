namespace RadialCommandWheel.Core;

public sealed class UndoHistory
{
    private readonly Stack<string> _snapshots = new();
    private readonly int _capacity;

    public UndoHistory(int capacity = 30)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool CanUndo => _snapshots.Count > 0;
    public int Count => _snapshots.Count;

    public void Record(AppConfiguration configuration)
    {
        var snapshot = ConfigurationStore.Serialize(configuration);
        if (_snapshots.TryPeek(out var previous) && previous == snapshot)
        {
            return;
        }

        _snapshots.Push(snapshot);
        while (_snapshots.Count > _capacity)
        {
            var items = _snapshots.Reverse().Skip(1).ToArray();
            _snapshots.Clear();
            foreach (var item in items)
            {
                _snapshots.Push(item);
            }
        }
    }

    public AppConfiguration? Undo()
    {
        if (!_snapshots.TryPop(out var snapshot))
        {
            return null;
        }

        var restored = ConfigurationStore.Deserialize(snapshot);
        ConfigurationValidator.Normalize(restored);
        return restored;
    }

    public void Clear() => _snapshots.Clear();
}

