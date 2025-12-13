using UnityEngine;
using UnityEngine.InputSystem;

public class DroneController : MonoBehaviour
{
    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float drag = 2f;
    [SerializeField] private float angularDrag = 3f;

    [Header("Flight Parameters")]
    [SerializeField] private float liftForce = 15f;
    [SerializeField] private float maxTilt = 45f;
    [SerializeField] private float tiltSpeed = 2f;
    [SerializeField] private float yawSpeed = 100f;
    [SerializeField] private float stabilizationSpeed = 3f;

    [Header("Input Sensitivity")]
    [SerializeField] private float throttleSensitivity = 5f;
    [SerializeField] private float pitchRollSensitivity = 1f;

    private Rigidbody rb;
    private float currentThrottle = 0f;
    private float targetYaw = 0f;

    private Keyboard keyboard;

    void Start()
    {
        // Setup or get Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        // Configure Rigidbody for realistic drone physics
        rb.mass = mass;
        rb.linearDamping = drag;
        rb.angularDamping = angularDrag;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.None;

        // Get keyboard reference
        keyboard = Keyboard.current;
    }

    void Update()
    {
        HandleInput();
    }

    void FixedUpdate()
    {
        ApplyLift();
        ApplyRotation();
    }

    private void HandleInput()
    {
        if (keyboard == null)
            return;

        // Throttle Control (Space to increase, Left Shift to decrease)
        if (keyboard.spaceKey.isPressed)
        {
            currentThrottle += throttleSensitivity * Time.deltaTime;
        }
        else if (keyboard.leftShiftKey.isPressed)
        {
            currentThrottle -= throttleSensitivity * Time.deltaTime;
        }

        // Clamp throttle between 0 and 2 (where 1.0 approximately hovers)
        currentThrottle = Mathf.Clamp(currentThrottle, 0f, 2f);

        // Yaw Control (Q/E for rotation around Y-axis)
        if (keyboard.qKey.isPressed)
        {
            targetYaw -= yawSpeed * Time.deltaTime;
        }
        if (keyboard.eKey.isPressed)
        {
            targetYaw += yawSpeed * Time.deltaTime;
        }
    }

    private void ApplyLift()
    {
        // Apply upward force relative to drone's orientation
        // This force counters gravity and provides lift
        Vector3 lift = transform.up * (liftForce * currentThrottle);
        rb.AddForce(lift, ForceMode.Force);
    }

    private void ApplyRotation()
    {
        if (keyboard == null)
            return;

        // Get Pitch and Roll input (WASD)
        float pitchInput = 0f;
        float rollInput = 0f;

        if (keyboard.wKey.isPressed)
            pitchInput = 1f; // Tilt forward (nose down)
        if (keyboard.sKey.isPressed)
            pitchInput = -1f;  // Tilt backward (nose up)
        if (keyboard.aKey.isPressed)
            rollInput = 1f;  // Tilt left
        if (keyboard.dKey.isPressed)
            rollInput = -1f;   // Tilt right

        // Apply sensitivity
        pitchInput *= pitchRollSensitivity;
        rollInput *= pitchRollSensitivity;

        // Calculate target rotation based on input
        float targetPitch = pitchInput * maxTilt;
        float targetRoll = rollInput * maxTilt;

        // Create target rotation (Yaw, Pitch, Roll in Euler angles)
        Quaternion targetRotation = Quaternion.Euler(targetPitch, targetYaw, targetRoll);

        // Smoothly interpolate to target rotation
        // This provides stability and prevents instant flipping
        float stabilization = (pitchInput == 0f && rollInput == 0f) ? stabilizationSpeed : tiltSpeed;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, stabilization * Time.fixedDeltaTime));
    }

    // Optional: Display current throttle in editor for debugging
    private void OnGUI()
    {
        if (Application.isEditor)
        {
            GUI.Label(new Rect(10, 10, 200, 20), $"Throttle: {currentThrottle:F2}");
            GUI.Label(new Rect(10, 30, 200, 20), $"Velocity: {rb.linearVelocity.magnitude:F2}");
            GUI.Label(new Rect(10, 50, 200, 20), $"Altitude: {transform.position.y:F2}");
        }
    }
}
