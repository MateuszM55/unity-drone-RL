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
    public float landingSuccess = 20f;
    [Tooltip("Terminal penalty applied when the drone collides with an obstacle or the ground.")]
    public float obstacleCollision = -10f;

    [Header("Step Penalty Scales")]
    [Tooltip("Scale for the delta-distance (potential-based progress) reward per step.")]
    public float deltaDistanceScale = 1.0f;
    [Tooltip("Scale for the motor energy penalty per step.")]
    public float energyScale = 0.001f;
    [Tooltip("Scale for the action-smoothness penalty per step.")]
    public float smoothnessScale = 0.0005f;
    [Tooltip("Constant time penalty per step to encourage faster task completion.")]
    public float timeScale = 0.001f;

    [Header("Safety Thresholds")]
    [Tooltip("Reference landing speed (m/s) at which the landing reward halves. Lower = stricter.")]
    public float maxSafeLandingSpeed = 2f;
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    public float maxTiltAngle = 60f;
}
