using UnityEngine;

/// <summary>
/// Shared reward-shaping utilities for drone ML-Agents.
/// All methods are pure functions — they compute a reward value or check a terminal
/// condition without calling AddReward / SetReward / EndEpisode, so the agent
/// retains full control over how rewards are applied.
/// </summary>
public static class DroneRewardMath
{
    /// <summary>Penalty applied when the drone collides with an obstacle or the ground.</summary>
    public const float ObstaclePenalty = -5.0f;

    /// <summary>Result of a terminal-condition check.</summary>
    public struct TerminalCheck
    {
        public bool IsTerminal;
        /// <summary>The reward to apply. Valid only when <see cref="IsTerminal"/> is <c>true</c>.</summary>
        public float TerminalReward;
    }

    // ───────────────────────── Terminal Conditions ─────────────────────────

    /// <summary>Returns terminal (−penalty) when the drone is too far from the target.</summary>
    public static TerminalCheck CheckTooFar(float distanceToTarget, float maxDistance, float penalty = -1.0f)
    {
        return new TerminalCheck
        {
            IsTerminal = distanceToTarget > maxDistance,
            TerminalReward = penalty
        };
    }

    /// <summary>Returns terminal (−penalty) when the drone's up vector tilts too far from world up.</summary>
    public static TerminalCheck CheckExcessiveTilt(Vector3 droneUp, float maxTiltDot, float penalty = -1.0f)
    {
        float tiltDot = Vector3.Dot(droneUp, Vector3.up);
        return new TerminalCheck
        {
            IsTerminal = tiltDot < maxTiltDot,
            TerminalReward = penalty
        };
    }

    // ───────────────────────── Continuous Shaping ──────────────────────────

    /// <summary>
    /// Reward that increases as the drone covers more of the distance to the target.
    /// Returns <c>scale * fractionMet</c> where <c>fractionMet = 1 − clamp(distance / maxDistance)</c>.
    /// At the target the fraction is 1; at or beyond <paramref name="maxDistance"/> it is 0.
    /// </summary>
    /// <param name="dronePosition">Current world/local position of the drone.</param>
    /// <param name="targetPosition">Position of the target.</param>
    /// <param name="startPosition">Position the drone started from (used as the reference distance).</param>
    /// <param name="scale">Multiplier applied to the fraction (default 0.01).</param>
    public static float ProximityReward(Vector3 dronePosition, Vector3 targetPosition, Vector3 startPosition, float scale = 0.01f)
    {
        float maxDistance = Vector3.Distance(startPosition, targetPosition);
        if (maxDistance < 0.001f)
            return scale;

        float distanceToTarget = Vector3.Distance(dronePosition, targetPosition);
        float fractionMet = 1f - Mathf.Clamp01(distanceToTarget / maxDistance);
        return scale * fractionMet;
    }

    /// <summary>
    /// Potential-based progress reward: positive when the drone moves closer to the target,
    /// negative when it moves away, and exactly zero when hovering in place.
    /// Eliminates the position-farming exploit of <see cref="ProximityReward"/>.
    /// <c>R = scale × (previousDistance − currentDistance)</c>
    /// </summary>
    /// <param name="previousDistance">Distance to target at the previous step.</param>
    /// <param name="currentDistance">Distance to target at the current step.</param>
    /// <param name="scale">Reward per metre of progress (default 0.1).</param>
    public static float DeltaDistanceReward(float previousDistance, float currentDistance, float scale = 0.1f)
    {
        return scale * (previousDistance - currentDistance);
    }

    /// <summary>
    /// Potential-based progress reward normalised by the episode's starting distance.
    /// Because the delta is divided by <paramref name="startDistance"/>, moving 1 % of the
    /// original distance always yields the same reward regardless of how far the drone
    /// started from the target (e.g. 50 m or 5 m).
    /// <c>R = maxProgressReward × (previousDistance − currentDistance) / startDistance</c>
    /// </summary>
    /// <param name="previousDistance">Distance to target at the previous step.</param>
    /// <param name="currentDistance">Distance to target at the current step.</param>
    /// <param name="startDistance">Distance to target at episode start (used as normaliser).</param>
    /// <param name="maxProgressReward">Reward earned when the drone covers the full start distance in one step (default 0.1).</param>
    public static float NormalizedDeltaDistanceReward(float previousDistance, float currentDistance, float startDistance, float maxProgressReward = 0.1f)
    {
        if (startDistance < 0.001f)
            return 0f;

        return maxProgressReward * (previousDistance - currentDistance) / startDistance;
    }

    /// <summary>Penalty proportional to how far the drone's up axis deviates from world up.</summary>
    public static float TiltPenalty(Vector3 droneUp, float scale = 0.005f)
    {
        float deviation = 1f - Vector3.Dot(droneUp, Vector3.up);
        return -scale * deviation;
    }

    /// <summary>Penalty proportional to the drone's angular velocity magnitude.</summary>
    public static float AngularVelocityPenalty(float angularVelocityMagnitude, float scale = 0.001f)
    {
        return -scale * angularVelocityMagnitude;
    }

