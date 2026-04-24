using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A <see cref="SensorComponent"/> that creates a <see cref="FrontalConeSensor"/>.
/// Provides a 13-ray concentric cone (1-4-8 pattern) aimed along local forward,
/// using volumetric SphereCasts and linear inverse-distance observations.
///
/// Attach to the same GameObject as the drone agent.
/// </summary>
[AddComponentMenu("ML Agents/Frontal Cone Sensor")]
public class FrontalConeSensorComponent : SensorComponent, IDisposable
{
    // ──────────────────────────────────────────────
    //  Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sensor Identity")]
    [SerializeField]
    string m_SensorName = "FrontalConeSensor";

    [Header("Cone Configuration")]
    [SerializeField, Range(10f, 90f)]
    [Tooltip("Half-angle of the cone in degrees (total FOV = 2× this value). 45° gives a 90° FOV.")]
    float m_ConeHalfAngle = 45f;

    [SerializeField, Range(-90f, 90f)]
    [Tooltip("Tilt of the entire cone around the local right axis in degrees. Positive = up, negative = down.")]
    float m_TiltAngle = 0f;

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

    [Header("Debug Gizmos")]
    [SerializeField]
    Color m_RayHitColor = Color.red;

    [SerializeField]
    Color m_RayMissColor = Color.cyan;

    // ──────────────────────────────────────────────
    //  Public Properties
    // ──────────────────────────────────────────────

    public string SensorName { get => m_SensorName; set => m_SensorName = value; }
    public float ConeHalfAngle { get => m_ConeHalfAngle; set => m_ConeHalfAngle = value; }
    public float TiltAngle { get => m_TiltAngle; set => m_TiltAngle = value; }
    public float RayLength { get => m_RayLength; set => m_RayLength = value; }
    public float SphereRadius { get => m_SphereRadius; set => m_SphereRadius = value; }
    public LayerMask RayLayerMask { get => m_RayLayerMask; set => m_RayLayerMask = value; }
    public List<string> DetectableLayers { get => m_DetectableLayers; set => m_DetectableLayers = value; }

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    FrontalConeSensor m_Sensor;

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
                    $"[{nameof(FrontalConeSensorComponent)}] Detectable layer '{m_DetectableLayers[i]}' " +
                    "was not found. One-hot encoding for this slot will always be 0.", this);
            layerIndices[i] = idx;
        }

        m_Sensor = new FrontalConeSensor(
            m_SensorName,
            m_ConeHalfAngle,
            m_RayLength,
            m_SphereRadius,
            layerIndices,
            m_RayLayerMask,
            transform,
            m_TiltAngle
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
                    $"[{nameof(FrontalConeSensorComponent)}] Detectable layer '{layerName}' does not exist.", this);
            if (!seen.Add(layerName))
                Debug.LogWarning(
                    $"[{nameof(FrontalConeSensorComponent)}] Detectable layer '{layerName}' is listed more than once.", this);
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    // ──────────────────────────────────────────────
    //  Gizmo Visualization
    // ──────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (m_Sensor != null && m_Sensor.RayDirections != null)
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
        Quaternion rotation = transform.rotation * Quaternion.AngleAxis(-m_Sensor.TiltAngle, Vector3.right);

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
        Quaternion rotation = transform.rotation * Quaternion.AngleAxis(-m_TiltAngle, Vector3.right);

        // Re-use the sensor's own direction generator so the preview stays in sync
        // if the cone layout ever changes (avoids the previous duplication bug).
        Vector3[] dirs = FrontalConeSensor.GenerateConcentricConeDirections(m_ConeHalfAngle);

        Gizmos.color = m_RayMissColor;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = rotation * dirs[i];
            Gizmos.DrawRay(origin, worldDir * m_RayLength);
            Gizmos.DrawWireSphere(origin + worldDir * m_RayLength, m_SphereRadius);
        }
    }
}
