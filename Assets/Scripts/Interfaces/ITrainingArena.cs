using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Contract that any training arena must satisfy.
///
/// <b>Purpose:</b> Decouples <see cref="DroneMLAgentBase"/> (and other consumers)
/// from the concrete <see cref="TrainingArena"/> MonoBehaviour, making it easy to
/// substitute alternative arena implementations (e.g. test doubles, network arenas)
/// without changing the agent code.
///
/// <b>Discovery:</b>
/// Unity's <c>GetComponentInParent</c> does not support interface type parameters,
/// so agents retrieve the concrete <see cref="TrainingArena"/> from the parent hierarchy
/// and store it through this interface for loose coupling.
/// </summary>
public interface ITrainingArena
{
    // ── Identity ─────────────────────────────────────────────────────────

    /// <summary>Unique arena index assigned by <see cref="ArenaManager"/>. -1 if unassigned.</summary>
    int ArenaId { get; }

    // ── Local references ─────────────────────────────────────────────────

    /// <summary>The ML-Agent operating in this arena.</summary>
    Agent Agent { get; }

    /// <summary>The target/landing pad transform for this arena.</summary>
    Transform Target { get; }

    // ── Curriculum ───────────────────────────────────────────────────────

    /// <summary>The shared curriculum plan asset.</summary>
    CurriculumPlan CurriculumPlan { get; }

    /// <summary>The lesson index resolved during the most recent <see cref="SetupEpisode"/> call.</summary>
    int CurrentLessonIndex { get; }

    // ── Lifecycle ────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns the arena's unique ID and runs one-time component discovery.
    /// Called by <see cref="ArenaManager"/> immediately after instantiation.
    /// </summary>
    void Initialise(int id);

    /// <summary>
    /// Runs one-time component discovery without changing the arena ID.
    /// Safe to call multiple times (idempotent).
    /// Called by agents that discover the arena via parent hierarchy lookup.
    /// </summary>
    void Initialise();

    /// <summary>
    /// Configures the arena for a new episode: resolves the curriculum lesson,
    /// positions <paramref name="drone"/>, and spawns obstacles.
    /// </summary>
    /// <param name="drone">The drone transform to reposition.</param>
    /// <param name="defaultPosition">Fallback local position when curriculum is unavailable.</param>
    /// <param name="defaultRotation">Fallback local rotation when curriculum is unavailable.</param>
    /// <returns>Maximum allowed distance from target before the episode should be terminated.</returns>
    float SetupEpisode(Transform drone, Vector3 defaultPosition, Quaternion defaultRotation);
}
