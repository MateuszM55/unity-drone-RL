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
    [SerializeField] protected float linearDrag = 0.05f;
    [SerializeField] protected float angularDrag = 0.05f;

    [Header("Training")]
    [SerializeField] protected Transform target;
    [SerializeField] protected float maxEpisodeDistance = 20f;
    [SerializeField] protected float reachedTargetDistance = 1f;

    [Header("Safety / Termination")]
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    [SerializeField] protected float maxTiltAngle = 60f;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDrag;
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

    protected virtual void OnCollisionEnter(Collision collision)
    {
        // Ignore collision with the target (handled via distance check)
        if (target != null && collision.transform == target)
            return;

        // Collision with obstacle or ground: -1.0 penalty
        SetReward(-1.0f);
        EndEpisode();
    }
}
