using UnityEngine;

/// <summary>
/// Owns the physical properties of the drone airframe: mass, gravity,
/// interpolation, and quadratic (velocity-squared) aerodynamic drag.
/// Drop this on any GameObject with a Rigidbody to get realistic
/// physics without relying on Unity's built-in linear damping.
///
/// Linear drag:  F_d = -½ · ρ · Cd · A · |v| · v
/// Angular drag:  τ_d = -k_ang · |ω| · ω
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
    [Tooltip("Lumped angular drag coefficient (combines Cd, area, and geometry for rotational resistance).")]
    [SerializeField] private float angularDragCoefficient = 0.005f;

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
        if (rb == null) rb = GetComponent<Rigidbody>();
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

        // --- Quadratic angular drag: τ_d = -k_ang · |ω| · ω ---
        Vector3 angVel = rb.angularVelocity;
        float angSpeed = angVel.magnitude;
        if (angSpeed > 1e-4f)
        {
            Vector3 angularDragTorque = -angularDragCoefficient * angSpeed * angVel;
            rb.AddTorque(angularDragTorque, ForceMode.Force);
        }
    }
}
