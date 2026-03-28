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

        // Physics configuration (mass, gravity, damping) is owned by DroneAerodynamics
        GetComponent<DroneAerodynamics>().InitialisePhysics();

        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        keyboard = Keyboard.current;
        maxTiltDot = Mathf.Cos(maxTiltAngle * Mathf.Deg2Rad);

        curriculumManager = GetComponent<DroneCurriculumManager>();
        curriculumManager.Initialise();
    }

    public override void OnEpisodeBegin()
    {
        ResetPhysics();
        maxEpisodeDistance = curriculumManager.SetupEpisode(transform, startPosition, startRotation);
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
                float speed = rb.linearVelocity.magnitude;
                AddReward(DroneRewardHelper.TouchdownReward(speed, maxSafeTouchdownSpeed));
            }
            return;
        }

        // Collision with obstacle or ground
        SetReward(DroneRewardHelper.ObstaclePenalty);
        EndEpisode();
    }
}
