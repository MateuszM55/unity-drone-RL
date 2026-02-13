using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple drone ML-Agent that controls 4 motors individually.
/// Each motor applies an upward force at its rotor position (AddForceAtPosition).
///
/// ACTION SPACE (4 Continuous Actions in [-1, 1]):
///   Action 0 → Motor FL thrust
///   Action 1 → Motor FR thrust
///   Action 2 → Motor RL thrust
///   Action 3 → Motor RR thrust
///
/// OBSERVATION SPACE (18 floats):
///   - Relative position to target    (3)
///   - Drone velocity                 (3)
///   - Drone angular velocity         (3)
///   - Drone orientation (forward)    (3)
///   - Drone orientation (up)         (3)
///   - Drone orientation (right)      (3)
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
/// Rotor transforms are populated automatically by the generator.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneSimpleML_Agent : Agent
{
    [Header("Motor Setup")]
    [Tooltip("Assigned automatically by DroneGenerator.\n" +
             "Order: FL(0), FR(1), RL(2), RR(3)")]
    [SerializeField] private Transform[] rotorTransforms;

    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float linearDrag = 2f;
    [SerializeField] private float angularDrag = 8f;

    [Header("Motor Settings")]
    [SerializeField] private float maxThrustPerMotor = 15f;

    [Header("Training")]
    [SerializeField] private Transform target;
    [SerializeField] private float spawnRadius = 5f;
    [SerializeField] private float maxEpisodeDistance = 20f;
    [SerializeField] private float reachedTargetDistance = 1f;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Keyboard keyboard;

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

        // Relative position to target (3)
        sensor.AddObservation(targetPos - transform.localPosition);

        // Velocity (3)
        sensor.AddObservation(rb.linearVelocity);

        // Angular velocity (3)
        sensor.AddObservation(rb.angularVelocity);

        // Orientation axes (9) — lets the agent understand its attitude
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.up);
        sensor.AddObservation(transform.right);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4)
            return;

        // Apply force at each rotor position
        for (int i = 0; i < 4; i++)
        {
            // Map action from [-1, 1] to [0, 1] (motors can only push, not pull)
            float normalized = (actions.ContinuousActions[i] + 1f) * 0.5f;
            float motorForce = normalized * maxThrustPerMotor;
            Vector3 forceVector = transform.up * motorForce;
            rb.AddForceAtPosition(forceVector, rotorTransforms[i].position);
        }

        // --- Reward shaping ---
        Vector3 targetPos = target != null ? target.localPosition : startPosition + Vector3.up * 3f;
        float distanceToTarget = Vector3.Distance(transform.localPosition, targetPos);

        // Small continuous reward for being close to the target
        float proximityReward = 1f - Mathf.Clamp01(distanceToTarget / spawnRadius);
        AddReward(0.01f * proximityReward);

        // Penalise excessive tilt (encourage upright flight)
        float tiltPenalty = 1f - Vector3.Dot(transform.up, Vector3.up);
        AddReward(-0.005f * tiltPenalty);

        // Penalise excessive angular velocity (encourage smooth flight)
        AddReward(-0.001f * rb.angularVelocity.magnitude);

        // Reached the target
        if (distanceToTarget < reachedTargetDistance)
        {
            SetReward(1.0f);
            EndEpisode();
        }

        // Fell or flew too far away
        if (transform.localPosition.y < -0.5f || distanceToTarget > maxEpisodeDistance)
        {
            SetReward(-1.0f);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;

        if (keyboard == null) return;

        // Keys 1-4 apply full thrust to individual rotors (FL, FR, RL, RR)
        ca[0] = keyboard.digit1Key.isPressed ? 1f : -1f;
        ca[1] = keyboard.digit2Key.isPressed ? 1f : -1f;
        ca[2] = keyboard.digit3Key.isPressed ? 1f : -1f;
        ca[3] = keyboard.digit4Key.isPressed ? 1f : -1f;
    }
}
