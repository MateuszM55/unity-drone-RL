using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

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
/// ACTION MAPPING — continuous [0, 1]:
///   [-1, 1]  →  [0, 1] thrust  (linear, preserves gradients for PPO)
///
/// Rotor transforms are populated automatically by <see cref="DroneGenerator"/>.
/// </summary>
public class DroneSimpleML_Agent : DroneMLAgentBase
{
    // rotorTransforms, maxThrustPerMotor, _previousActions, and _currentActionsBuffer
    // are declared in DroneMLAgentBase and assigned by DroneGenerator.

    /// <summary>
    /// Extends base observations with the 4 previous motor actions so the
    /// network can compute thrust deltas instead of absolute values each
    /// step — eliminates oscillation and makes manoeuvres smoother.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        base.CollectObservations(sensor);          // 17 floats from base
        for (int i = 0; i < 4; i++)               // +4 previous actions
            sensor.AddObservation(_previousActions[i]);
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        ClearActionBuffers();
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4)
            return;

        // Apply force at each rotor position
        for (int i = 0; i < 4; i++)
        {
            // Continuous mapping: [-1, 1] → [0, 1] thrust (preserves gradients for PPO)
            float normalized = (actions.ContinuousActions[i] + 1f) * 0.5f;
            float motorForce = normalized * maxThrustPerMotor;
            rb.AddForceAtPosition(transform.up * motorForce, rotorTransforms[i].position);
        }

        for (int i = 0; i < 4; i++)
            _currentActionsBuffer[i] = actions.ContinuousActions[i];

        ApplyStandardRewards(_currentActionsBuffer, _previousActions);
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
