using UnityEngine;
using UnityEngine.InputSystem;

public class DronePhysicsController : MonoBehaviour
{
    [Header("Motor Setup")]
    [Tooltip("Order: FrontLeft, FrontRight, RearLeft, RearRight")]
    [SerializeField] private Transform[] rotorTransforms;

    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float linearDrag = 1f;
    [SerializeField] private float angularDrag = 2.5f;

    [Header("PID Control (Stability)")]
    // Proportional (P): How hard to pull back to target angle
    // Derivative (D): How much to dampen the oscillation
    [SerializeField] private float kp_pitch_roll = 1f;
    [SerializeField] private float kd_pitch_roll = 0.5f;
    [SerializeField] private float kp_yaw = 1f;
    [SerializeField] private float kd_yaw = 1.5f;

    [Header("Flight Parameters")]
    [SerializeField] private float maxThrustPerMotor = 20f; // Power of each motor
    [SerializeField] private float maxTiltAngle = 45f;
    [SerializeField] private float yawSpeed = 100f;

    [Header("Input Sensitivity")]
    [SerializeField] private float throttleSpeed = 2f;

    // Internal Variables
    private Rigidbody rb;
    private float currentThrottle = 0f;
    private float targetYaw = 0f;

    // PID Errors
    private float lastPitchError, lastRollError, lastYawError;

    [Header("Motor Setup")]
    [Tooltip("Drag Transforms here in this specific order:\n" +
             "Element 0 -> Front Left\n" +
             "Element 1 -> Front Right\n" +
             "Element 2 -> Rear Left\n" +
             "Element 3 -> Rear Right")]

    //  MAPPING VISUALIZATION:
    //  
    //      (0) FL   ^   FR (1)
    //            \  |  /
    //             \ | /
    //              \|/
    //          -----------
    //              /|\
    //             / | \
    //            /  |  \
    //      (2) RL       RR (3)
    //
    private float[] motorInputs = new float[4];

    private Keyboard keyboard;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        // Physics Setup
        rb.mass = mass;
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDrag;
        rb.useGravity = true;

        // Auto-calculate hover throttle (approximate)
        // Force needed = Mass * Gravity. Divided by 4 motors.
        float forceNeeded = (mass * Mathf.Abs(Physics.gravity.y));
        float forcePerMotor = forceNeeded / 4f;
        // Set initial throttle to hover level (normalized 0-1)
        currentThrottle = forcePerMotor / maxThrustPerMotor;

        keyboard = Keyboard.current;

