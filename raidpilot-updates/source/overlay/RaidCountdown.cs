using System.Diagnostics;

namespace RaidPilot.Overlay;

internal readonly record struct RaidCountdownSnapshot(
    int Generation,
    bool IsActive,
    bool IsSynchronized,
    double RemainingSeconds);

/// <summary>
/// Horloge de raid en mémoire. Une valeur OCR appartient à l'instant de la
/// capture; le délai de décodage est donc retiré avant de poser l'ancre live.
/// </summary>
internal sealed class RaidCountdown
{
    private const double DirectSynchronizationToleranceSeconds = 15.0;
    private const double ConfirmationToleranceSeconds = 5.0;
    private readonly long _frequency;
    private int _generation;
    private bool _active;
    private double? _remainingAtAnchorSeconds;
    private long _anchorTimestamp;
    private long _lastCaptureTimestamp;
    private int? _pendingDiscontinuousRemainingSeconds;
    private long _pendingDiscontinuousCaptureTimestamp;

    internal RaidCountdown(long frequency = 0)
    {
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
    }

    internal int StartSession()
    {
        _generation = unchecked(_generation + 1);
        _active = true;
        _remainingAtAnchorSeconds = null;
        _anchorTimestamp = 0;
        _lastCaptureTimestamp = 0;
        _pendingDiscontinuousRemainingSeconds = null;
        _pendingDiscontinuousCaptureTimestamp = 0;
        return _generation;
    }

    internal bool TrySynchronize(
        int generation,
        int capturedRemainingSeconds,
        long captureTimestamp,
        long currentTimestamp)
    {
        if (!_active
            || generation != _generation
            || capturedRemainingSeconds < 0
            || capturedRemainingSeconds > RaidTimerParser.MaximumRemainingSeconds
            || captureTimestamp <= 0
            || currentTimestamp <= 0
            || captureTimestamp > currentTimestamp + _frequency
            || (_lastCaptureTimestamp != 0 && captureTimestamp <= _lastCaptureTimestamp))
        {
            return false;
        }

        if (_remainingAtAnchorSeconds is not null)
        {
            var predictedAtCapture = Math.Max(
                0.0,
                _remainingAtAnchorSeconds.Value
                    - ((captureTimestamp - _anchorTimestamp) / (double)_frequency));
            var difference = Math.Abs(capturedRemainingSeconds - predictedAtCapture);
            if (difference > DirectSynchronizationToleranceSeconds)
            {
                var confirmsPending = _pendingDiscontinuousRemainingSeconds is not null
                    && captureTimestamp > _pendingDiscontinuousCaptureTimestamp
                    && Math.Abs(
                        capturedRemainingSeconds
                            - Math.Max(
                                0.0,
                                _pendingDiscontinuousRemainingSeconds.Value
                                    - ((captureTimestamp - _pendingDiscontinuousCaptureTimestamp)
                                        / (double)_frequency)))
                        <= ConfirmationToleranceSeconds;
                if (!confirmsPending)
                {
                    _pendingDiscontinuousRemainingSeconds = capturedRemainingSeconds;
                    _pendingDiscontinuousCaptureTimestamp = captureTimestamp;
                    return false;
                }
            }
        }

        var elapsedSeconds = Math.Max(
            0.0,
            (currentTimestamp - captureTimestamp) / (double)_frequency);
        _remainingAtAnchorSeconds = Math.Max(
            0.0,
            capturedRemainingSeconds - elapsedSeconds);
        _anchorTimestamp = currentTimestamp;
        _lastCaptureTimestamp = captureTimestamp;
        _pendingDiscontinuousRemainingSeconds = null;
        _pendingDiscontinuousCaptureTimestamp = 0;
        return true;
    }

    internal RaidCountdownSnapshot GetSnapshot(long currentTimestamp)
    {
        if (!_active || _remainingAtAnchorSeconds is null)
        {
            return new RaidCountdownSnapshot(
                _generation,
                _active,
                IsSynchronized: false,
                RemainingSeconds: 0.0);
        }

        var elapsedSeconds = currentTimestamp > _anchorTimestamp
            ? (currentTimestamp - _anchorTimestamp) / (double)_frequency
            : 0.0;
        return new RaidCountdownSnapshot(
            _generation,
            IsActive: true,
            IsSynchronized: true,
            RemainingSeconds: Math.Max(0.0, _remainingAtAnchorSeconds.Value - elapsedSeconds));
    }
}
