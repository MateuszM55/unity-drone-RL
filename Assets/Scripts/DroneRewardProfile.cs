using UnityEngine;

/// <summary>
/// Scriptable-object "control panel" for all drone reward magnitudes and safety thresholds.
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
    [Tooltip("Maximum reward for landing on the target (multiplied by the soft-landing factor).")]
    public float landingSuccess = 5f;
    [Tooltip("Terminal penalty applied when the drone collides with an obstacle or the ground.")]
    public float obstacleCollision = -10f;

    [Header("Step Penalty Scales")]
    [Tooltip("Scale for the delta-distance (potential-based progress) reward per step.")]
    public float deltaDistanceScale = 0.01f;
    [Tooltip("Scale for the motor energy penalty per step.")]
    public float energyScale = 0.001f;
    [Tooltip("Scale for the action-smoothness penalty per step.")]
    public float smoothnessScale = 0.0005f;
    [Tooltip("Constant time penalty per step to encourage faster task completion.")]
    public float timeScale = 0.001f;
    [Tooltip("Scale for the fast-approach penalty inside the landing radius.")]
    public float fastApproachScale = 0.002f;
    [Tooltip("Radius (metres) within which the fast-approach penalty is active.")]
    public float landingRadius = 5f;

    [Header("Safety Thresholds")]
    [Tooltip("Reference landing speed (m/s) at which the landing reward halves. Lower = stricter.")]
    public float maxSafeLandingSpeed = 2f;
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    public float maxTiltAngle = 60f;

    [Header("Terminal Condition Rewards")]
    [Tooltip("Distance threshold (metres) at which the drone is considered to have reached the target.")]
    public float targetReachedThreshold = 0.5f;
    [Tooltip("Reward given when the drone reaches the target (CheckTargetReached).")]
    public float targetReachedReward = 1.0f;
    [Tooltip("Penalty applied when the drone flies too far from the target (CheckTooFar).")]
    public float tooFarPenalty = -1.0f;
    [Tooltip("Minimum Y position (world space) before the drone is considered fallen (CheckFallen).")]
    public float fallenMinY = -0.5f;
    [Tooltip("Penalty applied when the drone falls below fallenMinY (CheckFallen).")]
    public float fallenPenalty = -1.0f;
    [Tooltip("Penalty applied when the drone tilts beyond maxTiltAngle (CheckExcessiveTilt).")]
    public float excessiveTiltPenalty = -1.0f;

    [Header("Continuous Shaping Scales")]
    [Tooltip("Scale for the fraction-of-distance proximity reward (ProximityReward).")]
    public float proximityRewardScale = 0.01f;
    [Tooltip("Scale for the tilt-deviation penalty (TiltPenalty).")]
    public float tiltPenaltyScale = 0.005f;
    [Tooltip("Scale for the angular-velocity penalty (AngularVelocityPenalty).")]
    public float angularVelocityPenaltyScale = 0.001f;
    [Tooltip("Scale for the velocity-alignment reward (VelocityAlignmentReward).")]
    public float velocityAlignmentScale = 0.01f;

    [Header("Observation Parameters")]
    [Tooltip("Normalisation constant for the tanh-compressed distance observation. " +
             "Distance at which the observation value reaches ~0.76 (DecomposeTargetVector).")]
    public float distanceNorm = 10f;
}
