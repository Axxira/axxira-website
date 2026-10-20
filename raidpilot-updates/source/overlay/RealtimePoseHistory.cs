namespace RaidPilot.Overlay;

/// <summary>
/// Conserve un historique borné de poses afin de retrouver l'estimation qui
/// était active au moment exact d'une capture. Les entrées sont ordonnées par
/// leur horodatage monotone.
/// </summary>
public sealed class RealtimePoseHistory
{
    private readonly Entry[] _entries;
    private int _start;
    private int _count;

    public RealtimePoseHistory(int capacity = 256)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "La capacité doit être d'au moins deux poses.");
        }

        _entries = new Entry[capacity];
    }

    public int Capacity => _entries.Length;

    public int Count => _count;

    public long? OldestTimestamp => _count == 0 ? null : EntryAt(0).Timestamp;

    public long? NewestTimestamp => _count == 0 ? null : EntryAt(_count - 1).Timestamp;

    /// <summary>
    /// Ajoute une pose. Les horodatages doivent être strictement croissants;
    /// lorsque le tampon est plein, la pose la plus ancienne est remplacée.
    /// </summary>
    public void Add(long timestamp, RealtimePose pose)
    {
        if (!AreFinite(pose))
        {
            throw new ArgumentException("La pose doit contenir uniquement des valeurs finies.", nameof(pose));
        }

        if (_count > 0 && timestamp <= EntryAt(_count - 1).Timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                "L'horodatage doit être strictement supérieur au précédent.");
        }

        if (_count < _entries.Length)
        {
            _entries[PhysicalIndex(_count)] = new Entry(timestamp, pose);
            _count++;
            return;
        }

        _entries[_start] = new Entry(timestamp, pose);
        _start = (_start + 1) % _entries.Length;
    }

    /// <summary>
    /// Retourne la pose exacte ou une interpolation entre les deux poses qui
    /// encadrent l'horodatage. Aucun prolongement n'est effectué hors plage.
    /// </summary>
    public bool TryGetPoseAt(long timestamp, out RealtimePose pose)
    {
        pose = default;
        if (_count == 0)
        {
            return false;
        }

        var oldest = EntryAt(0);
        var newest = EntryAt(_count - 1);
        if (timestamp < oldest.Timestamp || timestamp > newest.Timestamp)
        {
            return false;
        }

        if (timestamp == oldest.Timestamp)
        {
            pose = oldest.Pose;
            return true;
        }

        if (timestamp == newest.Timestamp)
        {
            pose = newest.Pose;
            return true;
        }

        var low = 0;
        var high = _count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = EntryAt(middle);
            if (candidate.Timestamp == timestamp)
            {
                pose = candidate.Pose;
                return true;
            }

            if (candidate.Timestamp < timestamp)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        var before = EntryAt(low - 1);
        var after = EntryAt(low);
        var elapsed = NonNegativeDistance(timestamp, before.Timestamp);
        var duration = NonNegativeDistance(after.Timestamp, before.Timestamp);
        var amount = elapsed / (double)duration;

        pose = Interpolate(before.Pose, after.Pose, amount);
        return true;
    }

    /// <summary>
    /// Vide l'historique et permet de recommencer avec une nouvelle origine de
    /// temps, notamment lors d'un changement de raid ou d'un nouvel ancrage.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_entries);
        _start = 0;
        _count = 0;
    }

    private Entry EntryAt(int logicalIndex) => _entries[PhysicalIndex(logicalIndex)];

    private int PhysicalIndex(int logicalIndex) => (_start + logicalIndex) % _entries.Length;

    private static RealtimePose Interpolate(RealtimePose before, RealtimePose after, double amount)
    {
        var yawDelta = NormalizeSigned180(after.YawDegrees - before.YawDegrees);
        return new RealtimePose(
            Lerp(before.X, after.X, amount),
            Lerp(before.Y, after.Y, amount),
            Lerp(before.Z, after.Z, amount),
            Normalize360(before.YawDegrees + (yawDelta * amount)),
            Lerp(before.PositionUncertaintyMeters, after.PositionUncertaintyMeters, amount),
            Lerp(before.YawUncertaintyDegrees, after.YawUncertaintyDegrees, amount));
    }

    private static double Lerp(double before, double after, double amount) =>
        before + ((after - before) * amount);

    private static double NormalizeSigned180(double degrees)
    {
        var normalized = (degrees + 180.0) % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }

        return normalized - 180.0;
    }

    private static double Normalize360(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static ulong NonNegativeDistance(long later, long earlier) =>
        unchecked((ulong)(later - earlier));

    private static bool AreFinite(RealtimePose pose) =>
        double.IsFinite(pose.X)
        && double.IsFinite(pose.Y)
        && double.IsFinite(pose.Z)
        && double.IsFinite(pose.YawDegrees)
        && double.IsFinite(pose.PositionUncertaintyMeters)
        && double.IsFinite(pose.YawUncertaintyDegrees);

    private readonly record struct Entry(long Timestamp, RealtimePose Pose);
}
