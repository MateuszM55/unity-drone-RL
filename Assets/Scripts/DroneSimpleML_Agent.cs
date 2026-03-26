using Unity.MLAgents.Actuators;
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
/// Rotor transforms are populated automatically by <see cref="DroneGenerator"/>.
/// </summary>
public class DroneSimpleML_Agent : DroneMLAgentBase
{
    [Header("Motor Setup")]
    [Tooltip("Assigned automatically by DroneGenerator.\n" +
             "Order: FL(0), FR(1), RL(2), RR(3)")]
    [SerializeField] private Transform[] rotorTransforms;

    [Header("Motor Settings")]
    [SerializeField] private float maxThrustPerMotor = 15f;

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

        AddReward(DroneRewardHelper.ProximityReward(transform.localPosition, targetPos, startPosition));
        AddReward(DroneRewardHelper.TiltPenalty(transform.up));
        AddReward(DroneRewardHelper.AngularVelocityPenalty(rb.angularVelocity.magnitude));

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
