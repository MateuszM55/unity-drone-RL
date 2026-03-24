using UnityEngine;

/// <summary>
/// Shared reward-shaping utilities for drone ML-Agents.
/// All methods are pure functions — they compute a reward value or check a terminal
/// condition without calling AddReward / SetReward / EndEpisode, so the agent
/// retains full control over how rewards are applied.
/// </summary>
public static class DroneRewardHelper
{
    /// <summary>Result of a terminal-condition check.</summary>
    public struct TerminalCheck
    {
        public bool IsTerminal;
        public float Reward;
    }

    // ───────────────────────── Terminal Conditions ─────────────────────────

    /// <summary>Returns terminal (+reward) when the drone is close enough to the target.</summary>
    public static TerminalCheck CheckTargetReached(float distanceToTarget, float threshold, float reward = 1.0f)
    {
        return new TerminalCheck
        {
            IsTerminal = distanceToTarget < threshold,
            Reward = reward
        };
    }

    /// <summary>Returns terminal (−penalty) when the drone is too far from the target.</summary>
    public static TerminalCheck CheckTooFar(float distanceToTarget, float maxDistance, float penalty = -1.0f)
    {
        return new TerminalCheck
        {
            IsTerminal = distanceToTarget > maxDistance,
            Reward = penalty
        };
    }

    /// <summary>Returns terminal (−penalty) when the drone falls below a Y threshold.</summary>
    public static TerminalCheck CheckFallen(float yPosition, float minY = -0.5f, float penalty = -1.0f)
    {
        return new TerminalCheck
        {
            IsTerminal = yPosition < minY,
            Reward = penalty
        };
    }

    /// <summary>Returns terminal (−penalty) when the drone's up vector tilts too far from world up.</summary>
    public static TerminalCheck CheckExcessiveTilt(Vector3 droneUp, float maxTiltDot, float penalty = -1.0f)
    {
        float tiltDot = Vector3.Dot(droneUp, Vector3.up);
        return new TerminalCheck
        {
            IsTerminal = tiltDot < maxTiltDot,
            Reward = penalty
        };
    }

    // ───────────────────────── Continuous Shaping ──────────────────────────

    /// <summary>Reward that increases as the drone gets closer to the target (0 at maxDistance, scale at 0).</summary>
    public static float ProximityReward(float distanceToTarget, float maxDistance, float scale = 0.01f)
    {
        float proximity = 1f - Mathf.Clamp01(distanceToTarget / maxDistance);
        return scale * proximity;
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

    /// <summary>Reward proportional to how well the drone's velocity aligns with the direction to target.</summary>
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

    // ────────────────────────── Utility ────────────────────────────────────

    /// <summary>Resolves the effective target position, falling back to a default hover point.</summary>
    public static Vector3 ResolveTargetPosition(Transform target, Vector3 fallbackOrigin, float fallbackHeight = 3f)
    {
        return target != null ? target.localPosition : fallbackOrigin + Vector3.up * fallbackHeight;
    }
}
