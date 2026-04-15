using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Collects the standard drone observations (target direction, velocity,
/// orientation) into the ML-Agents sensor buffer.
///
/// Attach to the same GameObject as the drone agent.
///
/// OBSERVATION VECTOR (17 floats — body-local frame where applicable):
///   - Local unit direction to target        (3)  direction only, always [-1,1]
///   - Horizontal progress meter             (1)  currentXZ / startXZ, counts 1→0
///   - Vertical error meter                  (1)  verticalDiff / verticalBasis
///   - Drone velocity (local)                (3)
///   - Drone angular velocity (local)        (3)
///   - Drone orientation (forward)           (3)  world-frame attitude
///   - Drone orientation (up)                (3)  world-frame attitude
///
/// Call <see cref="StartEpisode"/> once at the beginning of each episode so the
/// two meters are calibrated to that specific flight's start distances.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneObserver : MonoBehaviour
{
    /// <summary>Minimum vertical basis (metres) used when the drone spawns level with the target.</summary>
    private const float MinVerticalBasis = 1f;

    private Rigidbody rb;
    private float startHorizontalDist;
    private float startVerticalDist;

    /// <summary>
    /// Caches component references. Call once from the agent's <c>Initialize</c>.
    /// </summary>
    public void Initialise()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Captures the horizontal and vertical basis distances at episode start.
    /// Must be called from <c>OnEpisodeBegin</c> after the drone and target have
    /// been repositioned for the new episode.
    /// </summary>
    /// <param name="dronePosition">Drone world/local position at spawn.</param>
    /// <param name="targetPosition">Target world/local position at spawn.</param>
    public void StartEpisode(Vector3 dronePosition, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - dronePosition;

        // XZ-plane distance — the "mission progress" basis
        startHorizontalDist = new Vector2(toTarget.x, toTarget.z).magnitude;

        // Vertical distance — clamped to a minimum so division is always safe
        startVerticalDist = Mathf.Max(Mathf.Abs(toTarget.y), MinVerticalBasis);
    }

    /// <summary>
    /// Writes the standard 17-float observation vector to <paramref name="sensor"/>.
    /// </summary>
    /// <param name="sensor">ML-Agents vector sensor to write into.</param>
    /// <param name="targetPosition">Current target position (local space).</param>
    public void Collect(VectorSensor sensor, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.localPosition;

        // Local unit direction to target (3) — pure steering signal
        Vector3 localVector = transform.InverseTransformDirection(toTarget);
        float dist = localVector.magnitude;
        Vector3 localDir = dist > 0.001f ? localVector / dist : Vector3.zero;
        sensor.AddObservation(localDir);

        // Horizontal progress meter (1): 1.0 at spawn, 0.0 on arrival
        float currentXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        float horizontalProgress = startHorizontalDist > 0.001f
            ? currentXZ / startHorizontalDist
            : 0f;
        sensor.AddObservation(horizontalProgress);

        // Vertical error meter (1): 0.0 = level with pad, ±1.0 = full start deficit
        float verticalError = toTarget.y / startVerticalDist;
        sensor.AddObservation(verticalError);

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
