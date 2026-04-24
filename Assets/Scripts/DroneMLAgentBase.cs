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
///   • <see cref="TrainingArena"/>             — arena identity, curriculum, spawn placement, obstacles
///   • <see cref="DroneObserver"/>             — observation collection
///   • <see cref="DroneRewardManager"/>        — terminal checks, per-step reward math
///   • <see cref="DroneTelemetry"/>            — TensorBoard stats, Inspector debug strings
///
/// Subclasses implement <see cref="Agent.OnActionReceived"/> and
/// <see cref="Agent.Heuristic"/> for their specific control schemes.
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
///
/// <b>Arena Architecture:</b>
/// The drone discovers its <see cref="TrainingArena"/> via parent hierarchy lookup,
/// enabling multiple arena instances with isolated curriculum management.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneAerodynamics))]
[RequireComponent(typeof(DroneObserver))]
[RequireComponent(typeof(DroneRewardManager))]
[RequireComponent(typeof(DroneTelemetry))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;

    [Header("Reward Profile")]
    [Tooltip("Scriptable object that controls all reward magnitudes and thresholds.")]
    [SerializeField] protected DroneRewardProfile rewardProfile;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;
    protected float maxEpisodeDistance;
    protected bool hasLanded;
    protected float touchdownTimer;
    protected ITrainingArena arena;
    protected DroneObserver observer;
    protected DroneRewardManager rewardEvaluator;
    protected DroneTelemetry telemetry;

    /// <summary>Convenience accessor — delegates to <see cref="TrainingArena.Target"/>.</summary>
    protected Transform target => arena?.Target;

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

        // Discover training arena in parent hierarchy (arena-centric architecture).
        // GetComponentInParent does not support interfaces, so we retrieve the concrete
        // TrainingArena and store it via the ITrainingArena interface for loose coupling.
        TrainingArena concreteArena = GetComponentInParent<TrainingArena>();
        if (concreteArena == null)
        {
            Debug.LogError($"[{name}] No TrainingArena found in parent hierarchy. " +
                "Add TrainingArena to the Arena Root GameObject.", this);
        }
        else
        {
            concreteArena.Initialise();
            arena = concreteArena;
        }

        observer = GetComponent<DroneObserver>();
        observer.Initialise();

        rewardEvaluator = GetComponent<DroneRewardManager>();
        rewardEvaluator.Initialise();

        telemetry = GetComponent<DroneTelemetry>();
    }

    public override void OnEpisodeBegin()
    {
        ResetPhysics();
        rewardEvaluator.ResetEpisode();

        if (arena != null)
        {
            maxEpisodeDistance = arena.SetupEpisode(transform, startPosition, startRotation);
            telemetry.OnNewEpisode(arena.CurrentLessonIndex);
        }
        else
        {
            // Fallback for missing arena
            maxEpisodeDistance = 10f;
            transform.localPosition = startPosition;
            transform.localRotation = startRotation;
            telemetry.OnNewEpisode(0);
        }

        if (target == null)
        {
            Debug.LogWarning($"[{name}] No target is set.", this);
            return;
        }

        // Calibrate the observer's two-dial meters to this episode's start distances
        observer.StartEpisode(transform.localPosition, target.localPosition);
    }

    /// <summary>
    /// Zeroes velocity / angular velocity and resets the touchdown state.
    /// Called at the start of every episode before the arena
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
        observer.Collect(sensor, targetPos);
    }

    protected virtual void FixedUpdate()
    {
        if (hasLanded)
        {
            touchdownTimer -= Time.fixedDeltaTime;
            if (touchdownTimer <= 0f)
            {
                telemetry.FlushEpisode(EpisodeOutcome.Success_TargetReached);
                EndEpisode();
                return;
            }
        }
    }

    protected virtual void OnCollisionEnter(Collision collision)
    {
        // Landing on the target: flat touchdown reward
        if (target != null && collision.transform == target)
        {
            if (!hasLanded)
            {
                hasLanded = true;
                touchdownTimer = touchdownDelay;
                AddReward(DroneRewardMath.TouchdownReward(rewardProfile != null ? rewardProfile.landingSuccess : 1f));
            }
            return;
        }

        // Collision with obstacle or ground
        SetReward(rewardProfile != null ? rewardProfile.obstacleCollision : DroneRewardMath.ObstaclePenalty);
        telemetry.FlushEpisode(EpisodeOutcome.Crash);
        EndEpisode();
    }

    /// <summary>
    /// Delegates to <see cref="DroneRewardManager"/> for terminal checks and
    /// per-step reward computation, then forwards results to <see cref="DroneTelemetry"/>.
    /// Call this at the end of <see cref="Agent.OnActionReceived"/> after applying forces.
    /// </summary>
    /// <param name="currentActions">Continuous actions issued this step (smoothness + energy fallback).</param>
    /// <param name="previousActions">Actions from the previous step; updated in-place after smoothness is computed.</param>
    /// <param name="energyValues">
    /// Values forwarded to <see cref="DroneRewardMath.EnergyPenalty"/>.
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

        Vector3 targetPos = DroneRewardMath.ResolveTargetPosition(target, startPosition);

        var result = rewardEvaluator.Evaluate(
            rewardProfile, targetPos, startPosition,
            maxTiltDot, maxEpisodeDistance,
            currentActions, previousActions, energyValues);

        if (result.IsTerminal)
        {
            SetReward(result.TerminalReward);
            telemetry.FlushEpisode(result.Outcome);
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
