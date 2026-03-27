using UnityEngine;

/// <summary>
/// Applies quadratic (velocity-squared) drag to a <see cref="Rigidbody"/>.
/// Drop this on any GameObject with a Rigidbody to get realistic air resistance
/// without relying on Unity's built-in linear damping.
///
/// Linear drag:  F_d = -½ · ρ · Cd · A · |v| · v
/// Angular drag:  τ_d = -k_ang · |ω| · ω
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneAerodynamics : MonoBehaviour
{
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
    /// Zeroes Unity's built-in linear and angular damping so this component
    /// is the sole source of aerodynamic drag.
    /// Call once during initialisation (e.g. from an Agent's <c>Initialize</c>).
    /// </summary>
    public void InitialiseDamping()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
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
