using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A <see cref="SensorComponent"/> that creates a <see cref="SixAxisSensor"/>.
/// Provides up to 6 orthogonal proximity rays (Up, Down, Left, Right, Back, Forward) using
/// volumetric SphereCasts and linear inverse-distance observations.
/// Individual rays can be toggled via the Inspector.
///
/// Mimics ultrasonic / infrared proximity sensors on commercial drones.
/// Attach to the same GameObject as the drone agent.
/// </summary>
[AddComponentMenu("ML Agents/Six Axis Sensor")]
public class SixAxisSensorComponent : SensorComponent, IDisposable
{
    // ──────────────────────────────────────────────
    //  Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sensor Identity")]
    [SerializeField]
    string m_SensorName = "SixAxisSensor";

    [Header("Ray Configuration")]
    [SerializeField, Range(1f, 1000f)]
    [Tooltip("Maximum length of each sphere-cast ray.")]
    float m_RayLength = 20f;

    [SerializeField, Range(0.01f, 2f)]
    [Tooltip("Radius of the SphereCast (≈ drone half-width). Prevents thin objects slipping between rays.")]
    float m_SphereRadius = 0.2f;

    [SerializeField]
    [Tooltip("Physics layers the rays can hit.")]
    LayerMask m_RayLayerMask = -5; // -5 == Physics.DefaultRaycastLayers == ~(1 << 2), excludes IgnoreRaycast

    [Header("Layer Detection")]
    [SerializeField]
    [Tooltip("Layer names to detect via one-hot encoding. Order maps to observation indices.")]
    List<string> m_DetectableLayers = new List<string>();

    [Header("Ray Toggles")]
    [SerializeField]
    [Tooltip("Enable or disable each individual ray. Order: Up, Down, Left, Right, Back, Forward.")]
    bool[] m_RayEnabled = { true, true, true, true, true, true };

    [Header("Debug Gizmos")]
    [SerializeField]
    Color m_RayHitColor = Color.red;

    [SerializeField]
    Color m_RayMissColor = Color.yellow;

    // ──────────────────────────────────────────────
    //  Public Properties
    // ──────────────────────────────────────────────

    public string SensorName { get => m_SensorName; set => m_SensorName = value; }
    public float RayLength { get => m_RayLength; set => m_RayLength = value; }
    public float SphereRadius { get => m_SphereRadius; set => m_SphereRadius = value; }
    public LayerMask RayLayerMask { get => m_RayLayerMask; set => m_RayLayerMask = value; }
    public List<string> DetectableLayers { get => m_DetectableLayers; set => m_DetectableLayers = value; }
    public bool[] RayEnabled { get => m_RayEnabled; set => m_RayEnabled = value; }

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    SixAxisSensor m_Sensor;

    // ──────────────────────────────────────────────
    //  SensorComponent
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public override ISensor[] CreateSensors()
    {
        // Dispose any previously created sensor first.
        // The Editor validation path can call CreateSensors() repeatedly without ever
        // calling Dispose(), which would leak NativeArray persistent allocations.
        Dispose();

        var layerIndices = new int[m_DetectableLayers.Count];
        for (int i = 0; i < m_DetectableLayers.Count; i++)
        {
            int idx = LayerMask.NameToLayer(m_DetectableLayers[i]);
            if (idx < 0)
                Debug.LogWarning(
                    $"[{nameof(SixAxisSensorComponent)}] Detectable layer '{m_DetectableLayers[i]}' " +
                    "was not found. One-hot encoding for this slot will always be 0.", this);
            layerIndices[i] = idx;
        }

        // Ensure the toggle array is always exactly 6 elements (one per axis direction).
        if (m_RayEnabled == null || m_RayEnabled.Length != 6)
        {
            var fixed6 = new bool[6];
            for (int i = 0; i < 6; i++)
                fixed6[i] = m_RayEnabled != null && i < m_RayEnabled.Length ? m_RayEnabled[i] : true;
            m_RayEnabled = fixed6;
        }

        m_Sensor = new SixAxisSensor(
            m_SensorName,
            m_RayLength,
            m_SphereRadius,
            layerIndices,
            m_RayLayerMask,
            transform,
            m_RayEnabled
        );

        return new ISensor[] { m_Sensor };
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!ReferenceEquals(m_Sensor, null))
        {
            m_Sensor.Dispose();
            m_Sensor = null;
        }
    }

    void OnValidate()
    {
        if (m_DetectableLayers == null) return;
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (string layerName in m_DetectableLayers)
        {
            if (string.IsNullOrEmpty(layerName)) continue;
            if (LayerMask.NameToLayer(layerName) < 0)
                Debug.LogWarning(
                    $"[{nameof(SixAxisSensorComponent)}] Detectable layer '{layerName}' does not exist.", this);
            if (!seen.Add(layerName))
                Debug.LogWarning(
                    $"[{nameof(SixAxisSensorComponent)}] Detectable layer '{layerName}' is listed more than once.", this);
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    // ──────────────────────────────────────────────
    //  Gizmo Visualization
    // ──────────────────────────────────────────────

    static readonly Vector3[] s_PreviewDirections =
    {
        Vector3.up,
        Vector3.down,
        Vector3.left,
        Vector3.right,
        Vector3.back,
        Vector3.forward
    };

    static readonly string[] s_DirectionLabels =
    {
        "Up", "Down", "Left", "Right", "Back", "Forward"
    };

    void OnDrawGizmosSelected()
    {
        if (m_Sensor != null && m_Sensor.Observations != null)
            DrawRuntimeGizmos();
        else
            DrawEditorPreviewGizmos();
    }

    void DrawRuntimeGizmos()
    {
        var dirs = m_Sensor.RayDirections;
        var obs = m_Sensor.Observations;
        int floatsPerRay = m_Sensor.FloatsPerRay;
        float rayLen = m_Sensor.RayLength;
        float radius = m_Sensor.SphereRadius;
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = rotation * dirs[i];
            float val = obs[i * floatsPerRay];
            bool hit = val > 0.001f;
            // Recover physical distance from the linear observation: dist = (1 - val) * rayLen
            float dist = rayLen * (1f - val);

            Gizmos.color = hit ? m_RayHitColor : m_RayMissColor;
            Gizmos.DrawRay(origin, worldDir * dist);
            Gizmos.DrawWireSphere(origin + worldDir * dist, radius);
        }
    }

    void DrawEditorPreviewGizmos()
    {
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        for (int i = 0; i < s_PreviewDirections.Length; i++)
        {
            bool enabled = m_RayEnabled == null || i >= m_RayEnabled.Length || m_RayEnabled[i];
            if (!enabled) continue;

            Gizmos.color = m_RayMissColor;
            Vector3 worldDir = rotation * s_PreviewDirections[i];
            Gizmos.DrawRay(origin, worldDir * m_RayLength);
            Gizmos.DrawWireSphere(origin + worldDir * m_RayLength, m_SphereRadius);
        }
    }
}
