using UnityEngine;

/// <summary>
/// Evaluates terminal conditions and computes per-step reward components
/// using a <see cref="DroneRewardProfile"/>.
///
/// Stateful: tracks the previous distance to target for delta-distance rewards.
/// Call <see cref="ResetEpisode"/> at the start of every episode.
///
/// This component never calls <c>AddReward</c> / <c>SetReward</c> /
/// <c>EndEpisode</c> — the agent retains full control over how results
/// are applied.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneRewardManager : MonoBehaviour
{
    private Rigidbody rb;
    private float _previousDistance = -1f;
    private float _startDistance = -1f;
    private float _maxTiltDot = 0.5f; // cos(60°) — overwritten by Configure()

    /// <summary>
    /// Pre-computes and caches the maximum-tilt dot-product threshold from the reward profile.
    /// Call once from the agent's <c>Initialize</c> after the profile is available.
    /// </summary>
    /// <param name="profile">Reward profile that owns <c>maxTiltAngle</c>.</param>
    public void Configure(DroneRewardProfile profile)
    {
        if (profile != null)
            _maxTiltDot = Mathf.Cos(profile.maxTiltAngle * Mathf.Deg2Rad);
    }

    /// <summary>Result returned by <see cref="Evaluate"/>.</summary>
    public struct EvalResult
    {
        /// <summary><c>true</c> when a terminal condition was triggered.</summary>
        public bool IsTerminal;
        /// <summary>Reward to apply via <c>SetReward</c> when <see cref="IsTerminal"/> is true.</summary>
        public float TerminalReward;
        /// <summary>Why the episode ended (valid only when <see cref="IsTerminal"/> is true).</summary>
        public EpisodeOutcome Outcome;
        /// <summary>Per-step reward breakdown (valid only when <see cref="IsTerminal"/> is false).</summary>
        public RewardStepSummary StepRewards;
    }

    /// <summary>
    /// Caches component references. Call once from the agent's <c>Initialize</c>.
    /// </summary>
    public void Initialise()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Resets episode-scoped state (previous distance tracker).
    /// Call at the start of every episode.
    /// </summary>
    public void ResetEpisode()
    {
        _previousDistance = -1f;
        _startDistance = -1f;
    }

    /// <summary>
    /// Checks terminal conditions and computes all per-step reward components.
    /// The caller is responsible for calling <c>AddReward / SetReward / EndEpisode</c>.
    /// </summary>
    /// <param name="profile">Reward magnitudes and thresholds.</param>
    /// <param name="targetPosition">Current target position (local space).</param>
    /// <param name="startPosition">Drone start position (for proximity baseline).</param>
    /// <param name="maxEpisodeDistance">Max allowed distance before episode termination.</param>
    /// <param name="currentActions">Continuous actions issued this step (used for smoothness + energy fallback).</param>
    /// <param name="previousActions">Actions from the previous step.</param>
    /// <param name="energyValues">
    /// Values forwarded to <see cref="DroneRewardMath.EnergyPenalty"/>.
    /// Pass <c>null</c> to fall back to <paramref name="currentActions"/>.
    /// </param>
    public EvalResult Evaluate(
        DroneRewardProfile profile,
        Vector3 targetPosition,
        Vector3 startPosition,
        float maxEpisodeDistance,
        float[] currentActions,
        float[] previousActions,
        float[] energyValues = null)
    {
        float distanceToTarget = Vector3.Distance(transform.localPosition, targetPosition);

        // --- Terminal conditions ---
        var tilt = DroneRewardMath.CheckExcessiveTilt(
            transform.up, _maxTiltDot, profile.excessiveTiltPenalty);
        if (tilt.IsTerminal) return MakeTerminal(tilt.Reward, EpisodeOutcome.Safety_ExcessiveTilt);

        var tooFar = DroneRewardMath.CheckTooFar(
            distanceToTarget, maxEpisodeDistance, profile.tooFarPenalty);
        if (tooFar.IsTerminal) return MakeTerminal(tooFar.Reward, EpisodeOutcome.Safety_BoundaryLeft);

        // --- Per-step rewards ---
        if (_previousDistance < 0f)
            _startDistance = distanceToTarget;

        float deltaReward = _previousDistance >= 0f
            ? DroneRewardMath.DeltaDistanceReward(
                  _previousDistance, distanceToTarget, profile.deltaDistanceScale)
            : 0f;
        float normalizedDeltaReward = _previousDistance >= 0f
            ? DroneRewardMath.NormalizedDeltaDistanceReward(
                  _previousDistance, distanceToTarget, _startDistance,
                  profile.normalizedDeltaDistanceMaxProgressReward)
            : 0f;
        _previousDistance = distanceToTarget;

        var summary = new RewardStepSummary(
            deltaReward,
            normalizedDeltaReward,
            DroneRewardMath.ProximityReward(
                transform.localPosition, targetPosition, startPosition, profile.proximityRewardScale),
            DroneRewardMath.EnergyPenalty(energyValues ?? currentActions, profile.energyScale),
            DroneRewardMath.ActionSmoothnessPenalty(currentActions, previousActions, profile.smoothnessScale),
            DroneRewardMath.TiltPenalty(transform.up, profile.tiltPenaltyScale),
            DroneRewardMath.AngularVelocityPenalty(
                rb.angularVelocity.magnitude, profile.angularVelocityPenaltyScale),
            DroneRewardMath.VelocityAlignmentReward(
                rb.linearVelocity, targetPosition - transform.localPosition, profile.velocityAlignmentScale),
            DroneRewardMath.TimePenalty(profile.timeScale),
            DroneRewardMath.FastApproachPenalty(
                rb.linearVelocity.magnitude, distanceToTarget, profile.landingRadius, profile.fastApproachScale)
        );

        return new EvalResult { IsTerminal = false, StepRewards = summary };
    }

    private static EvalResult MakeTerminal(float reward, EpisodeOutcome outcome)
    {
        return new EvalResult { IsTerminal = true, TerminalReward = reward, Outcome = outcome };
    }

    /// <summary>
    /// Evaluates a collision event and returns the appropriate terminal result.
    /// Returns a non-terminal result when the collision is not meaningful (e.g. already landed).
    /// The caller is responsible for calling <c>AddReward / SetReward / EndEpisode</c>.
    /// </summary>
    /// <param name="profile">Reward magnitudes and thresholds.</param>
    /// <param name="collidedTransform">The transform the drone collided with.</param>
    /// <param name="targetTransform">The arena's target/landing-pad transform.</param>
    /// <param name="alreadyLanded"><c>true</c> if the drone has already registered a landing this episode.</param>
    public CollisionResult EvaluateCollision(
        DroneRewardProfile profile,
        Transform collidedTransform,
        Transform targetTransform,
        bool alreadyLanded)
    {
        if (targetTransform != null && collidedTransform == targetTransform)
        {
            if (alreadyLanded)
                return new CollisionResult { Kind = CollisionKind.None };

            float reward = profile != null ? profile.landingSuccess : 1f;
            return new CollisionResult { Kind = CollisionKind.Landing, Reward = reward };
        }

        float crashReward = profile != null ? profile.obstacleCollision : DroneRewardMath.ObstaclePenalty;
        return new CollisionResult { Kind = CollisionKind.Crash, Reward = crashReward };
    }

    /// <summary>Type of collision detected by <see cref="EvaluateCollision"/>.</summary>
    public enum CollisionKind { None, Landing, Crash }

    /// <summary>Result of <see cref="EvaluateCollision"/>.</summary>
    public struct CollisionResult
    {
        public CollisionKind Kind;
        public float Reward;
    }
}
