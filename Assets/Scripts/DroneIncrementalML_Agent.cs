using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Incremental drone ML-Agent that models ESC and motor spool-up inertia.
/// Instead of setting absolute thrust, the network outputs the *rate of change* (acceleration) of the rotors.
/// 
/// ACTION SPACE (4 Continuous Actions in [-1, 1]):
///   Action 0-3 → Rate of change for motors FL, FR, RL, RR
/// 
/// OBSERVATION SPACE (25 floats):
///   - Base observations from DroneMLAgentBase (17)
///   - Current normalized thrust of each motor (4)
///   - Previous action commands (4)
/// </summary>
public class DroneIncrementalML_Agent : DroneMLAgentBase
{
    [Header("Motor Setup")]
    [Tooltip("Assigned automatically by DroneGenerator.\nOrder: FL(0), FR(1), RL(2), RR(3)")]
    [SerializeField] private Transform[] rotorTransforms;

    [Header("Realistic Motor Physics")]
    [Tooltip("Absolute maximum thrust a single motor can produce in Newtons.")]
    [SerializeField] private float maxThrustPerMotor = 5.0f;

    [Tooltip("Time in seconds for a motor to spool up from 0 to 100% thrust.")]
    [SerializeField] private float timeToMaxThrust = 0.1f;

    private readonly float[] _currentThrusts = new float[4];
    private readonly float[] _previousActions = new float[4];
    private readonly float[] _currentActionsBuffer = new float[4];
    private readonly float[] _normalizedThrusts = new float[4];

    private float _maxThrustChangePerStep;

    public override void Initialize()
    {
        base.Initialize();

        // Calculate how much the thrust can physically change in a single FixedUpdate step
        // e.g., if maxThrust is 5, timeToMax is 0.1s, and fixedDeltaTime is 0.02s:
        // maxChange = 5 * (0.02 / 0.1) = 1.0 Newton per step.
        _maxThrustChangePerStep = maxThrustPerMotor * (Time.fixedDeltaTime / timeToMaxThrust);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        base.CollectObservations(sensor); // 17 floats from base

        // Proprioception: The agent MUST know its current RPM/Thrust state
        for (int i = 0; i < 4; i++)
        {
            sensor.AddObservation(_currentThrusts[i] / maxThrustPerMotor); // Normalized current thrust [0, 1] (4 floats)
            sensor.AddObservation(_previousActions[i]);                    // Previous delta commands [-1, 1] (4 floats)
        }
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        System.Array.Clear(_currentThrusts, 0, _currentThrusts.Length);
        System.Array.Clear(_previousActions, 0, _previousActions.Length);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4) return;

        // 1. INTEGRAL CONTROL — integrate delta commands into physical thrust
        for (int i = 0; i < 4; i++)
        {
            float requestedChange = actions.ContinuousActions[i] * _maxThrustChangePerStep;

            // Integrate and clamp to physical limits [0, maxThrust]
            _currentThrusts[i] = Mathf.Clamp(_currentThrusts[i] + requestedChange, 0f, maxThrustPerMotor);

            // Apply force
            rb.AddForceAtPosition(transform.up * _currentThrusts[i], rotorTransforms[i].position);
        }

        // 2. BUILD BUFFERS — raw delta commands (smoothness) and actual thrusts (energy)
        for (int i = 0; i < 4; i++)
        {
            _currentActionsBuffer[i] = actions.ContinuousActions[i];
            _normalizedThrusts[i]    = _currentThrusts[i] / maxThrustPerMotor;
        }

        // 3. STANDARD REWARDS — terminal checks, shaping, TensorBoard, debug strings
        ApplyStandardRewards(_currentActionsBuffer, _previousActions, _normalizedThrusts);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        if (keyboard == null) return;

        // Positive pushes thrust up, negative pushes thrust down. Zero holds current thrust.
        ca[0] = keyboard.digit1Key.isPressed ? 1f : (keyboard.qKey.isPressed ? -1f : 0f);
        ca[1] = keyboard.digit2Key.isPressed ? 1f : (keyboard.wKey.isPressed ? -1f : 0f);
        ca[2] = keyboard.digit3Key.isPressed ? 1f : (keyboard.eKey.isPressed ? -1f : 0f);
        ca[3] = keyboard.digit4Key.isPressed ? 1f : (keyboard.rKey.isPressed ? -1f : 0f);
    }
}