        // Initialize Target Yaw to current facing
        targetYaw = transform.eulerAngles.y;
    }

    void Update()
    {
        HandleInput();
    }

    void FixedUpdate()
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4)
            return;

        // 1. Calculate Target Angles
        Vector3 targetOrientation = CalculateTargetOrientation();

        // 2. PID Control (Calculate necessary torque corrections)
        Vector3 torqueCorrection = CalculatePID(targetOrientation);

        // 3. The Mixer (Convert Torque + Throttle -> Individual Motor Forces)
        ApplyMotorMixer(torqueCorrection);
    }

    private void HandleInput()
    {
        if (keyboard == null) return;

        // Throttle Input
        if (keyboard.spaceKey.isPressed) currentThrottle += throttleSpeed * Time.deltaTime;
        if (keyboard.leftShiftKey.isPressed) currentThrottle -= throttleSpeed * Time.deltaTime;
        currentThrottle = Mathf.Clamp(currentThrottle, 0f, 1f); // 0% to 100% power

        // Yaw Input (Modify the target yaw)
        if (keyboard.qKey.isPressed) targetYaw -= yawSpeed * Time.deltaTime;
        if (keyboard.eKey.isPressed) targetYaw += yawSpeed * Time.deltaTime;
    }

    private Vector3 CalculateTargetOrientation()
    {
        float pitchInput = 0f;
        float rollInput = 0f;

        if (keyboard.wKey.isPressed) pitchInput = 1f;  // Nose Down
        if (keyboard.sKey.isPressed) pitchInput = -1f; // Nose Up
        if (keyboard.aKey.isPressed) rollInput = 1f;   // Roll Left
        if (keyboard.dKey.isPressed) rollInput = -1f;  // Roll Right

        float targetPitch = pitchInput * maxTiltAngle;
        float targetRoll = rollInput * maxTiltAngle;

        return new Vector3(targetPitch, targetYaw, targetRoll);
    }

    private Vector3 CalculatePID(Vector3 targetEuler)
    {
        // Get current rotation in Euler angles (handling Unity's 360 wrap-around)
        Vector3 currentEuler = transform.eulerAngles;

        // Helper to normalize angles to -180 to 180 range for correct math
        float NormalizeAngle(float angle) => angle > 180 ? angle - 360 : angle;

        float currentPitch = NormalizeAngle(currentEuler.x);
        float currentRoll = NormalizeAngle(currentEuler.z);
        float currentYaw = NormalizeAngle(currentEuler.y); // Note: Yaw is global

        // --- PITCH PID ---
        float pitchError = targetEuler.x - currentPitch;
        float pitchCorrection = (pitchError * kp_pitch_roll) - ((pitchError - lastPitchError) / Time.fixedDeltaTime * kd_pitch_roll);
        lastPitchError = pitchError;

        // --- ROLL PID ---
        float rollError = targetEuler.z - currentRoll;
        float rollCorrection = (rollError * kp_pitch_roll) - ((rollError - lastRollError) / Time.fixedDeltaTime * kd_pitch_roll);
        lastRollError = rollError;

        // --- YAW PID ---
        // For Yaw, we just want to stabilize angular velocity mostly, but here we track a target heading
        float yawError = targetEuler.y - currentYaw;
        // Handle wrapping (if error is > 180, go the other way)
        if (yawError > 180) yawError -= 360;
        if (yawError < -180) yawError += 360;

        float yawCorrection = (yawError * kp_yaw) - ((yawError - lastYawError) / Time.fixedDeltaTime * kd_yaw);
        lastYawError = yawError;

        return new Vector3(pitchCorrection, yawCorrection, rollCorrection);
    }

    private void ApplyMotorMixer(Vector3 correction)
    {
        // PID Outputs
        float pitchCmd = correction.x;
        float yawCmd = correction.y;
        float rollCmd = correction.z;

        // Base Throttle
        float t = currentThrottle;

        // Quad X Configuration Mixing Logic
        // Motor 0: Front Left  (CW)  -> +Pitch +Roll +Yaw
        // Motor 1: Front Right (CCW) -> +Pitch -Roll -Yaw
        // Motor 2: Rear Left   (CCW) -> -Pitch +Roll -Yaw
        // Motor 3: Rear Right  (CW)  -> -Pitch -Roll +Yaw

        // Note on Yaw: CW motors create CCW torque. To turn Right (CW), we spin CCW motors (1 & 2) faster.
        // The signs below depend on your specific motor direction setup, this is standard "Quad X":

        motorInputs[0] = t + pitchCmd + rollCmd + yawCmd; // FL
        motorInputs[1] = t + pitchCmd - rollCmd - yawCmd; // FR 
        motorInputs[2] = t - pitchCmd + rollCmd - yawCmd; // RL 
        motorInputs[3] = t - pitchCmd - rollCmd + yawCmd; // RR 

        // Apply Forces
        for (int i = 0; i < 4; i++)
        {
            // Clamp motor values (cannot spin backwards, cannot exceed max power)
            float motorForce = Mathf.Clamp(motorInputs[i], 0f, 1f) * maxThrustPerMotor;

            // Apply force at the specific rotor position upwards relative to the drone
            Vector3 forceVector = transform.up * motorForce;
            rb.AddForceAtPosition(forceVector, rotorTransforms[i].position);

            // Debug Visualization
            Debug.DrawRay(rotorTransforms[i].position, forceVector * 0.1f, Color.red);
        }
    }

    private void OnGUI()
    {
        if (Application.isEditor)
        {
            GUI.Box(new Rect(10, 10, 200, 120), "Drone Stats");
            GUI.Label(new Rect(20, 30, 180, 20), $"Throttle: {currentThrottle * 100:F0}%");
            GUI.Label(new Rect(20, 50, 180, 20), $"FL: {motorInputs[0]:F2} | FR: {motorInputs[1]:F2}");
            GUI.Label(new Rect(20, 70, 180, 20), $"RL: {motorInputs[2]:F2} | RR: {motorInputs[3]:F2}");
            GUI.Label(new Rect(20, 90, 180, 20), $"Alt: {transform.position.y:F1}m");
        }
    }

    void OnDrawGizmos()
    {
        if (rotorTransforms == null || rotorTransforms.Length < 4) return;

        // Draw the Center of Mass
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.1f);

        // Draw the Rotors
        Gizmos.color = Color.red;
        // FL
        if (rotorTransforms[0]) Gizmos.DrawWireSphere(rotorTransforms[0].position, 0.05f);
        // FR
        if (rotorTransforms[1]) Gizmos.DrawWireSphere(rotorTransforms[1].position, 0.05f);

        // Draw lines connecting diagonals to check centering
        Gizmos.color = Color.blue;
        if (rotorTransforms[0] && rotorTransforms[3])
            Gizmos.DrawLine(rotorTransforms[0].position, rotorTransforms[3].position);
        if (rotorTransforms[1] && rotorTransforms[2])
            Gizmos.DrawLine(rotorTransforms[1].position, rotorTransforms[2].position);
    }
}