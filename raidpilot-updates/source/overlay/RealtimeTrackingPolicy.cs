namespace RaidPilot.Overlay;

/// <summary>
/// Conservative rules shared by the Beacon player tracker and its native tests.
/// These estimate movement from player inputs; they do not read Tarkov positions.
/// </summary>
internal readonly record struct RealtimeTrackingModes(
    bool Sprint,
    bool Slow,
    bool Aiming,
    bool SprintLatched,
    bool WalkLatched,
    bool AimLatched);

internal readonly record struct RealtimeTrackingPosture(bool Crouched, bool Prone);

internal static class RealtimeTrackingPolicy
{
    internal const double MaximumIntegratedTickSeconds = 0.75;
    internal const double MaximumReliableGapSeconds = 2.0;

    internal static RealtimeTrackingModes ResolveModes(
        double forward,
        bool requestedSprint,
        bool requestedSlow,
        bool requestedAim,
        bool sprintLatched,
        bool walkLatched,
        bool aimLatched)
    {
        if (!double.IsFinite(forward) || Math.Abs(forward) > 1.0)
            throw new ArgumentOutOfRangeException(nameof(forward));

        // Tarkov stops sprint when W is released. A stale toggle must not
        // silently resume sprint velocity when W is pressed again.
        var sprint = requestedSprint && forward > 0.0;
        if (forward <= 0.0) sprintLatched = false;
        if (sprint)
        {
            // Sprint automatically cancels slow-walk and ADS in-game.
            return new RealtimeTrackingModes(
                Sprint: true, Slow: false, Aiming: false,
                SprintLatched: sprintLatched, WalkLatched: false, AimLatched: false);
        }
        return new RealtimeTrackingModes(false, requestedSlow, requestedAim,
            sprintLatched, walkLatched, aimLatched);
    }

    internal static RealtimeTrackingPosture UpdatePosture(
        RealtimeTrackingPosture state,
        bool duckReleased,
        bool duckWheelChord,
        bool pronePressed,
        bool jumpPressed,
        bool sprinting)
    {
        if (sprinting || jumpPressed) return default;
        if (pronePressed) return new RealtimeTrackingPosture(false, !state.Prone);
        if (duckReleased && !duckWheelChord)
            return state.Prone
                ? new RealtimeTrackingPosture(true, false)
                : new RealtimeTrackingPosture(!state.Crouched, false);
        return state;
    }

    internal static double PostureSpeedScale(RealtimeTrackingPosture state) =>
        state.Prone ? 0.20 : state.Crouched ? 0.55 : 1.0;

    internal static RealtimeMotionEvidence FilterAmbiguousStationaryEvidence(
        RealtimeMotionEvidence evidence, bool commandedMovement, bool sprint) =>
        commandedMovement && !sprint && evidence == RealtimeMotionEvidence.Stationary
            ? RealtimeMotionEvidence.Unknown
            : evidence;

    internal static double IntegratedSeconds(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        return Math.Min(elapsed, MaximumIntegratedTickSeconds);
    }

    internal static double UnobservedSeconds(double elapsed) =>
        elapsed - IntegratedSeconds(elapsed);

    internal static bool HasUnreliableGap(double elapsed) =>
        elapsed > MaximumReliableGapSeconds;
}
