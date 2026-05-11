using UnityEngine;

/// <summary>
/// Scriptable-object "control panel" for all drone reward magnitudes and thresholds.
///
/// Create profiles via the Unity menu: Assets → Create → Drone → Reward Profile.
/// Assign the desired profile to the <c>rewardProfile</c> field on any
/// <see cref="DroneMLAgentBase"/> subclass in the Inspector.
/// Values can be tweaked live during Play Mode for rapid iteration.
/// </summary>
[CreateAssetMenu(fileName = "NewRewardProfile", menuName = "Drone/Reward Profile")]
public class DroneRewardProfile : ScriptableObject
{
    [Header("Main Rewards")]
    [Tooltip("One-time reward added when the drone successfully lands on the target pad. This is the single biggest positive signal in an episode — keep it larger than the sum of all per-step rewards so landing is always the most attractive outcome. Typical range: 3 – 20.")]
    public float landingSuccess = 5f;
    [Tooltip("One-time penalty added when the drone hits an obstacle or the ground (episode ends). Should be large enough to strongly discourage crashes. Make it at least as large as landingSuccess in magnitude. Typical range: -5 to -20.")]
    public float obstacleCollision = -10f;

    [Header("Step Penalty Scales")]
    [Tooltip("Sets the maximum total reward the drone can earn over a full episode from normalised progress (each step's progress is expressed as a fraction of the starting distance). Raise this to make steady approach more important relative to the landing bonus. Typical episode total: 0 – 8. Recommended: 2 – 15.")]
    public float normalizedDeltaDistanceMaxProgressReward = 3f;
    [Tooltip("Fixed penalty subtracted every step to push the drone to complete the task quickly. Over a 1000-step episode the total cost is scale × 1000. Typical episode total: -0.5 to -2. Recommended scale: 0.0005 – 0.003.")]
    public float timeScale = 0.001f;

    [Header("Terminal Condition Rewards")]
    [Tooltip("If the drone tilts beyond this many degrees from vertical the episode ends with an excessive-tilt penalty. Lower values demand more stable flight. Typical value: 45 – 75 degrees.")]
    public float maxTiltAngle = 60f;
    [Tooltip("One-time penalty applied when the drone wanders beyond the allowed area and the episode ends. Should discourage exploration in the wrong direction. Typical range: -0.5 to -3.")]
    public float tooFarPenalty = -1.0f;
    [Tooltip("One-time penalty when the drone tilts beyond maxTiltAngle and the episode ends. Typical range: -0.5 to -3.")]
    public float excessiveTiltPenalty = -10.0f;

    [Header("Post-Touchdown Penalties")]
    [Tooltip("Subtracted every step after first pad contact, scaled by total motion via soft-sign: penalty = −scale × (x / (1 + x)) where x = linearSpeed + angularSpeed. Bounds the penalty as speeds grow large while staying sensitive over a wide speed range. Encourages the agent to settle quickly once landed. Typical range: 0.01 – 0.2.")]
    public float restlessnessScale = 0.05f;

    [Header("Yaw Deviation Penalty")]
    [Tooltip("Maximum yaw angle (degrees) the drone may deviate from facing the target before a penalty is applied. At this angle penalty is 0; at 180° penalty equals yawDeviationScale. Typical range: 30 – 90°.")]
    public float maxYawDeviationAngle = 45f;
    [Tooltip("Maximum per-step penalty applied when the drone faces directly away from the target. Scales linearly from 0 at maxYawDeviationAngle to this value at 180°. Typical range: 0.001 – 0.02.")]
    public float yawDeviationScale = 0.001f;
}
