using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for drone ML-Agents. Acts as the "glue" that
/// orchestrates companion components and manages the episode lifecycle.
///
/// Companion components handle specialised concerns:
///   • <see cref="DroneAerodynamics"/>         — mass, gravity, quadratic drag
///   • <see cref="DroneCurriculumManager"/>    — curriculum, spawn placement, obstacles
///   • <see cref="DroneObserver"/>             — observation collection
///   • <see cref="DroneRewardEvaluator"/>      — terminal checks, per-step reward math
///   • <see cref="DroneTelemetry"/>            — TensorBoard stats, Inspector debug strings
///
/// Subclasses implement <see cref="Agent.OnActionReceived"/> and
/// <see cref="Agent.Heuristic"/> for their specific control schemes.
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneAerodynamics))]
[RequireComponent(typeof(DroneCurriculumManager))]
[RequireComponent(typeof(DroneObserver))]
[RequireComponent(typeof(DroneRewardEvaluator))]
[RequireComponent(typeof(DroneTelemetry))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;

    [Header("Reward Profile")]
    [Tooltip("Scriptable object that controls all reward magnitudes and safety thresholds.")]
    [SerializeField] protected DroneRewardProfile rewardProfile;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;
    protected float maxEpisodeDistance;
    protected bool hasLanded;
    protected float touchdownTimer;
    protected DroneCurriculumManager curriculumManager;
    protected DroneObserver observer;
    protected DroneRewardEvaluator rewardEvaluator;
    protected DroneTelemetry telemetry;

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

        observer = GetComponent<DroneObserver>();
        observer.Initialise();

        rewardEvaluator = GetComponent<DroneRewardEvaluator>();
        rewardEvaluator.Initialise();

        telemetry = GetComponent<DroneTelemetry>();
    }

    public override void OnEpisodeBegin()
    {
        ResetPhysics();
        rewardEvaluator.ResetEpisode();
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
        float distNorm = rewardProfile != null ? rewardProfile.distanceNorm : 10f;
        observer.Collect(sensor, targetPos, distNorm);
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
    /// Delegates to <see cref="DroneRewardEvaluator"/> for terminal checks and
    /// per-step reward computation, then forwards results to <see cref="DroneTelemetry"/>.
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

        var result = rewardEvaluator.Evaluate(
            rewardProfile, targetPos, startPosition,
            maxTiltDot, maxEpisodeDistance,
            currentActions, previousActions, energyValues);

        if (result.IsTerminal)
        {
            SetReward(result.TerminalReward);
            EndEpisode();
            return true;
        }

        System.Array.Copy(currentActions, previousActions,
            Mathf.Min(currentActions.Length, previousActions.Length));

        AddReward(result.StepRewards.Total);
        telemetry.Record(result.StepRewards);

        return false;
    }
}
