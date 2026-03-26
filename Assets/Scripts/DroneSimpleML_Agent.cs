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
    [SerializeField] private float maxEpisodeDistance = 20f;
    [SerializeField] private float reachedTargetDistance = 1f;

    [Header("Safety / Termination")]
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    [SerializeField] private float maxTiltAngle = 60f;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Keyboard keyboard;
    private float maxTiltDot;

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

        // Terminal: excessive tilt
        var tilt = DroneRewardHelper.CheckExcessiveTilt(transform.up, maxTiltDot);
        if (tilt.IsTerminal) { SetReward(tilt.Reward); EndEpisode(); return; }

        // --- Reward shaping (via shared helper) ---
        Vector3 targetPos = DroneRewardHelper.ResolveTargetPosition(target, startPosition);
        float distanceToTarget = Vector3.Distance(transform.localPosition, targetPos);

        AddReward(DroneRewardHelper.TiltPenalty(transform.up));
        AddReward(DroneRewardHelper.AngularVelocityPenalty(rb.angularVelocity.magnitude));

        // Terminal: reached the target
        var reached = DroneRewardHelper.CheckTargetReached(distanceToTarget, reachedTargetDistance);
        if (reached.IsTerminal) { SetReward(reached.Reward); EndEpisode(); return; }

        // Terminal: fell below ground
        var fallen = DroneRewardHelper.CheckFallen(transform.localPosition.y);
        if (fallen.IsTerminal) { SetReward(fallen.Reward); EndEpisode(); return; }

        // Terminal: flew too far away
        var tooFar = DroneRewardHelper.CheckTooFar(distanceToTarget, maxEpisodeDistance);
        if (tooFar.IsTerminal) { SetReward(tooFar.Reward); EndEpisode(); return; }
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
