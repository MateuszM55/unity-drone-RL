using Unity.Collections;
using UnityEngine;

/// <summary>
/// Shared, allocation-free hit-processing logic used by both
/// <see cref="FrontalConeSensor"/> and <see cref="SixAxisSensor"/>.
/// All methods are pure helpers — they write into a caller-supplied
/// observation buffer and have no side effects.
/// </summary>
internal static class SensorProcessing
{
    /// <summary>
    /// Converts a completed <see cref="SpherecastCommand"/> result buffer into
    /// flat observation floats and writes them into <paramref name="observations"/>.
    ///
    /// Layout per ray (starting at <c>rayIndex * floatsPerRay</c>):
    /// <list type="bullet">
    ///   <item>[0] — Linear inverse distance: 1.0 = touching, 0.0 = path clear (max range).</item>
    ///   <item>[1 … layerCount] — One-hot layer encoding. All zeros when no hit or unknown layer.</item>
    /// </list>
    ///
    /// The one-hot zero-clear is skipped for rays that did not register a hit,
    /// which is correct because the buffer is already zeroed after <see cref="Reset"/>
    /// and after construction. Only hit rays need their slot written.
    /// </summary>
    /// <param name="hits">Completed hit results, one per ray.</param>
    /// <param name="rayCount">Number of rays (must equal <paramref name="hits"/>.Length).</param>
    /// <param name="floatsPerRay">Floats per ray: 1 (distance) + detectableLayer count.</param>
    /// <param name="detectableLayers">Layer indices for one-hot encoding.</param>
    /// <param name="rayLength">Max cast length, used to normalise distance.</param>
    /// <param name="observations">Pre-allocated output buffer (size = rayCount × floatsPerRay). Caller must zero it before the first call (constructor guarantees this).</param>
    internal static void ProcessHits(
        NativeArray<RaycastHit> hits,
        int rayCount,
        int floatsPerRay,
        int[] detectableLayers,
        float rayLength,
        float[] observations)
    {
        for (int i = 0; i < rayCount; i++)
        {
            int baseIdx = i * floatsPerRay;
            var hit = hits[i];
            bool hasHit = hit.collider != null;

            if (hasHit)
            {
                // Linear inverse distance: 1.0 = touching, 0.0 = clear (max range).
                observations[baseIdx] = 1f - (hit.distance / rayLength);

                // Zero the one-hot slots, then set the matching layer.
                for (int t = 0; t < detectableLayers.Length; t++)
                    observations[baseIdx + 1 + t] = 0f;

                int hitLayer = hit.collider.gameObject.layer;
                for (int t = 0; t < detectableLayers.Length; t++)
                {
                    if (hitLayer == detectableLayers[t])
                    {
                        observations[baseIdx + 1 + t] = 1f;
                        break;
                    }
                }
            }
            else
            {
                // No hit: distance slot is 0 (clear), one-hot slots are all 0.
                // Slots are already 0 after construction / Reset — only write if
                // a previous step had written a hit value here.
                observations[baseIdx] = 0f;
                for (int t = 0; t < detectableLayers.Length; t++)
                    observations[baseIdx + 1 + t] = 0f;
            }
        }
    }
}
