using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for drone ML-Agents. Provides shared observation
/// collection, episode reset, collision handling, and touchdown logic.
///
/// Companion components handle specialised concerns:
///   • <see cref="DroneAerodynamics"/>         — mass, gravity, quadratic drag
///   • <see cref="DroneCurriculumManager"/>    — curriculum, spawn placement, obstacles
///
/// Subclasses implement <see cref="Agent.OnActionReceived"/> and
/// <see cref="Agent.Heuristic"/> for their specific control schemes.
///
/// OBSERVATION SPACE (20 floats — body-local frame where applicable):
///   - Local unit direction to target        (3)  direction only, always [-1,1]
///   - Squashed distance to target           (1)  tanh(d/10), always [0,1)
///   - Drone velocity (local)                (3)
///   - Drone angular velocity (local)        (3)
///   - Drone orientation (forward)           (3)  world-frame attitude
///   - Drone orientation (up)                (3)  world-frame attitude
///   (right = cross(forward, up) — omitted, linearly dependent)
///   - Previous motor actions FL/FR/RL/RR    (4)  proprioception / muscle memory
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneAerodynamics))]
[RequireComponent(typeof(DroneCurriculumManager))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;

    [Header("Reward Profile")]
    [Tooltip("Scriptable object that controls all reward magnitudes and safety thresholds.")]
    [SerializeField] protected DroneRewardProfile rewardProfile;

    [Header("Debug - Live Rewards")]
    public string debugDeltaDist;
    public string debugEnergy;
    public string debugSmoothness;
    public string debugTime;
    public string debugTotalStepReward;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;
    protected float maxEpisodeDistance;
    protected bool hasLanded;
    protected float touchdownTimer;
    protected float _previousDistance = -1f;
    protected DroneCurriculumManager curriculumManager;

    /// <summary>Convenience accessor — delegates to <see cref="DroneCurriculumManager.Target"/>.</summary>
    protected Transform target => curriculumManager.Target;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

        // Physics configuration (mass, gravity, damping) is owned by DroneAerodynamics
        GetComponent<DroneAerodynamics>().InitialisePhysics();

        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        keyboard = Keyboard.current;
        maxTiltDot = rewardProfile != null
            ? Mathf.Cos(rewardProfile.maxTiltAngle * Mathf.Deg2Rad)
            : Mathf.Cos(60f * Mathf.Deg2Rad);

        curriculumManager = GetComponent<DroneCurriculumManager>();
        curriculumManager.Initialise();
    }

    public override void OnEpisodeBegin()
    {
        ResetPhysics();
        _previousDistance = -1f;
        maxEpisodeDistance = curriculumManager.SetupEpisode(transform, startPosition, startRotation);

        if (target == null)
            Debug.LogWarning($"[{name}] No target is set.", this);
    }

    /// <summary>
    /// Zeroes velocity / angular velocity and resets the touchdown state.
    /// Called at the start of every episode before the curriculum manager
    /// repositions the drone.
    /// </summary>
    protected void ResetPhysics()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        hasLanded = false;
        touchdownTimer = 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.localPosition : startPosition;

        // Decompose target vector → local unit direction (3) + squashed distance (1)
        float distNorm = rewardProfile != null ? rewardProfile.distanceNorm : 10f;
        DroneRewardHelper.DecomposeTargetVector(
            transform, targetPos - transform.localPosition,
            out Vector3 localDir, out float squashedDist, distNorm);
        sensor.AddObservation(localDir);
        sensor.AddObservation(squashedDist);

        // Velocity in body frame (3) — matches real IMU output
        sensor.AddObservation(transform.InverseTransformDirection(rb.linearVelocity));

        // Angular velocity in body frame (3) — matches real gyroscope output
        sensor.AddObservation(transform.InverseTransformDirection(rb.angularVelocity));

        // Orientation axes (6) — world-frame attitude for gravity awareness
        // (right omitted: right = cross(forward, up), linearly dependent)
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.up);
    }

    protected virtual void FixedUpdate()
    {
        if (hasLanded)
        {
            touchdownTimer -= Time.fixedDeltaTime;
            if (touchdownTimer <= 0f)
            {
                EndEpisode();
                return;
            }
        }
    }

    protected virtual void OnCollisionEnter(Collision collision)
    {
        // Landing on the target: reward inversely proportional to touchdown speed
        if (target != null && collision.transform == target)
        {
            if (!hasLanded)
            {
                hasLanded = true;
                touchdownTimer = touchdownDelay;
                float speed   = rb.linearVelocity.magnitude;
                float maxSafe = rewardProfile != null ? rewardProfile.maxSafeLandingSpeed : 2f;
                float scale   = rewardProfile != null ? rewardProfile.landingSuccess      : 1f;
                AddReward(scale * DroneRewardHelper.TouchdownReward(speed, maxSafe));
            }
            return;
        }

        // Collision with obstacle or ground
        SetReward(rewardProfile != null ? rewardProfile.obstacleCollision : DroneRewardHelper.ObstaclePenalty);
        EndEpisode();
    }

    /// <summary>
    /// Checks terminal conditions, computes standard per-step rewards using the reward
    /// profile, sends stats to TensorBoard, and updates the Inspector debug strings.
    /// Call this at the end of <see cref="Agent.OnActionReceived"/> after applying forces.
    /// </summary>
    /// <param name="currentActions">Continuous actions issued this step (smoothness + energy fallback).</param>
    /// <param name="previousActions">Actions from the previous step; updated in-place after smoothness is computed.</param>
    /// <param name="energyValues">
    /// Values forwarded to <see cref="DroneRewardHelper.EnergyPenalty"/>.
    /// Pass <c>null</c> to fall back to <paramref name="currentActions"/>.
    /// </param>
    /// <returns><c>true</c> if the episode was terminated; the caller must return immediately.</returns>
    protected bool ApplyStandardRewards(float[] currentActions, float[] previousActions, float[] energyValues = null)
    {
        if (rewardProfile == null)
        {
            Debug.LogWarning($"[{name}] rewardProfile is not assigned — skipping standard rewards.", this);
            return false;
        }

        Vector3 targetPos = DroneRewardHelper.ResolveTargetPosition(target, startPosition);
        float distanceToTarget = Vector3.Distance(transform.localPosition, targetPos);

        // --- Terminal conditions ---
        var tilt = DroneRewardHelper.CheckExcessiveTilt(transform.up, maxTiltDot, rewardProfile.excessiveTiltPenalty);
        if (tilt.IsTerminal) { SetReward(tilt.Reward); EndEpisode(); return true; }

        var tooFar = DroneRewardHelper.CheckTooFar(distanceToTarget, maxEpisodeDistance, rewardProfile.tooFarPenalty);
        if (tooFar.IsTerminal) { SetReward(tooFar.Reward); EndEpisode(); return true; }

        var fallen = DroneRewardHelper.CheckFallen(transform.localPosition.y, rewardProfile.fallenMinY, rewardProfile.fallenPenalty);
        if (fallen.IsTerminal) { SetReward(fallen.Reward); EndEpisode(); return true; }

        // --- Per-step rewards ---
        float deltaReward = _previousDistance >= 0f
            ? DroneRewardHelper.DeltaDistanceReward(_previousDistance, distanceToTarget, rewardProfile.deltaDistanceScale)
            : 0f;
        _previousDistance = distanceToTarget;

        float energyPenalty     = DroneRewardHelper.EnergyPenalty(energyValues ?? currentActions, rewardProfile.energyScale);
        float smoothnessPenalty = DroneRewardHelper.ActionSmoothnessPenalty(currentActions, previousActions, rewardProfile.smoothnessScale);
        float timePenalty       = DroneRewardHelper.TimePenalty(rewardProfile.timeScale);

        System.Array.Copy(currentActions, previousActions, Mathf.Min(currentActions.Length, previousActions.Length));

        AddReward(deltaReward);
        AddReward(energyPenalty);
        AddReward(smoothnessPenalty);
        AddReward(timePenalty);

        // --- TensorBoard stats ---
        var stats = Academy.Instance.StatsRecorder;
        stats.Add("Rewards/DeltaDistance", deltaReward);
        stats.Add("Rewards/Energy",        energyPenalty);
        stats.Add("Rewards/Smoothness",    smoothnessPenalty);
        stats.Add("Rewards/Time",          timePenalty);

        // --- Inspector debug ---
        const string fmt = " 0.00000;-0.00000";
        debugDeltaDist       = deltaReward.ToString(fmt);
        debugEnergy          = energyPenalty.ToString(fmt);
        debugSmoothness      = smoothnessPenalty.ToString(fmt);
        debugTime            = timePenalty.ToString(fmt);
        debugTotalStepReward = (deltaReward + energyPenalty + smoothnessPenalty + timePenalty).ToString(fmt);

        return false;
    }
}
