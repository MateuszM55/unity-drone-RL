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

    /// <summary>Small constant penalty per step to encourage faster task completion.</summary>
    public static float TimePenalty(float scale = 0.001f)
    {
        return -scale;
    }

    // ────────────────────────── Utility ────────────────────────────────────

    /// <summary>
    /// Penalty that scales linearly from 0 when the drone's yaw is within
    /// <paramref name="maxAllowedAngle"/> of the direction to the target, to
    /// <paramref name="scale"/> when the drone is looking directly away (180°).
    /// Only the horizontal (XZ) plane is considered.
    /// </summary>
    /// <param name="droneForward">The drone's forward vector (world space).</param>
    /// <param name="toTarget">Vector from the drone to the target (world space).</param>
    /// <param name="maxAllowedAngle">Angle in degrees within which no penalty is applied.</param>
    /// <param name="scale">Maximum penalty magnitude (applied at 180° deviation).</param>
    public static float YawDeviationPenalty(Vector3 droneForward, Vector3 toTarget, float maxAllowedAngle, float scale = 0.005f)
    {
        // Project both vectors onto the XZ plane
        Vector3 fwd = new Vector3(droneForward.x, 0f, droneForward.z);
        Vector3 dir = new Vector3(toTarget.x, 0f, toTarget.z);

        if (fwd.sqrMagnitude < 0.0001f || dir.sqrMagnitude < 0.0001f)
            return 0f;

        float angle = Vector3.Angle(fwd, dir); // 0–180°
        float excessAngle = angle - maxAllowedAngle;
        if (excessAngle <= 0f)
            return 0f;

        // Normalize excess to [0,1]: 0 at maxAllowedAngle, 1 at 180°
        float range = 180f - maxAllowedAngle;
        if (range < 0.001f) return -scale;
        float t = excessAngle / range;
        return -scale * t;
    }

    /// <summary>Resolves the effective target position, falling back to a default height.</summary>
    public static Vector3 ResolveTargetPosition(Transform target, Vector3 fallbackOrigin, float fallbackHeight = 3f)
    {
        return target != null ? target.localPosition : fallbackOrigin + Vector3.up * fallbackHeight;
    }

    /// <summary>
    /// Per-step penalty proportional to total motion (linear + angular velocity).
    /// Uses the soft-sign (algebraic sigmoid) to bound the penalty as speeds grow large:
    /// <c>penalty = -scale × (x / (1 + x))</c> where <c>x = linearVelocityMagnitude + angularVelocityMagnitude</c>.
    /// The soft-sign stays sensitive over a wider speed range than tanh and is cheaper to compute.
    /// </summary>
    /// <param name="linearVelocityMagnitude">Magnitude of the drone's linear velocity.</param>
    /// <param name="angularVelocityMagnitude">Magnitude of the drone's angular velocity.</param>
    /// <param name="scale">Multiplier applied to the soft-sign output (default 0.05).</param>
    public static float RestlessnessPenalty(float linearVelocityMagnitude, float angularVelocityMagnitude, float scale = 0.05f)
    {
        float x = linearVelocityMagnitude + angularVelocityMagnitude;
        return -scale * (x / (1f + x));
    }
}
