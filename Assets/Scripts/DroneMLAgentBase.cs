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
/// Curriculum and spawn logic is handled by the companion <see cref="DroneCurriculumManager"/> component.
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
[RequireComponent(typeof(DroneCurriculumManager))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Physics Settings")]
    [SerializeField] protected float mass = 1f;

    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;
    [Tooltip("Reference speed (m/s) at which the landing reward halves. Lower = stricter.")]
    [SerializeField] protected float maxSafeTouchdownSpeed = 2f;

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
    protected DroneCurriculumManager curriculumManager;

    /// <summary>Convenience accessor — delegates to <see cref="DroneCurriculumManager.Target"/>.</summary>
    protected Transform target => curriculumManager.Target;

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

        curriculumManager = GetComponent<DroneCurriculumManager>();
        curriculumManager.Initialise();
    }

    public override void OnEpisodeBegin()
    {
        // Reset drone physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset touchdown state
        hasLanded = false;
        touchdownTimer = 0f;

        // Delegate spawn placement, obstacle management, and distance limits to the curriculum manager
        maxEpisodeDistance = curriculumManager.SetupEpisode(transform, startPosition, startRotation);
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