    /// <summary>
    /// Penalty proportional to the mean absolute change in motor commands between steps.
    /// Encourages smooth, jitter-free control signals.
    /// <c>penalty = −scale × mean(|current[i] − previous[i]|)</c>
    /// </summary>
    /// <param name="currentActions">Motor commands issued this step.</param>
    /// <param name="previousActions">Motor commands issued the previous step.</param>
    /// <param name="scale">Multiplier applied to the mean delta (default 0.01).</param>
    public static float ActionSmoothnessPenalty(float[] currentActions, float[] previousActions, float scale = 0.001f)
    {
        if (currentActions == null || previousActions == null || currentActions.Length == 0)
            return 0f;

        float totalDelta = 0f;
        int count = Mathf.Min(currentActions.Length, previousActions.Length);
        for (int i = 0; i < count; i++)
            totalDelta += Mathf.Abs(currentActions[i] - previousActions[i]);

        return -scale * (totalDelta / count);
    }

    /// <summary>
    /// Reward proportional to how well the drone's velocity aligns with the direction to target.
    /// Both vectors <b>must</b> be expressed in the same coordinate frame (both world or both local).
    /// When <paramref name="toTarget"/> is a local-space vector (e.g. <c>targetLocalPos - transform.localPosition</c>),
    /// convert the velocity to local space first: <c>transform.InverseTransformDirection(rb.linearVelocity)</c>.
    /// </summary>
    public static float VelocityAlignmentReward(Vector3 velocity, Vector3 toTarget, float scale = 0.01f)
    {
        float distance = toTarget.magnitude;
        if (distance < 0.001f)
            return 0f;

        Vector3 direction = toTarget / distance;
        float alignment = Vector3.Dot(velocity, direction);
        return scale * alignment;
    }

    /// <summary>Small constant penalty per step to encourage faster task completion.</summary>
    public static float TimePenalty(float scale = 0.001f)
    {
        return -scale;
    }

    /// <summary>
    /// Penalty proportional to the mean squared normalised thrust across all motors.
    /// Discourages the network from saturating motors at full throttle when it isn't needed.
    /// Actions are assumed to be in [−1, 1]; thrust is mapped to [0, 1] before squaring.
    /// <c>penalty = −scale × mean((action[i] + 1) / 2)²</c>
    /// </summary>
    /// <param name="actions">Raw continuous actions in [−1, 1].</param>
    /// <param name="scale">Multiplier applied to the mean energy (default 0.001).</param>
    public static float EnergyPenalty(float[] actions, float scale = 0.001f)
    {
        if (actions == null || actions.Length == 0)
            return 0f;

        float total = 0f;
        for (int i = 0; i < actions.Length; i++)
        {
            float thrust = (actions[i] + 1f) * 0.5f;
            total += thrust * thrust;
        }
        return -scale * (total / actions.Length);
    }

    /// <summary>
    /// Penalty that discourages fast movement inside a landing radius (any direction, not just approach).
    /// Zero outside <paramref name="landingRadius"/>; scales linearly with both
    /// proximity and speed: <c>penalty = −scale × speed × (1 − distance / radius)</c>.
    /// </summary>
    /// <param name="speed">Drone speed in m/s.</param>
    /// <param name="distanceToTarget">Current distance to the target.</param>
    /// <param name="landingRadius">Radius within which the penalty is active.</param>
    /// <param name="scale">Multiplier applied to the penalty (default 0.002).</param>
    public static float FastApproachPenalty(float speed, float distanceToTarget, float landingRadius, float scale = 0.002f)
    {
        if (landingRadius < 0.001f) return 0f;
        float proximity = 1f - Mathf.Clamp01(distanceToTarget / landingRadius);
        return -scale * speed * proximity;
    }

    // ────────────────────────── Utility ────────────────────────────────────

    /// <summary>Resolves the effective target position, falling back to a default height.</summary>
    public static Vector3 ResolveTargetPosition(Transform target, Vector3 fallbackOrigin, float fallbackHeight = 3f)
    {
        return target != null ? target.localPosition : fallbackOrigin + Vector3.up * fallbackHeight;
    }

    /// <summary>
    /// Penalty applied every step after the drone has first touched the landing pad.
    /// Scales with total motion (linear + angular) to encourage the agent to settle quickly.
    /// <c>penalty = -scale × (linearVelocityMagnitude + angularVelocityMagnitude)</c>
    /// </summary>
    /// <param name="linearVelocityMagnitude">Magnitude of the drone's linear velocity.</param>
    /// <param name="angularVelocityMagnitude">Magnitude of the drone's angular velocity.</param>
    /// <param name="scale">Multiplier applied to the combined velocity magnitude (default 0.05).</param>
    public static float RestlessnessPenalty(float linearVelocityMagnitude, float angularVelocityMagnitude, float scale = 0.05f)
    {
        return -scale * (linearVelocityMagnitude + angularVelocityMagnitude);
    }
}
