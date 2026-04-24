using UnityEngine;

/// <summary>
/// Owns the physical properties of the drone airframe: mass, gravity,
/// interpolation, and quadratic (velocity-squared) aerodynamic drag.
/// Drop this on any GameObject with a Rigidbody to get realistic
/// physics without relying on Unity's built-in linear damping.
///
/// Linear drag:   F_d   = -½ · ρ · Cd · A · |v| · v
/// Angular drag:  τ_d_i = -k_i · |ω_i| · ω_i   (per local axis, i ∈ {x,y,z})
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneAerodynamics : MonoBehaviour
{
    [Header("Rigidbody")]
    [SerializeField] private float mass = 1f;

    [Header("Quadratic Drag")]
    [Tooltip("Air density in kg/m³ (1.225 at sea level).")]
    [SerializeField] private float airDensity = 1.225f;
    [Tooltip("Drag coefficient of the airframe (0.5–0.7 for a typical drone).")]
    [SerializeField] private float dragCoefficient = 0.5f;
    [Tooltip("Cross-sectional area of the drone in m².")]
    [SerializeField] private float crossSectionalArea = 0.04f;
    [Tooltip("Per-axis angular drag coefficients in local space (x = Pitch, y = Yaw, z = Roll).\n" +
             "Yaw resistance is typically higher than Pitch/Roll on a quadcopter due to arm geometry.")]
    [SerializeField] private Vector3 angularDragCoefficients = new Vector3(0.005f, 0.01f, 0.005f);

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Configures the <see cref="Rigidbody"/> for realistic drone physics:
    /// sets mass, enables gravity, centres the CoM, enables interpolation,
    /// and zeroes Unity's built-in damping so this component is the sole
    /// source of aerodynamic drag.
    /// Call once during initialisation (e.g. from an Agent's <c>Initialize</c>).
    /// </summary>
    public void InitialisePhysics()
    {
        rb.mass = mass;
        rb.useGravity = true;
        rb.centerOfMass = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
    }

    private void FixedUpdate()
    {
        // --- Quadratic linear drag: F_d = -½ · ρ · Cd · A · |v| · v ---
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed > 1e-4f)
        {
            Vector3 linearDragForce = -0.5f * airDensity * dragCoefficient
                                      * crossSectionalArea * speed * velocity;
            rb.AddForce(linearDragForce, ForceMode.Force);
        }

        // --- Quadratic angular drag: τ_d_i = -k_i · |ω_i| · ω_i  (per local axis) ---
        // Work in local space so x/z always mean Pitch/Roll and y always means Yaw.
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);
        if (localAngVel.magnitude > 1e-4f)
        {
            Vector3 localDragTorque = new Vector3(
                -angularDragCoefficients.x * Mathf.Abs(localAngVel.x) * localAngVel.x,
                -angularDragCoefficients.y * Mathf.Abs(localAngVel.y) * localAngVel.y,
                -angularDragCoefficients.z * Mathf.Abs(localAngVel.z) * localAngVel.z);
            rb.AddTorque(transform.TransformDirection(localDragTorque), ForceMode.Force);
        }
    }
}
