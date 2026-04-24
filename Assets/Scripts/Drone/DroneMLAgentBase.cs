using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Abstract base class for drone ML-Agents. Acts as the "glue" that
/// orchestrates companion components and manages the episode lifecycle.
///
/// Companion components handle specialised concerns:
///   - DroneAerodynamics   -- mass, gravity, quadratic drag
///   - TrainingArena       -- arena identity, curriculum, spawn placement, obstacles
///   - DroneObserver       -- observation collection
///   - DroneRewardManager  -- terminal checks, per-step reward math
///   - DroneTelemetry      -- TensorBoard stats, Inspector debug strings
///
/// Subclasses implement Agent.OnActionReceived and Agent.Heuristic for their
/// specific control schemes.  They MUST populate _currentActionsBuffer with
/// the raw action values for each motor before calling ApplyStandardRewards,
/// so that smoothness and energy rewards are computed correctly.
///
/// The drone model is generated via DroneGenerator.
///
/// Arena Architecture:
/// The drone discovers its TrainingArena via GetComponentInParent, enabling
/// multiple arena instances with isolated curriculum management.
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

    [Header("Motor Setup")]
    [Tooltip("Rotor transforms assigned automatically by DroneGenerator. Order: FL(0), FR(1), RL(2), RR(3).")]
    [SerializeField] protected Transform[] rotorTransforms;

    [Tooltip("Absolute maximum thrust a single motor can produce in Newtons.")]
    [SerializeField] protected float maxThrustPerMotor = 5f;

    // Rigidbody -- used by subclasses to apply forces.
    protected Rigidbody rb;

    // Action buffers -- subclasses MUST write _currentActionsBuffer before calling
    // ApplyStandardRewards so smoothness / energy rewards are computed correctly.
    protected readonly float[] _previousActions      = new float[4];
    protected readonly float[] _currentActionsBuffer = new float[4];

    // Private state -- not exposed to subclasses; access via protected helpers only.
    private Vector3            _startPosition;
    private Quaternion         _startRotation;
    private Vector3            _episodeStartPosition;
    private float              _maxEpisodeDistance;
    private bool               _hasLanded;
    private float              _touchdownTimer;
    private ITrainingArena     _arena;
    private DroneObserver      _observer;
    private DroneRewardManager _rewardEvaluator;
    private DroneTelemetry     _telemetry;

    /// <summary>
    /// Convenience accessor for the target transform in this arena.
    /// All positional math uses local-space coordinates: always pass
    /// <c>target.localPosition</c>, never <c>target.position</c>, to
    /// observers or reward helpers.
    /// </summary>
    protected Transform target => _arena?.Target;

    /// <summary>
    /// Clears the shared action buffers. Call at the start of every episode
    /// to prevent stale values bleeding into the first-step reward computation.
    /// </summary>
    protected void ClearActionBuffers()
    {
        System.Array.Clear(_previousActions,      0, _previousActions.Length);
        System.Array.Clear(_currentActionsBuffer, 0, _currentActionsBuffer.Length);
    }

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

        // Physics configuration (mass, gravity, damping) is owned by DroneAerodynamics.
        GetComponent<DroneAerodynamics>().InitialisePhysics();

        _startPosition = transform.localPosition;
        _startRotation = transform.localRotation;

        // Discover the training arena via parent hierarchy.
        // Retrieve the concrete TrainingArena, then store via ITrainingArena for loose coupling.
        TrainingArena concreteArena = GetComponentInParent<TrainingArena>();
        if (concreteArena == null)
        {
            Debug.LogError($"[{name}] No TrainingArena found in parent hierarchy. " +
                "Add TrainingArena to the Arena Root GameObject.", this);
        }
        else
        {
            concreteArena.Initialise();
            _arena = concreteArena;
        }

        _observer = GetComponent<DroneObserver>();
        _observer.Initialise();

        _rewardEvaluator = GetComponent<DroneRewardManager>();
        _rewardEvaluator.Initialise();
        _rewardEvaluator.Configure(rewardProfile);

        _telemetry = GetComponent<DroneTelemetry>();
    }

    public override void OnEpisodeBegin()
    {
        ResetPhysics();
        _rewardEvaluator.ResetEpisode();

        if (_arena != null)
        {
            _maxEpisodeDistance = _arena.SetupEpisode(transform, _startPosition, _startRotation);
            _telemetry.OnNewEpisode(_arena.CurrentLessonIndex);
        }
        else
        {
            // Fallback: keeps the agent functional when no arena is wired up (quick scene tests).
            _maxEpisodeDistance = 10f;
            transform.localPosition = _startPosition;
            transform.localRotation = _startRotation;
            _telemetry.OnNewEpisode(0);
        }

        // Capture the actual post-reposition spawn location for the proximity reward baseline.
        _episodeStartPosition = transform.localPosition;

        if (target == null)
        {
            Debug.LogWarning($"[{name}] No target is set.", this);
            return;
        }

        // Calibrate the observer progress meters to this episode start distances.
        _observer.StartEpisode(transform.localPosition, target.localPosition);
    }

    /// <summary>
    /// Zeroes velocity / angular velocity and resets touchdown state.
    /// Called at the start of every episode before the arena repositions the drone.
    /// </summary>
    protected void ResetPhysics()
    {
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        _hasLanded      = false;
        _touchdownTimer = 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Use local-space target position -- all arena coordinates are local.
        Vector3 targetPos = target != null ? target.localPosition : _startPosition;
        _observer.Collect(sensor, targetPos);
    }

    protected virtual void FixedUpdate()
    {
        if (_hasLanded)
        {
            _touchdownTimer -= Time.fixedDeltaTime;
            if (_touchdownTimer <= 0f)
            {
                _telemetry.FlushEpisode(EpisodeOutcome.Success_TargetReached);
                EndEpisode();
                return;
            }
        }
    }

    protected virtual void OnCollisionEnter(Collision collision)
    {
        var result = _rewardEvaluator.EvaluateCollision(rewardProfile, collision.transform, target, _hasLanded);

        switch (result.Kind)
        {
            case DroneRewardManager.CollisionKind.Landing:
                _hasLanded      = true;
                _touchdownTimer = touchdownDelay;
                AddReward(result.Reward);
                break;

            case DroneRewardManager.CollisionKind.Crash:
                SetReward(result.Reward);
                _telemetry.FlushEpisode(EpisodeOutcome.Crash);
                EndEpisode();
                break;

            // CollisionKind.None: already landed -- ignore subsequent collisions.
        }
    }

    /// <summary>
    /// Delegates to DroneRewardManager for terminal checks and per-step rewards,
    /// then forwards results to DroneTelemetry.
    /// Call at the end of OnActionReceived after all forces have been applied.
    /// </summary>
    /// <remarks>
    /// Contract for subclasses: populate <see cref="_currentActionsBuffer"/> with
    /// the raw action value for each motor before calling this method.  The base
    /// class uses it to compute smoothness (difference from previous step) and,
    /// as a fallback when <paramref name="energyValues"/> is null, energy penalty.
    /// </remarks>
    /// <param name="currentActions">Continuous actions issued this step.</param>
    /// <param name="previousActions">Actions from the previous step; updated in-place.</param>
    /// <param name="energyValues">Per-motor normalised thrust for energy penalty. Falls back to currentActions when null.</param>
    /// <returns><c>true</c> if the episode was terminated; caller must return immediately.</returns>
    protected bool ApplyStandardRewards(float[] currentActions, float[] previousActions, float[] energyValues = null)
    {
        if (rewardProfile == null)
        {
            Debug.LogWarning($"[{name}] rewardProfile is not assigned -- skipping standard rewards.", this);
            return false;
        }

        Vector3 targetPos = DroneRewardMath.ResolveTargetPosition(target, _episodeStartPosition);

        var result = _rewardEvaluator.Evaluate(
            rewardProfile, targetPos, _episodeStartPosition,
            _maxEpisodeDistance,
            currentActions, previousActions, energyValues);

        if (result.IsTerminal)
        {
            SetReward(result.TerminalReward);
            _telemetry.FlushEpisode(result.Outcome);
            EndEpisode();
            return true;
        }

        System.Array.Copy(currentActions, previousActions,
            Mathf.Min(currentActions.Length, previousActions.Length));

        AddReward(result.StepRewards.Total);
        _telemetry.Record(result.StepRewards);
        return false;
    }
}