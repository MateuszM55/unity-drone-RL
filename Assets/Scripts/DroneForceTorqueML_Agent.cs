using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Force & Torque ML-Agent — "Acro Mode" reinforcement learning controller.
/// Instead of commanding individual motors, the agent outputs high-level
/// force/torque commands which are applied directly to the rigid body.
///
/// ACTION SPACE (4 Continuous Actions in [-1, 1]):
///   Action 0 → Pitch torque   (+1 = nose down / forward, -1 = nose up / backward)
///   Action 1 → Yaw torque     (rotation around local Y-axis)
///   Action 2 → Roll torque    (rotation around local Z-axis)
///   Action 3 → Throttle       (vertical lift force, remapped to [0, 1])
///
/// OBSERVATION SPACE:
///   Vector observations (18 floats — body-local frame where applicable):
///     - Relative direction to target (local)  (3)
///     - Drone velocity (local)                (3)
///     - Drone angular velocity (local)        (3)
///     - Drone orientation (forward)           (3)
///     - Drone orientation (up)                (3)
///     - Drone orientation (right)             (3)
///   Fibonacci Sphere Sensor (via FibonacciSphereSensorComponent):
///     - Omnidirectional 3D ray casts (Fibonacci Lattice) measuring distance to nearby objects
///
/// REWARD FUNCTION:
///   - Target reached:       +1.0  (terminal)
///   - Velocity-alignment:   dot(velocity, directionToTarget) per step ("compass" shaping)
///   - Collision penalty:    -1.0  (terminal, obstacle or ground)
///   - Tilt penalty:         -1.0  (terminal, drone up > maxTiltAngle from world up)
///   - Time penalty:         -0.001 per step (encourage fast flight)
///
/// PHYSICS MODEL:
///   - Thrust is applied along the drone's local Up axis (tilted drone = tilted thrust).
///   - Torques are applied as relative torques (rotation around the drone's own axes).
///   - No individual motor simulation — single rigid body with net force + net torque.
///   - No autopilot / PID stabilization. The agent must learn to balance.
///
/// CONTROL ALLOCATION (conceptual):
///   The 4×4 allocation matrix that maps motor speeds² to [Fz, τ_φ, τ_θ, τ_ψ]
///   is effectively inverted: the agent directly outputs the desired Fz, τ_φ, τ_θ, τ_ψ
///   and the physics engine applies them. This removes the motor-mixing stage and
///   lets the agent reason in a more intuitive force/torque space.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(FibonacciSphereSensorComponent))]
public class DroneForceTorqueML_Agent : Agent
{
    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float linearDrag = 0.05f;
    [SerializeField] private float angularDrag = 0.05f;

    [Header("Force & Torque Limits")]
    [SerializeField] private float maxThrust = 40f;
    [SerializeField] private float maxPitchTorque = 1.0f;
    [SerializeField] private float maxRollTorque = 1.0f;
    [SerializeField] private float maxYawTorque = 0.15f;

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

        // --- Curriculum Learning ---
        var envParams = Academy.Instance.EnvironmentParameters;
        float targetSpawnDistance = envParams.GetWithDefault("target_spawn_distance", maxEpisodeDistance);
        reachedTargetDistance = envParams.GetWithDefault("precision_radius", reachedTargetDistance);

        // Randomize target placement
        if (target != null)
        {
            Vector3 randomOffset = Random.insideUnitSphere * targetSpawnDistance;
            randomOffset.y = Mathf.Clamp(randomOffset.y, 2f, 10f);
            target.localPosition = startPosition + randomOffset;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.localPosition : startPosition + Vector3.up * 3f;

        // Relative direction to target in body frame (3)
        sensor.AddObservation(transform.InverseTransformDirection(targetPos - transform.localPosition));

        // Velocity in body frame (3) — matches real IMU output
        sensor.AddObservation(transform.InverseTransformDirection(rb.linearVelocity));

        // Angular velocity in body frame (3) — matches real gyroscope output
        sensor.AddObservation(transform.InverseTransformDirection(rb.angularVelocity));

        // Orientation axes (9) — world-frame attitude for gravity awareness
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.up);
        sensor.AddObservation(transform.right);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float pitchInput    = actions.ContinuousActions[0];
        float yawInput      = actions.ContinuousActions[1];
        float rollInput     = actions.ContinuousActions[2];
        float throttleInput = actions.ContinuousActions[3];

        // --- THRUST (vertical force along local Up) ---
        // Remap throttle from [-1, 1] to [0, 1] (drones can't generate negative thrust)
        float normalizedThrust = (throttleInput + 1f) * 0.5f;
        float thrustForce = normalizedThrust * maxThrust;
        rb.AddForce(transform.up * thrustForce);

        // --- TORQUES (rotation around drone's own axes) ---
        float pitchTorque = pitchInput * maxPitchTorque;
        float yawTorque   = yawInput   * maxYawTorque;
        float rollTorque  = rollInput  * maxRollTorque;

        Vector3 localTorque = new Vector3(pitchTorque, yawTorque, rollTorque);
        rb.AddRelativeTorque(localTorque);

        // Terminal: excessive tilt
        var tilt = DroneRewardHelper.CheckExcessiveTilt(transform.up, maxTiltDot);
        if (tilt.IsTerminal) { SetReward(tilt.Reward); EndEpisode(); return; }

        // --- Reward shaping (via shared helper) ---
        Vector3 targetPos = DroneRewardHelper.ResolveTargetPosition(target, startPosition);
        Vector3 toTarget = targetPos - transform.localPosition;
        float distanceToTarget = toTarget.magnitude;

        AddReward(DroneRewardHelper.VelocityAlignmentReward(rb.linearVelocity, toTarget));
        AddReward(DroneRewardHelper.TimePenalty());

        // Terminal: reached the target
        var reached = DroneRewardHelper.CheckTargetReached(distanceToTarget, reachedTargetDistance);
        if (reached.IsTerminal) { SetReward(reached.Reward); EndEpisode(); return; }

        // Terminal: flew too far away
        var tooFar = DroneRewardHelper.CheckTooFar(distanceToTarget, maxEpisodeDistance);
        if (tooFar.IsTerminal) { SetReward(tooFar.Reward); EndEpisode(); return; }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Ignore collision with the target (handled via distance check)
        if (target != null && collision.transform == target)
            return;

        // Collision with obstacle or ground: -1.0 penalty
        SetReward(-1.0f);
        EndEpisode();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;

        if (keyboard == null) return;

        // Pitch (W/S → nose down / nose up)
        ca[0] = 0f;
        if (keyboard.wKey.isPressed) ca[0] =  1f;
        if (keyboard.sKey.isPressed) ca[0] = -1f;

        // Yaw (Q/E → turn left / turn right)
        ca[1] = 0f;
        if (keyboard.qKey.isPressed) ca[1] = -1f;
        if (keyboard.eKey.isPressed) ca[1] =  1f;

        // Roll (A/D → bank left / bank right)
        ca[2] = 0f;
        if (keyboard.aKey.isPressed) ca[2] =  1f;
        if (keyboard.dKey.isPressed) ca[2] = -1f;

        // Throttle (Space / LShift → up / down)
        ca[3] = -1f; // default: no thrust
        if (keyboard.spaceKey.isPressed)     ca[3] =  1f;
        if (keyboard.leftShiftKey.isPressed) ca[3] = -1f;
    }
}
