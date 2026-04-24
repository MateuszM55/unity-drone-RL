using UnityEngine;

/// <summary>
/// Owns the physical properties of the drone airframe: mass, gravity,
/// interpolation, and quadratic (velocity-squared) aerodynamic drag.
/// Drop this on any GameObject with a Rigidbody to get realistic
/// physics without relying on Unity's built-in linear damping.
///
/// Linear drag:   F_d     = -0.5 * rho * Cd * A * |v| * v
/// Angular drag:  tau_d_i = -k_i * |omega_i| * omega_i   (per local axis)
///   where local x = Pitch, local y = Yaw, local z = Roll
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneAerodynamics : MonoBehaviour
{
    [Header("Rigidbody")]
    [SerializeField] private float mass = 1f;

    [Header("Quadratic Drag")]
    [Tooltip("Air density in kg/m3 (1.225 at sea level).")]
    [SerializeField] private float airDensity = 1.225f;
    [Tooltip("Drag coefficient of the airframe (0.5-0.7 for a typical drone).")]
    [SerializeField] private float dragCoefficient = 0.5f;
    [Tooltip("Cross-sectional area of the drone in m2.")]
    [SerializeField] private float crossSectionalArea = 0.04f;
    [Tooltip("Per-axis angular drag coefficients in local space (x = Pitch, y = Yaw, z = Roll).\n" +
             "Yaw resistance is typically higher than Pitch/Roll on a quadcopter due to arm geometry.")]
    [SerializeField] private Vector3 angularDragCoefficients = new Vector3(0.005f, 0.01f, 0.005f);

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Configures the Rigidbody for realistic drone physics:
    /// sets mass, enables gravity, centres the CoM, enables interpolation,
    /// and zeroes Unity's built-in damping so this component is the sole
    /// source of aerodynamic drag.
    /// Call once during initialisation (e.g. from an Agent's Initialize).
    /// </summary>
    public void InitialisePhysics()
    {
        // Guard against the rare case where InitialisePhysics is called before Awake.
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        _rb.mass           = mass;
        _rb.useGravity     = true;
        _rb.centerOfMass   = Vector3.zero;
        _rb.interpolation  = RigidbodyInterpolation.Interpolate;
        _rb.linearDamping  = 0f;
        _rb.angularDamping = 0f;
    }

    private void FixedUpdate()
    {
        // --- Quadratic linear drag: F_d = -0.5 * rho * Cd * A * |v| * v ---
        Vector3 velocity = _rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed > 1e-4f)
        {
            Vector3 linearDragForce = -0.5f * airDensity * dragCoefficient
                                      * crossSectionalArea * speed * velocity;
            _rb.AddForce(linearDragForce, ForceMode.Force);
        }

        // --- Quadratic angular drag: tau_d_i = -k_i * |omega_i| * omega_i ---
        // Work in local space: x = Pitch, y = Yaw, z = Roll.
        Vector3 localAngVel = transform.InverseTransformDirection(_rb.angularVelocity);
        if (localAngVel.magnitude > 1e-4f)
        {
            Vector3 localDragTorque = new Vector3(
                -angularDragCoefficients.x * Mathf.Abs(localAngVel.x) * localAngVel.x,
                -angularDragCoefficients.y * Mathf.Abs(localAngVel.y) * localAngVel.y,
                -angularDragCoefficients.z * Mathf.Abs(localAngVel.z) * localAngVel.z);
            _rb.AddTorque(transform.TransformDirection(localDragTorque), ForceMode.Force);
        }
    }
}