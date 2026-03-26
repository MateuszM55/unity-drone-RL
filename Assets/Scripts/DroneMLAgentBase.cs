using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for drone ML-Agents. Provides shared physics setup,
/// observation collection, episode reset, and collision handling.
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
[RequireComponent(typeof(Rigidbody))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Physics Settings")]
    [SerializeField] protected float mass = 1f;

    [Header("Quadratic Drag (replaces Unity's linear damping)")]
    [Tooltip("Air density in kg/m³ (1.225 at sea level).")]
    [SerializeField] protected float airDensity = 1.225f;
    [Tooltip("Drag coefficient of the airframe (0.5–0.7 for a typical drone).")]
    [SerializeField] protected float dragCoefficient = 0.5f;
    [Tooltip("Cross-sectional area of the drone in m².")]
    [SerializeField] protected float crossSectionalArea = 0.04f;
    [Tooltip("Lumped angular drag coefficient (combines Cd, area, and geometry for rotational resistance).")]
    [SerializeField] protected float angularDragCoefficient = 0.005f;

    [Header("Training")]
    [SerializeField] protected Transform target;
    [SerializeField] protected float maxEpisodeDistance = 20f;

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
    protected bool hasLanded;
    protected float touchdownTimer;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.useGravity = true;
        rb.centerOfMass = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        keyboard = Keyboard.current;
        maxTiltDot = Mathf.Cos(maxTiltAngle * Mathf.Deg2Rad);
    }

    public override void OnEpisodeBegin()
    {
        // Reset drone physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset touchdown state
        hasLanded = false;
        touchdownTimer = 0f;

        // Reset drone to start position
        transform.localPosition = startPosition;
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

        // --- Quadratic linear drag: F_d = -½ · ρ · Cd · A · |v| · v ---
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed > 1e-4f)
        {
            Vector3 linearDragForce = -0.5f * airDensity * dragCoefficient
                                      * crossSectionalArea * speed * velocity;
            rb.AddForce(linearDragForce, ForceMode.Force);
        }

        // --- Quadratic angular drag: τ_d = -k_ang · |ω| · ω ---
        Vector3 angVel = rb.angularVelocity;
        float angSpeed = angVel.magnitude;
        if (angSpeed > 1e-4f)
        {
            Vector3 angularDragTorque = -angularDragCoefficient * angSpeed * angVel;
            rb.AddTorque(angularDragTorque, ForceMode.Force);
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

        // Collision with obstacle or ground: -1.0 penalty
        SetReward(-1.0f);
        EndEpisode();
    }
}
