using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Incremental drone ML-Agent that models ESC and motor spool-up inertia.
/// Instead of setting absolute thrust, the network outputs the rate of change
/// (delta) of each rotor thrust per physics step.
///
/// ACTION SPACE (4 Continuous Actions in [-1, 1]):
///   Action 0-3 -- delta thrust rate for motors FL, FR, RL, RR
///
/// OBSERVATION SPACE (25 floats):
///   From DroneObserver base (17 floats):
///     [0-2]  Local unit direction to target (body frame)
///     [3]    Signed horizontal progress  (0 at spawn, ~+1 at target, negative when drifting away)
///     [4]    Vertical error meter
///     [5-7]  Linear velocity (body frame)
///     [8-10] Angular velocity (body frame)
///     [11-13] Orientation forward (world frame)
///     [14-16] Orientation up (world frame)
///   Proprioception -- interleaved per motor (8 floats):
///     [17] Motor 0 normalised thrust, [18] Motor 0 previous delta
///     [19] Motor 1 normalised thrust, [20] Motor 1 previous delta
///     [21] Motor 2 normalised thrust, [22] Motor 2 previous delta
///     [23] Motor 3 normalised thrust, [24] Motor 3 previous delta
/// </summary>
public class DroneMotorInertiaAgent : DroneMLAgentBase
{
    [Header("Realistic Motor Physics")]
    [Tooltip("Time in seconds for a motor to spool up from 0 to 100% thrust.")]
    [SerializeField] private float timeToMaxThrust = 0.1f;

    private readonly float[] _currentThrusts    = new float[4];

    private Keyboard _keyboard;

    public override void Initialize()
    {
        base.Initialize();
        _keyboard = Keyboard.current;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        base.CollectObservations(sensor); // 17 floats from DroneObserver

        // Proprioception: interleaved normalised thrust + previous delta per motor (8 floats).
        // Layout: [normalizedThrust_i, previousDelta_i] for i in 0..3
        for (int i = 0; i < 4; i++)
        {
            sensor.AddObservation(_currentThrusts[i] / maxThrustPerMotor); // [0, 1]
            sensor.AddObservation(_previousActions[i]);                    // [-1, 1]
        }
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        System.Array.Clear(_currentThrusts, 0, _currentThrusts.Length);
        ClearActionBuffers();
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4) return;

        // Compute per-step max thrust change from the spool-up model.
        // Computed here (not cached) so it stays correct if fixedDeltaTime changes.
        float maxThrustChangePerStep = maxThrustPerMotor * (Time.fixedDeltaTime / timeToMaxThrust);

        for (int i = 0; i < 4; i++)
        {
            if (rotorTransforms[i] == null)
            {
                Debug.LogWarning($"[{name}] rotorTransforms[{i}] is null -- run DroneGenerator to rebuild the model.", this);
                return;
            }

            float requestedChange = actions.ContinuousActions[i] * maxThrustChangePerStep;

            // Integrate delta and clamp to physical limits [0, maxThrust].
            _currentThrusts[i] = Mathf.Clamp(_currentThrusts[i] + requestedChange, 0f, maxThrustPerMotor);

            rb.AddForceAtPosition(transform.up * _currentThrusts[i], rotorTransforms[i].position);

            _previousActions[i] = actions.ContinuousActions[i];
        }

        ApplyStandardRewards();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        if (_keyboard == null) return;

        var ca = actionsOut.ContinuousActions;
        // Positive = spool up, negative = spool down, zero = hold current thrust.
        ca[0] = _keyboard.digit1Key.isPressed ? 1f : (_keyboard.qKey.isPressed ? -1f : 0f);
        ca[1] = _keyboard.digit2Key.isPressed ? 1f : (_keyboard.wKey.isPressed ? -1f : 0f);
        ca[2] = _keyboard.digit3Key.isPressed ? 1f : (_keyboard.eKey.isPressed ? -1f : 0f);
        ca[3] = _keyboard.digit4Key.isPressed ? 1f : (_keyboard.rKey.isPressed ? -1f : 0f);
    }
}