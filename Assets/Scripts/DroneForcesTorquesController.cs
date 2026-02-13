using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Forces & Torques drone controller - "Acro Mode" / "Rate Mode".
/// Direct physics control without stabilization (PID-free).
/// 
/// PHYSICS MODEL:
/// - Single rigid body with mass and drag
/// - Net force (thrust) + net torque applied to center of mass
/// - NO individual motor simulation
/// 
/// ACTION SPACE (4 Continuous Actions for ML-Agents):
///   Action 0 – Pitch    : torque around local X-axis (nose up/down)
///   Action 1 – Yaw      : torque around local Y-axis (turn left/right)
///   Action 2 – Roll     : torque around local Z-axis (bank left/right)
///   Action 3 – Throttle : vertical lift force (rescaled to [0,1])
/// 
/// CONTROL LOGIC:
/// - Throttle: Applied as force along local Up axis (tilted drone = tilted thrust)
/// - Torques: Applied as relative torques (rotation around drone's own axes)
/// - NO autopilot: Drone maintains orientation until agent changes it (Newton's 1st Law)
/// </summary>
public class DroneForcesTorquesController : MonoBehaviour
{
    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float linearDrag = 2f;
    [SerializeField] private float angularDrag = 8f;

    [Header("Force & Torque Limits")]
    [SerializeField] private float maxThrust = 20f;
    [SerializeField] private float maxPitchTorque = 5f;
    [SerializeField] private float maxRollTorque = 5f;
    [SerializeField] private float maxYawTorque = 3f;

    [Header("Input Sensitivity (keyboard mode)")]
    [SerializeField] private float throttleSensitivity = 0.5f;

    // --- public API for agents / neural networks ---
    // Agent actions in [-1, 1]; throttle will be remapped to [0, 1]
    [HideInInspector] public float pitchInput;
    [HideInInspector] public float yawInput;
    [HideInInspector] public float rollInput;
    [HideInInspector] public float throttleInput;

    private Rigidbody rb;
    private float normalizedThrottle;
    private Keyboard keyboard;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = mass;
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDrag;
        rb.useGravity = true;
        rb.centerOfMass = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        normalizedThrottle = 0f;
        keyboard = Keyboard.current;
    }

    void Update()
    {
        HandleKeyboardInput();
    }

    void FixedUpdate()
    {
        ApplyForcesAndTorques();
    }

    /// <summary>
    /// ML-Agents API: Set all 4 action channels at once.
    /// Call this from your Agent's OnActionReceived(ActionBuffers).
    /// 
    /// Action order matches ML-Agents convention:
    ///   actions[0] → Pitch  (rotation around X)
    ///   actions[1] → Yaw    (rotation around Y)
    ///   actions[2] → Roll   (rotation around Z)
    ///   actions[3] → Throttle (vertical force)
    /// 
    /// All inputs should be in [-1, 1]; throttle will be remapped to [0, 1].
    /// </summary>
    public void SetActions(float pitch, float yaw, float roll, float throttle)
    {
        pitchInput    = Mathf.Clamp(pitch,    -1f, 1f);
        yawInput      = Mathf.Clamp(yaw,      -1f, 1f);
        rollInput     = Mathf.Clamp(roll,     -1f, 1f);
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
    }

    private void HandleKeyboardInput()
    {
        if (keyboard == null) return;

        // Throttle: ramp up/down and hold (already in [0,1] range for keyboard)
        float vertInput = 0f;
        if (keyboard.spaceKey.isPressed)     vertInput =  1f;
        if (keyboard.leftShiftKey.isPressed) vertInput = -1f;
        normalizedThrottle += vertInput * throttleSensitivity * Time.deltaTime;
        normalizedThrottle = Mathf.Clamp(normalizedThrottle, 0f, 1f);
        // Convert [0,1] to [-1,1] for consistent internal representation
        throttleInput = normalizedThrottle * 2f - 1f;

        // Pitch (nose up/down)
        pitchInput = 0f;
        if (keyboard.wKey.isPressed) pitchInput = -1f;
        if (keyboard.sKey.isPressed) pitchInput =  1f;

        // Roll (bank left/right)
        rollInput = 0f;
        if (keyboard.aKey.isPressed) rollInput =  1f;
        if (keyboard.dKey.isPressed) rollInput = -1f;

        // Yaw (turn left/right)
        yawInput = 0f;
        if (keyboard.qKey.isPressed) yawInput = -1f;
        if (keyboard.eKey.isPressed) yawInput =  1f;
    }

    private void ApplyForcesAndTorques()
    {
        // STEP A: Process Throttle (Vertical Force)
        // Remap from [-1, 1] to [0, 1] (drones can't generate negative thrust)
        float normalizedThrust = (throttleInput + 1f) * 0.5f;
        float thrustForce = normalizedThrust * maxThrust;
        
        // CRUCIAL: Apply force relative to drone's local Up axis
        // If drone tilts forward, thrust tilts with it → forward movement
        rb.AddForce(transform.up * thrustForce);

        // STEP B: Process Torques (Rotational Forces)
        float pitchTorque = pitchInput * maxPitchTorque;
        float yawTorque   = yawInput   * maxYawTorque;
        float rollTorque  = rollInput  * maxRollTorque;

        // CRUCIAL: Apply as relative torques (rotation around drone's own axes)
        Vector3 localTorque = new Vector3(pitchTorque, yawTorque, rollTorque);
        rb.AddRelativeTorque(localTorque);

        // STEP C: No Autopilot
        // NO stabilization code here — drone maintains whatever orientation
        // the agent leaves it in (Newton's 1st Law + drag)

        // Debug visualization
        Debug.DrawRay(transform.position, transform.up * (thrustForce * 0.05f), Color.green);
    }

    private void OnGUI()
    {
        if (!Application.isEditor) return;

        float normalizedThrust = (throttleInput + 1f) * 0.5f;
        
        GUI.Box(new Rect(10, 10, 220, 160), "Drone Acro Mode");
        GUI.Label(new Rect(20, 30, 200, 20), $"Pitch:    {pitchInput:F2}");
        GUI.Label(new Rect(20, 50, 200, 20), $"Yaw:      {yawInput:F2}");
        GUI.Label(new Rect(20, 70, 200, 20), $"Roll:     {rollInput:F2}");
        GUI.Label(new Rect(20, 90, 200, 20), $"Throttle: {throttleInput:F2} → {normalizedThrust:F2}");
        GUI.Label(new Rect(20, 110, 200, 20), $"Alt:      {transform.position.y:F1}m");
        GUI.Label(new Rect(20, 130, 200, 20), $"Angular Vel: {rb.angularVelocity.magnitude:F2}");
    }
}
