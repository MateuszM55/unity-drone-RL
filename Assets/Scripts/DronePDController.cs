using UnityEngine;
using UnityEngine.InputSystem;
/// OUTDATED: This agent is an early experiment
public class DronePDController : MonoBehaviour
{
    [Header("Motor Setup")]
    [Tooltip("Drag Transforms here in this specific order:\n" +
             "Element 0 -> Front Left\n" +
             "Element 1 -> Front Right\n" +
             "Element 2 -> Rear Left\n" +
             "Element 3 -> Rear Right")]
    [SerializeField] private Transform[] rotorTransforms;

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

    [Header("Physics Settings")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float linearDrag = 2f;
    [SerializeField] private float angularDrag = 8f;

    [Header("Attitude PD – Pitch & Roll")]
    [SerializeField] private float kp_pitch_roll = 3f;
    [SerializeField] private float kd_pitch_roll = 3.5f;

    [Header("Attitude PD – Yaw")]
    [SerializeField] private float kp_yaw = 1.5f;
    [SerializeField] private float kd_yaw = 3f;

    [Header("Altitude PD")]
    [SerializeField] private float kp_altitude = 3f;
    [SerializeField] private float kd_altitude = 3f;

    [Header("Flight Parameters")]
    [SerializeField] private float maxThrustPerMotor = 15f;
    [SerializeField] private float maxTiltAngle = 25f;
    [SerializeField] private float yawSpeed = 90f;
    [SerializeField] private float maxCorrectionMix = 0.2f;

    [Header("Input Sensitivity")]
    [SerializeField] private float climbSpeed = 3f;

    // Internal state
    private Rigidbody rb;
    private float hoverThrottle;
    private float targetAltitude;
    private float targetYaw;
    private float inputPitch;
    private float inputRoll;
    private float[] motorOutputs = new float[4];
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

        // Hover throttle: total gravity force split across 4 motors, normalized 0-1
        hoverThrottle = (mass * Mathf.Abs(Physics.gravity.y)) / (4f * maxThrustPerMotor);

        targetAltitude = transform.position.y;
        targetYaw = transform.eulerAngles.y;
        keyboard = Keyboard.current;
    }

    void Update()
    {
        HandleInput();
    }

    void FixedUpdate()
    {
        if (rotorTransforms == null || rotorTransforms.Length != 4)
            return;

        // 1. Altitude PD → base throttle
        float throttle = ComputeThrottle();

        // 2. Attitude PD → pitch / roll / yaw corrections (uses angular velocity for D term)
        Vector3 correction = ComputeAttitudeCorrection();

        // 3. Quad-X motor mixer → per-motor forces
        MixAndApplyMotors(throttle, correction);
    }

    private void HandleInput()
    {
        if (keyboard == null) return;

        // Throttle drives a target altitude instead of raw motor power
        float vertInput = 0f;
        if (keyboard.spaceKey.isPressed) vertInput = 1f;
        if (keyboard.leftShiftKey.isPressed) vertInput = -1f;
        targetAltitude += vertInput * climbSpeed * Time.deltaTime;
        targetAltitude = Mathf.Max(0f, targetAltitude);

        // Pitch & Roll (stick input, sampled every frame for responsiveness)
        inputPitch = 0f;
        inputRoll = 0f;
        if (keyboard.wKey.isPressed) inputPitch = -1f;  // Nose down (forward)
        if (keyboard.sKey.isPressed) inputPitch = 1f;   // Nose up (backward)
        if (keyboard.aKey.isPressed) inputRoll = 1f;    // Roll left
        if (keyboard.dKey.isPressed) inputRoll = -1f;   // Roll right

        // Yaw drives a target heading
        float yawInput = 0f;
        if (keyboard.qKey.isPressed) yawInput = -1f;
        if (keyboard.eKey.isPressed) yawInput = 1f;
        targetYaw += yawInput * yawSpeed * Time.deltaTime;
        targetYaw = NormalizeAngle(targetYaw);
    }

    // Altitude PD: returns a throttle value in [0,1]
    private float ComputeThrottle()
    {
        float altError = targetAltitude - transform.position.y;
        float vertVelocity = rb.linearVelocity.y;

        // PD on altitude error; scale relative to hover so corrections are proportional
        float correction = kp_altitude * altError - kd_altitude * vertVelocity;
        return Mathf.Clamp(hoverThrottle + correction * hoverThrottle, 0f, 1f);
    }

