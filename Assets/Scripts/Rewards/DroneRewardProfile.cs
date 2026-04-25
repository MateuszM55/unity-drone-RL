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
    [Tooltip("Added every step: scale × (distance_last_step − distance_this_step). Positive when the drone moves closer, negative when it moves away. Larger values speed up early learning but can cause jittery flight. Typical per-step range: -0.05 to +0.05. Recommended scale: 0.005 – 0.05.")]
    public float deltaDistanceScale = 0.01f;
    [Tooltip("Sets the maximum total reward the drone can earn over a full episode from normalised progress (each step's progress is expressed as a fraction of the starting distance). Raise this to make steady approach more important relative to the landing bonus. Typical episode total: 0 – 8. Recommended: 2 – 15.")]
    public float normalizedDeltaDistanceMaxProgressReward = 8f;
    [Tooltip("Subtracted every step proportional to the mean squared normalised thrust across all motors (actions mapped from [−1,1] to [0,1] before squaring). Discourages wasting energy hovering at full power. Typical per-step penalty: -0.001 to -0.01. Recommended scale: 0.0005 – 0.005.")]
    public float energyScale = 0.001f;
    [Tooltip("Subtracted every step proportional to the mean absolute change in motor commands between steps. Encourages smooth, stable flight. Typical per-step penalty: -0.0005 to -0.005. Recommended scale: 0.0001 – 0.002.")]
    public float smoothnessScale = 0.0005f;
    [Tooltip("Fixed penalty subtracted every step to push the drone to complete the task quickly. Over a 1000-step episode the total cost is scale × 1000. Typical episode total: -0.5 to -2. Recommended scale: 0.0005 – 0.003.")]
    public float timeScale = 0.001f;
    [Tooltip("Subtracted every step when the drone is inside landingRadius and moving fast (any direction). Encourages a gentle final descent. penalty = −scale × speed × (1 − distance/radius). Typical per-step penalty: -0.002 to -0.02. Recommended scale: 0.001 – 0.01.")]
    public float fastApproachScale = 0.002f;
    [Tooltip("How close (metres) the drone must be before the fast-approach penalty kicks in. Should be larger than targetReachedThreshold. Typical value: 2 – 10 m.")]
    public float landingRadius = 5f;

    [Header("Terminal Condition Rewards")]
    [Tooltip("If the drone tilts beyond this many degrees from vertical the episode ends with an excessive-tilt penalty. Lower values demand more stable flight. Typical value: 45 – 75 degrees.")]
    public float maxTiltAngle = 60f;
    [Tooltip("One-time penalty applied when the drone wanders beyond the allowed area and the episode ends. Should discourage exploration in the wrong direction. Typical range: -0.5 to -3.")]
    public float tooFarPenalty = -1.0f;
    [Tooltip("One-time penalty when the drone tilts beyond maxTiltAngle and the episode ends. Typical range: -0.5 to -3.")]
    public float excessiveTiltPenalty = -1.0f;

    [Header("Continuous Shaping Scales")]
    [Tooltip("Added every step as scale × (1 − distance/startDistance). Reward is highest when the drone is close to the target and zero at the starting distance. Complements deltaDistanceScale by rewarding absolute proximity rather than change. Typical per-step range: 0 – 0.01. Recommended scale: 0.005 – 0.05.")]
    public float proximityRewardScale = 0.01f;
    [Tooltip("Subtracted every step proportional to how far the drone is tilted from level (tilt_degrees / maxTiltAngle). Encourages stable, upright hovering. Typical per-step penalty: -0.001 to -0.01. Recommended scale: 0.001 – 0.01.")]
    public float tiltPenaltyScale = 0.005f;
    [Tooltip("Subtracted every step proportional to the drone's angular velocity magnitude. Penalises spinning and wobbling even when tilt is low. Typical per-step penalty: -0.0005 to -0.005. Recommended scale: 0.0005 – 0.005.")]
    public float angularVelocityPenaltyScale = 0.001f;
    [Tooltip("Added every step proportional to how directly the drone is flying toward the target (dot product of velocity and target direction, clamped 0–1). Encourages efficient straight-line approach. Typical per-step range: 0 – 0.01. Recommended scale: 0.005 – 0.05.")]
    public float velocityAlignmentScale = 0.01f;

    [Header("Post-Touchdown Penalties")]
    [Tooltip("Applied every step after first pad contact. Penalty = -scale × linearSpeed². Punishes sliding, bouncing, or any translational movement after landing. Typical range: 0.01 – 0.2.")]
    public float restlessnessLinearScale = 0.05f;
    [Tooltip("Applied every step after first pad contact. Penalty = -scale × angularSpeed². Punishes spinning/tumbling after landing. Typical range: 0.01 – 0.2.")]
    public float restlessnessAngularScale = 0.05f;
}
