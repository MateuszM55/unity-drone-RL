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
public class DroneRewardEvaluator : MonoBehaviour
{
    private Rigidbody rb;
    private float _previousDistance = -1f;

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
    }

    /// <summary>
    /// Checks terminal conditions and computes all per-step reward components.
    /// The caller is responsible for calling <c>AddReward / SetReward / EndEpisode</c>.
    /// </summary>
    /// <param name="profile">Reward magnitudes and thresholds.</param>
    /// <param name="targetPosition">Current target position (local space).</param>
    /// <param name="startPosition">Drone start position (for proximity baseline).</param>
    /// <param name="maxTiltDot">Pre-computed cosine of the maximum safe tilt angle.</param>
    /// <param name="maxEpisodeDistance">Max allowed distance before episode termination.</param>
    /// <param name="currentActions">Continuous actions issued this step (used for smoothness + energy fallback).</param>
    /// <param name="previousActions">Actions from the previous step.</param>
    /// <param name="energyValues">
    /// Values forwarded to <see cref="DroneRewardHelper.EnergyPenalty"/>.
    /// Pass <c>null</c> to fall back to <paramref name="currentActions"/>.
    /// </param>
    public EvalResult Evaluate(
        DroneRewardProfile profile,
        Vector3 targetPosition,
        Vector3 startPosition,
        float maxTiltDot,
        float maxEpisodeDistance,
        float[] currentActions,
        float[] previousActions,
        float[] energyValues = null)
    {
        float distanceToTarget = Vector3.Distance(transform.localPosition, targetPosition);

        // --- Terminal conditions ---
        var reached = DroneRewardHelper.CheckTargetReached(
            distanceToTarget, profile.targetReachedThreshold, profile.targetReachedReward);
        if (reached.IsTerminal) return MakeTerminal(reached.Reward, EpisodeOutcome.Success_TargetReached);

        var tilt = DroneRewardHelper.CheckExcessiveTilt(
            transform.up, maxTiltDot, profile.excessiveTiltPenalty);
        if (tilt.IsTerminal) return MakeTerminal(tilt.Reward, EpisodeOutcome.Safety_ExcessiveTilt);

        var tooFar = DroneRewardHelper.CheckTooFar(
            distanceToTarget, maxEpisodeDistance, profile.tooFarPenalty);
        if (tooFar.IsTerminal) return MakeTerminal(tooFar.Reward, EpisodeOutcome.Safety_BoundaryLeft);

        var fallen = DroneRewardHelper.CheckFallen(
            transform.localPosition.y, profile.fallenMinY, profile.fallenPenalty);
        if (fallen.IsTerminal) return MakeTerminal(fallen.Reward, EpisodeOutcome.Crash_Ground);

        // --- Per-step rewards ---
        float deltaReward = _previousDistance >= 0f
            ? DroneRewardHelper.DeltaDistanceReward(
                  _previousDistance, distanceToTarget, profile.deltaDistanceScale)
            : 0f;
        _previousDistance = distanceToTarget;

        var summary = new RewardStepSummary(
            deltaReward,
            DroneRewardHelper.ProximityReward(
                transform.localPosition, targetPosition, startPosition, profile.proximityRewardScale),
            DroneRewardHelper.EnergyPenalty(energyValues ?? currentActions, profile.energyScale),
            DroneRewardHelper.ActionSmoothnessPenalty(currentActions, previousActions, profile.smoothnessScale),
            DroneRewardHelper.TiltPenalty(transform.up, profile.tiltPenaltyScale),
            DroneRewardHelper.AngularVelocityPenalty(
                rb.angularVelocity.magnitude, profile.angularVelocityPenaltyScale),
            DroneRewardHelper.VelocityAlignmentReward(
                rb.linearVelocity, targetPosition - transform.localPosition, profile.velocityAlignmentScale),
            DroneRewardHelper.TimePenalty(profile.timeScale),
            DroneRewardHelper.FastApproachPenalty(
                rb.linearVelocity.magnitude, distanceToTarget, profile.landingRadius, profile.fastApproachScale)
        );

        return new EvalResult { IsTerminal = false, StepRewards = summary };
    }

    private static EvalResult MakeTerminal(float reward, EpisodeOutcome outcome)
    {
        return new EvalResult { IsTerminal = true, TerminalReward = reward, Outcome = outcome };
    }
}