    // Attitude PD: uses rb.angularVelocity for the D term (gyro-based, no derivative kick)
    private Vector3 ComputeAttitudeCorrection()
    {
        // Invert pitch: Unity's +X rotation = nose down, but we need negative pitch error
        float currentPitch = -NormalizeAngle(transform.eulerAngles.x);
        float currentRoll = NormalizeAngle(transform.eulerAngles.z);
        float currentYaw = NormalizeAngle(transform.eulerAngles.y);

        float targetPitch = inputPitch * maxTiltAngle;
        float targetRoll = inputRoll * maxTiltAngle;

        // Angular velocity in local space — stable, noise-free derivative signal
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);

        // Pitch PD
        float pitchError = targetPitch - currentPitch;
        float pitchCmd = kp_pitch_roll * pitchError - kd_pitch_roll * localAngVel.x;

        // Roll PD
        float rollError = targetRoll - currentRoll;
        float rollCmd = kp_pitch_roll * rollError - kd_pitch_roll * localAngVel.z;

        // Yaw PD
        float yawError = NormalizeAngle(targetYaw - currentYaw);
        float yawCmd = kp_yaw * yawError - kd_yaw * localAngVel.y;

        return new Vector3(pitchCmd, yawCmd, rollCmd);
    }

    private void MixAndApplyMotors(float throttle, Vector3 correction)
    {
        // Normalize PD outputs into the ~0-1 throttle range and clamp to prevent flip
        float pitchMix = Mathf.Clamp(correction.x / maxTiltAngle, -maxCorrectionMix, maxCorrectionMix);
        float yawMix = Mathf.Clamp(correction.y / 180f, -maxCorrectionMix, maxCorrectionMix);
        float rollMix = Mathf.Clamp(correction.z / maxTiltAngle, -maxCorrectionMix, maxCorrectionMix);

        // CORRECTED Quad-X mixing
        // Motor 0: Front Left  (CW)  → Front(+Pitch), Left(-Roll),  CW(-Yaw)
        // Motor 1: Front Right (CCW) → Front(+Pitch), Right(+Roll), CCW(+Yaw)
        // Motor 2: Rear Left   (CCW) → Rear(-Pitch),  Left(-Roll),  CCW(+Yaw)
        // Motor 3: Rear Right  (CW)  → Rear(-Pitch),  Right(+Roll), CW(-Yaw)
        motorOutputs[0] = throttle + pitchMix - rollMix - yawMix; // FL
        motorOutputs[1] = throttle + pitchMix + rollMix + yawMix; // FR
        motorOutputs[2] = throttle - pitchMix - rollMix + yawMix; // RL
        motorOutputs[3] = throttle - pitchMix + rollMix - yawMix; // RR

        for (int i = 0; i < 4; i++)
        {
            float motorForce = Mathf.Clamp01(motorOutputs[i]) * maxThrustPerMotor;
            Vector3 forceVector = transform.up * motorForce;
            rb.AddForceAtPosition(forceVector, rotorTransforms[i].position);
            Debug.DrawRay(rotorTransforms[i].position, forceVector * 0.1f, Color.red);
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    private void OnGUI()
    {
        if (Application.isEditor)
        {
            GUI.Box(new Rect(10, 10, 200, 140), "Drone Stats");
            GUI.Label(new Rect(20, 30, 180, 20), $"Target Alt: {targetAltitude:F1}m");
            GUI.Label(new Rect(20, 50, 180, 20), $"Alt: {transform.position.y:F1}m");
            GUI.Label(new Rect(20, 70, 180, 20), $"FL: {motorOutputs[0]:F2} | FR: {motorOutputs[1]:F2}");
            GUI.Label(new Rect(20, 90, 180, 20), $"RL: {motorOutputs[2]:F2} | RR: {motorOutputs[3]:F2}");
            GUI.Label(new Rect(20, 110, 180, 20), $"Hover: {hoverThrottle * 100:F0}%");
        }
    }

    void OnDrawGizmos()
    {
        if (rotorTransforms == null || rotorTransforms.Length < 4) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.11f);

        Gizmos.color = Color.red;
        if (rotorTransforms[0]) Gizmos.DrawWireSphere(rotorTransforms[0].position, 0.05f);
        if (rotorTransforms[1]) Gizmos.DrawWireSphere(rotorTransforms[1].position, 0.05f);

        Gizmos.color = Color.blue;
        if (rotorTransforms[0] && rotorTransforms[3])
            Gizmos.DrawLine(rotorTransforms[0].position, rotorTransforms[3].position);
        if (rotorTransforms[1] && rotorTransforms[2])
            Gizmos.DrawLine(rotorTransforms[1].position, rotorTransforms[2].position);
    }
}