namespace RaidPilot.Overlay;

/// <summary>
/// Historique RAM borné des transitions des seules touches de contrôle EFT.
/// Les horodatages Stopwatch permettent de mesurer un tap complet entre deux
/// ticks sans journaliser le texte saisi ni écrire quoi que ce soit sur disque.
/// </summary>
internal sealed class RawKeyboardTimeline
{
    private const int DefaultCapacity = 512;

    private readonly KeyTransition[] _events;
    private readonly Dictionary<Keys, bool> _states = new();
    private int _start;
    private int _count;
    private long _lastTimestamp;
    private long _discardedThroughTimestamp = long.MinValue;

    internal RawKeyboardTimeline(int capacity = DefaultCapacity)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _events = new KeyTransition[capacity];
    }

    internal int Count => _count;

    internal int Capacity => _events.Length;

    internal void Reset()
    {
        Array.Clear(_events);
        _states.Clear();
        _start = 0;
        _count = 0;
        _lastTimestamp = 0;
        _discardedThroughTimestamp = long.MinValue;
    }

    /// <summary>
    /// Définit l'état physique connu au début d'une nouvelle période de
    /// premier plan. Aucune arête n'est créée, donc un appui fait dans une
    /// autre application ne peut pas devenir une action Tarkov.
    /// </summary>
    internal void Synchronize(Keys key, bool isDown)
    {
        key &= Keys.KeyCode;
        if (key != Keys.None)
        {
            _states[key] = isDown;
        }
    }

    internal void Record(Keys key, bool isDown, long timestamp)
    {
        key &= Keys.KeyCode;
        if (key == Keys.None)
        {
            return;
        }
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        // Stopwatch est monotone, mais deux messages peuvent partager un tick.
        // Borner un horodatage régressif garde l'ordre déterministe sans jeter
        // une transition utile.
        timestamp = Math.Max(timestamp, _lastTimestamp);
        _lastTimestamp = timestamp;

        if (_states.TryGetValue(key, out var current) && current == isDown)
        {
            // Les répétitions automatiques ne sont pas de nouveaux appuis.
            return;
        }

        _states[key] = isDown;
        if (_count == _events.Length)
        {
            var evicted = _events[_start];
            _discardedThroughTimestamp = Math.Max(
                _discardedThroughTimestamp,
                evicted.Timestamp);
            _start = (_start + 1) % _events.Length;
            _count--;
        }

        var index = (_start + _count) % _events.Length;
        _events[index] = new KeyTransition(key, isDown, timestamp);
        _count++;
    }

    internal bool TrySample(
        Keys key,
        long startTimestamp,
        long endTimestamp,
        out RawKeyboardIntervalSample sample)
    {
        sample = default;
        key &= Keys.KeyCode;
        if (key == Keys.None
            || startTimestamp < 0
            || endTimestamp < startTimestamp
            || startTimestamp < _discardedThroughTimestamp
            || !_states.TryGetValue(key, out var currentState))
        {
            return false;
        }

        // Revenir de l'état courant à l'état exactement au début de
        // l'intervalle. Une transition est forcément l'inverse de son état
        // précédent puisque Record élimine les répétitions.
        var stateAtStart = currentState;
        for (var logicalIndex = _count - 1; logicalIndex >= 0; logicalIndex--)
        {
            var transition = GetAt(logicalIndex);
            if (transition.Timestamp <= startTimestamp)
            {
                break;
            }
            if (transition.Key == key)
            {
                stateAtStart = !transition.IsDown;
            }
        }

        var isDown = stateAtStart;
        var cursor = startTimestamp;
        var heldTicks = 0L;
        var wasPressed = false;
        var wasReleased = false;
        for (var logicalIndex = 0; logicalIndex < _count; logicalIndex++)
        {
            var transition = GetAt(logicalIndex);
            if (transition.Timestamp <= startTimestamp || transition.Key != key)
            {
                continue;
            }
            if (transition.Timestamp > endTimestamp)
            {
                break;
            }

            if (isDown)
            {
                heldTicks += transition.Timestamp - cursor;
            }
            cursor = transition.Timestamp;
            isDown = transition.IsDown;
            wasPressed |= transition.IsDown;
            wasReleased |= !transition.IsDown;
        }

        if (isDown)
        {
            heldTicks += endTimestamp - cursor;
        }

        sample = new RawKeyboardIntervalSample(
            IsDown: isDown,
            WasPressed: wasPressed,
            WasReleased: wasReleased,
            HeldTicks: heldTicks,
            DurationTicks: endTimestamp - startTimestamp);
        return true;
    }

    internal bool CopyTransitionTimes(long start, long end, ReadOnlySpan<Keys> keys, List<long> target)
    {
        if (start < 0 || end < start || start < _discardedThroughTimestamp) return false;
        foreach (var key in keys) if (!_states.ContainsKey(key & Keys.KeyCode)) return false;
        for (int i = 0; i < _count; i++)
        {
            var transition = GetAt(i);
            if (transition.Timestamp > end) break;
            if (transition.Timestamp <= start) continue;
            foreach (var key in keys)
                if (transition.Key == (key & Keys.KeyCode)) { target.Add(transition.Timestamp); break; }
        }
        return true;
    }

    private KeyTransition GetAt(int logicalIndex)
    {
        return _events[(_start + logicalIndex) % _events.Length];
    }

    private readonly record struct KeyTransition(Keys Key, bool IsDown, long Timestamp);
}

internal readonly record struct RawKeyboardIntervalSample(
    bool IsDown,
    bool WasPressed,
    bool WasReleased,
    long HeldTicks,
    long DurationTicks)
{
    internal double HeldFraction => DurationTicks <= 0
        ? IsDown ? 1.0 : 0.0
        : Math.Clamp(HeldTicks / (double)DurationTicks, 0.0, 1.0);
}
