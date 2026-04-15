using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Collects the standard drone observations (target direction, velocity,
/// orientation) into the ML-Agents sensor buffer.
///
/// Attach to the same GameObject as the drone agent.
///
/// OBSERVATION VECTOR (16 floats — body-local frame where applicable):
///   - Local unit direction to target        (3)  direction only, always [-1,1]
///   - Squashed distance to target           (1)  tanh(d/norm), always [0,1)
///   - Drone velocity (local)                (3)
///   - Drone angular velocity (local)        (3)
///   - Drone orientation (forward)           (3)  world-frame attitude
///   - Drone orientation (up)                (3)  world-frame attitude
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneObserver : MonoBehaviour
{
    private Rigidbody rb;

    /// <summary>
    /// Caches component references. Call once from the agent's <c>Initialize</c>.
    /// </summary>
    public void Initialise()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Writes the standard 16-float observation vector to <paramref name="sensor"/>.
    /// </summary>
    /// <param name="sensor">ML-Agents vector sensor to write into.</param>
    /// <param name="targetPosition">Current target position (local space).</param>
    /// <param name="distanceNorm">Normalisation constant for tanh-compressed distance.</param>
    public void Collect(VectorSensor sensor, Vector3 targetPosition, float distanceNorm)
    {
        // Decompose target vector → local unit direction (3) + squashed distance (1)
        DroneRewardMath.DecomposeTargetVector(
            transform, targetPosition - transform.localPosition,
            out Vector3 localDir, out float squashedDist, distanceNorm);
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
}
