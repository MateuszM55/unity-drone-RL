using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Collects the standard drone observations (target direction, velocity,
/// orientation) into the ML-Agents sensor buffer.
///
/// Attach to the same GameObject as the drone agent.
///
/// OBSERVATION VECTOR (17 floats):
///   [0-2]  Local unit direction to target (body frame)     always in [-1,1]
///   [3]    Horizontal progress meter                       1.0 at spawn, 0.0 at target
///                                                          (currentXZ / startXZ)
///   [4]    Vertical error meter                            0 = level with pad,
///                                                          positive = above pad
///   [5-7]  Drone linear velocity (body frame)
///   [8-10] Drone angular velocity (body frame)
///   [11-13] Drone forward axis (world frame)               attitude / gravity awareness
///   [14-16] Drone up axis (world frame)                    attitude / gravity awareness
///
/// Call StartEpisode once at the beginning of each episode so the progress
/// meters are calibrated to that episode's spawn distances.
///
/// Note: lessons that spawn the drone directly above the target (spawnRadius = 0
/// and spawnHeight > 0) make the horizontal progress meter meaningless -- the
/// drone already has zero horizontal distance to cover.  Validate lesson profiles
/// to avoid this configuration; LessonProfile.ValidateAndWarn will report it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneObserver : MonoBehaviour
{
    /// <summary>Minimum vertical basis (metres). Prevents division by zero when the drone spawns level with the target.</summary>
    private const float MinVerticalBasis = 1f;

    private Rigidbody _rb;
    private float _startHorizontalDist;
    private float _startVerticalDist;

    /// <summary>
    /// Caches component references. Call once from the agent's Initialize.
    /// </summary>
    public void Initialise()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Captures the horizontal and vertical basis distances at episode start.
    /// Must be called from OnEpisodeBegin after the drone and target have been
    /// repositioned for the new episode.
    /// </summary>
    /// <param name="dronePosition">Drone local position at spawn.</param>
    /// <param name="targetPosition">Target local position at spawn.</param>
    public void StartEpisode(Vector3 dronePosition, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - dronePosition;

        _startHorizontalDist = new Vector2(toTarget.x, toTarget.z).magnitude;

        if (_startHorizontalDist <= 0.001f)
        {
            Debug.LogWarning(
                $"[DroneObserver] Horizontal start distance is ~0 -- the drone is spawning " +
                "directly above the target (spawnRadius = 0). The horizontal progress meter " +
                "[3] will be meaningless this episode. Set spawnRadius > 0 in the LessonProfile.", this);
        }

        // Vertical distance clamped to a minimum so division is always safe.
        _startVerticalDist = Mathf.Max(Mathf.Abs(toTarget.y), MinVerticalBasis);
    }

    /// <summary>
    /// Writes the standard 17-float observation vector to <paramref name="sensor"/>.
    /// Velocity and angular velocity are transformed to body-local space.
    /// Orientation axes are world-frame for gravity awareness.
    /// </summary>
    /// <param name="sensor">ML-Agents vector sensor to write into.</param>
    /// <param name="targetPosition">
    /// Current target local position. Must be in the same coordinate space
    /// as the drone's <c>transform.localPosition</c>.
    /// </param>
    public void Collect(VectorSensor sensor, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.localPosition;

        // [0-2] Local unit direction to target (3) -- pure steering signal in body frame.
        Vector3 localVector = transform.InverseTransformDirection(toTarget);
        float dist = localVector.magnitude;
        Vector3 localDir = dist > 0.001f ? localVector / dist : Vector3.zero;
        sensor.AddObservation(localDir);

        // [3] Horizontal progress meter (1): 1.0 at spawn, 0.0 on arrival.
        // Clamped to [0, inf) -- the agent sees 0 when it reaches the target and a
        // positive value proportional to remaining horizontal distance while en route.
        float currentXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        float horizontalProgress = _startHorizontalDist > 0.001f
            ? currentXZ / _startHorizontalDist
            : 0f;
        sensor.AddObservation(horizontalProgress);

        // [4] Vertical error meter (1): 0 = level with pad, positive = above pad.
        float verticalError = toTarget.y / _startVerticalDist;
        sensor.AddObservation(verticalError);

        // [5-7] Linear velocity in body frame (3) -- onboard accelerometer / velocity axes.
        sensor.AddObservation(transform.InverseTransformDirection(_rb.linearVelocity));

        // [8-10] Angular velocity in body frame (3) -- matches onboard gyroscope axes.
        sensor.AddObservation(transform.InverseTransformDirection(_rb.angularVelocity));

        // [11-16] Orientation axes in world frame (6) -- encodes attitude and gravity direction.
        // Right axis omitted: linearly dependent (right = cross(forward, up)).
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.up);
    }
}