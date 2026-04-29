using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically writes a <c>mapping_legend.txt</c> file into the build output
/// folder immediately after every successful Player build.
///
/// The file documents every <see cref="DroneRewardProfile"/> and
/// <see cref="CurriculumPlan"/> asset that exists in the project, ordered the
/// same way they appear in the Inspector lists on the prefab — so the index
/// numbers in your YAML trainer config map directly to the names in the file.
///
/// <b>Example output:</b>
/// <code>
/// --- REWARD PROFILES ---
/// Index 0: Simple
/// Index 1: Landing
///
/// --- CURRICULUMS ---
/// Index 0: Standard (5 lessons)
/// Index 1: Obstacle (3 lessons)
/// </code>
///
/// <b>Strategy:</b>
/// <list type="number">
///   <item>
///     Scan every prefab in the build scenes for a <see cref="DroneMLAgentBase"/>
///     and a <see cref="TrainingArena"/> component to obtain the exact ordered
///     lists that will be used at runtime.
///   </item>
///   <item>
///     Fall back to a full AssetDatabase search (sorted by asset name) when no
///     suitable prefab is found, so a manifest is always generated.
///   </item>
/// </list>
/// </summary>
public class BuildManifestGenerator : IPostprocessBuildWithReport
{
    // Run after all other post-process steps so the output folder exists.
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.result != BuildResult.Succeeded &&
            report.summary.result != BuildResult.Unknown)
        {
            return;
        }

        string buildDir = Path.GetDirectoryName(report.summary.outputPath);
        if (string.IsNullOrEmpty(buildDir))
        {
            Debug.LogWarning("[BuildManifestGenerator] Could not determine build output directory.");
            return;
        }

        List<string> rewardNames    = ResolveRewardProfileNames();
        List<(string name, int lessonCount)> curriculums = ResolveCurriculums();

        string manifestPath = Path.Combine(buildDir, "mapping_legend.txt");
        WriteLegend(manifestPath, rewardNames, curriculums);

        Debug.Log($"[BuildManifestGenerator] mapping_legend.txt written to: {manifestPath}");
    }

    // -------------------------------------------------------------------------
    // Resolution helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns reward-profile names in index order.
    /// Tries to read the ordered list from the first DroneMLAgentBase prefab found
    /// in the project; falls back to an alphabetical AssetDatabase scan.
    /// </summary>
    private static List<string> ResolveRewardProfileNames()
    {
        // Try to source the ordered list from a prefab.
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            DroneMLAgentBase agent = prefab.GetComponentInChildren<DroneMLAgentBase>(includeInactive: true);
            if (agent == null) continue;

            SerializedObject so = new SerializedObject(agent);
            SerializedProperty listProp = so.FindProperty("rewardProfiles");
            if (listProp != null && listProp.isArray && listProp.arraySize > 0)
            {
                List<string> names = new List<string>();
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    Object element = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    names.Add(element != null ? element.name : $"<missing at index {i}>");
                }
                return names;
            }
        }

        // Fallback: all DroneRewardProfile assets, sorted by name.
        return FallbackAssetNames<DroneRewardProfile>("t:DroneRewardProfile");
    }

    /// <summary>
    /// Returns curriculum-plan entries (name + lesson count) in index order.
    /// Tries to read the ordered list from the first TrainingArena prefab found
    /// in the project; falls back to an alphabetical AssetDatabase scan.
    /// </summary>
    private static List<(string name, int lessonCount)> ResolveCurriculums()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            TrainingArena arena = prefab.GetComponentInChildren<TrainingArena>(includeInactive: true);
            if (arena == null) continue;

            SerializedObject so = new SerializedObject(arena);
            SerializedProperty listProp = so.FindProperty("curriculumPlans");
            if (listProp != null && listProp.isArray && listProp.arraySize > 0)
            {
                List<(string name, int lessonCount)> entries = new List<(string name, int lessonCount)>();
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    Object element = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    CurriculumPlan plan = element as CurriculumPlan;
                    string name = element != null ? element.name : $"<missing at index {i}>";
                    int lessonCount = plan != null ? plan.LessonCount : 0;
                    entries.Add((name, lessonCount));
                }
                return entries;
            }
        }

        return FallbackCurriculums();
    }

    private static List<(string name, int lessonCount)> FallbackCurriculums()
    {
        List<(string name, int lessonCount)> entries = new List<(string name, int lessonCount)>();
        foreach (string guid in AssetDatabase.FindAssets("t:CurriculumPlan"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CurriculumPlan plan = AssetDatabase.LoadAssetAtPath<CurriculumPlan>(path);
            if (plan != null)
                entries.Add((plan.name, plan.LessonCount));
        }
        entries.Sort((a, b) => System.StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
        return entries;
    }

    /// <summary>
    /// Scans the AssetDatabase for all assets of <typeparamref name="T"/> and
    /// returns their names sorted alphabetically (deterministic fallback).
    /// </summary>
    private static List<string> FallbackAssetNames<T>(string filter) where T : Object
    {
        List<string> names = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets(filter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                names.Add(asset.name);
        }
        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // -------------------------------------------------------------------------
    // File writing
    // -------------------------------------------------------------------------

    private static void WriteLegend(string path, List<string> rewardNames, List<(string name, int lessonCount)> curriculums)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("--- REWARD PROFILES ---");
        if (rewardNames.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }
        else
        {
            for (int i = 0; i < rewardNames.Count; i++)
                sb.AppendLine($"Index {i}: {rewardNames[i]}");
        }

        sb.AppendLine();

        sb.AppendLine("--- CURRICULUMS ---");
        if (curriculums.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }
        else
        {
            for (int i = 0; i < curriculums.Count; i++)
                sb.AppendLine($"Index {i}: {curriculums[i].name} ({curriculums[i].lessonCount} lesson{(curriculums[i].lessonCount == 1 ? "" : "s")})");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
