using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for drone ML-Agents. Provides shared physics setup,
/// observation collection, episode reset, and collision handling.
/// Aerodynamic drag is handled by the companion <see cref="DroneAerodynamics"/> component.
/// Obstacle generation is handled by the companion <see cref="PoissonObstacleGenerator"/> component.
/// Subclasses implement <see cref="Agent.OnActionReceived"/> and
/// <see cref="Agent.Heuristic"/> for their specific control schemes.
///
/// OBSERVATION SPACE (16 floats — body-local frame where applicable):
///   - Local unit direction to target        (3)  direction only, always [-1,1]
///   - Squashed distance to target           (1)  tanh(d/10), always [0,1)
///   - Drone velocity (local)                (3)
///   - Drone angular velocity (local)        (3)
///   - Drone orientation (forward)           (3)  world-frame attitude
///   - Drone orientation (up)                (3)  world-frame attitude
///   (right = cross(forward, up) — omitted, linearly dependent)
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
/// </summary>
public enum Lesson
{
    /// <summary>Drone spawns directly above the target — focus on hovering and landing.</summary>
    Landing = 0,
    /// <summary>Drone spawns at a random point on a circle around the target — focus on navigation.</summary>
    Navigation = 1,
    /// <summary>Drone spawns at a random point on a larger circle — longer-range navigation.</summary>
    FarNavigation = 2,
    /// <summary>Drone spawns far away with random obstacles placed between it and the target.</summary>
    Obstacles = 3
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneAerodynamics))]
[RequireComponent(typeof(PoissonObstacleGenerator))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Physics Settings")]
    [SerializeField] protected float mass = 1f;

    [Header("Training")]
    [SerializeField] protected Transform target;
    [Tooltip("Max distance from target before episode ends (Landing & Navigation lessons).")]
    [SerializeField] protected float nearMaxEpisodeDistance = 10f;
    [Tooltip("Max distance from target before episode ends (FarNavigation lesson).")]
    [SerializeField] protected float farMaxEpisodeDistance = 20f;

    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;
    [Tooltip("Reference speed (m/s) at which the landing reward halves. Lower = stricter.")]
    [SerializeField] protected float maxSafeTouchdownSpeed = 2f;

    [Header("Spawn / Curriculum")]
    [Tooltip("Height above the target at which the drone spawns.")]
    [SerializeField] protected float spawnHeight = 3f;
    [Tooltip("Spawn distance for the Navigation lesson.")]
    [SerializeField] protected float navigationSpawnDistance = 5f;
    [Tooltip("Spawn distance for the FarNavigation lesson.")]
    [SerializeField] protected float farNavigationSpawnDistance = 15f;

    [Header("Safety / Termination")]
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    [SerializeField] protected float maxTiltAngle = 60f;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;
    protected float maxEpisodeDistance;
    protected bool hasLanded;
    protected float touchdownTimer;
    protected PoissonObstacleGenerator obstacleGenerator;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = true;
        rb.centerOfMass = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Let DroneAerodynamics be the sole source of damping
        GetComponent<DroneAerodynamics>().InitialiseDamping();

        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        keyboard = Keyboard.current;
        maxTiltDot = Mathf.Cos(maxTiltAngle * Mathf.Deg2Rad);

        obstacleGenerator = GetComponent<PoissonObstacleGenerator>();
        obstacleGenerator.Initialise(transform.parent);
    }

    public override void OnEpisodeBegin()
    {
        // Reset drone physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset touchdown state
        hasLanded = false;
        touchdownTimer = 0f;

        // Remove obstacles from the previous episode
        obstacleGenerator.Clear();

        // Read current lesson from curriculum
        Lesson lesson = (Lesson)(int)Academy.Instance.EnvironmentParameters
            .GetWithDefault("lesson", 0f);

        // Adjust max episode distance per lesson
        maxEpisodeDistance = lesson == Lesson.FarNavigation || lesson == Lesson.Obstacles
            ? farMaxEpisodeDistance
            : nearMaxEpisodeDistance;

        Vector3 targetPos = target != null ? target.localPosition : startPosition;

        switch (lesson)
        {
            case Lesson.Landing:
                // Start directly above the target
                transform.localPosition = targetPos + Vector3.up * spawnHeight;
                break;

            case Lesson.Navigation:
            {
                // Start at a random point on a circle around the target
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * navigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.FarNavigation:
            {
                // Start at a random point on a larger circle
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.Obstacles:
            {
                // Start far away, same as FarNavigation
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;

                // Spawn random obstacles inside the max-distance circle
                obstacleGenerator.Generate(targetPos, transform.parent);
                break;
            }

            default:
                transform.localPosition = startPosition;
                break;
        }

        transform.localRotation = startRotation;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.localPosition : startPosition + Vector3.up * 3f;

        // Decompose target vector → local unit direction (3) + squashed distance (1)
        DroneRewardHelper.DecomposeTargetVector(
            transform, targetPos - transform.localPosition,
            out Vector3 localDir, out float squashedDist);
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
        // --- Touchdown countdown ---
        if (hasLanded)
        {
            touchdownTimer -= Time.fixedDeltaTime;
            if (touchdownTimer <= 0f)
            {
                EndEpisode();
                return;
            }
        }

        // Aerodynamic drag is now handled by DroneAerodynamics (FixedUpdate)
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
                float speed = rb.linearVelocity.magnitude;
                AddReward(DroneRewardHelper.TouchdownReward(speed, maxSafeTouchdownSpeed));
            }
            return;
        }

        // Collision with obstacle or ground: -1.0 penalty
        SetReward(-1.0f);
        EndEpisode();
    }
}
