using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically exports all <see cref="CurriculumPlan"/> and
/// <see cref="DroneRewardProfile"/> assets to a <c>config/</c> folder
/// placed next to the built executable before every build.
/// </summary>
public class BuildPreProcessor : IPreprocessBuildWithReport
{
    // Run before other preprocessors that might depend on StreamingAssets.
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string configDir = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "config"));

        ExportAllCurriculumPlans(configDir);
        ExportAllRewardProfiles(configDir);
    }

    // -----------------------------------------------------------------------
    // CurriculumPlan export  (data classes are defined in CurriculumPlan.cs)
    // -----------------------------------------------------------------------

    private static void ExportAllCurriculumPlans(string configDir)
    {
        string[] guids = AssetDatabase.FindAssets("t:CurriculumPlan");
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var plan = AssetDatabase.LoadAssetAtPath<CurriculumPlan>(assetPath);
            if (plan == null) continue;

            if (!plan.IsValid())
            {
                Debug.LogWarning($"[BuildPreProcessor] Skipping invalid CurriculumPlan: {assetPath}");
                continue;
            }

            var data = new CurriculumPlan.CurriculumData { planName = plan.name };
            for (int i = 0; i < plan.LessonCount; i++)
            {
                LessonProfile lp = plan.GetLesson(i);
                data.lessons.Add(new CurriculumPlan.LessonData
                {
                    lessonName             = lp.name,
                    spawnRadius            = lp.SpawnRadius,
                    spawnHeightMin         = lp.SpawnHeightMin,
                    spawnHeightMax         = lp.SpawnHeightMax,
                    spawnStartPad          = lp.SpawnStartPad,
                    maxEpisodeDistance     = lp.MaxEpisodeDistance,
                    maxObstacleCount       = lp.MaxObstacleCount,
                    obstacleSpawnRadius    = lp.ObstacleSpawnRadius,
                    minObstacleSpawnRadius = lp.MinObstacleSpawnRadius,
                    hexSpacing             = lp.HexSpacing,
                    hexMinDistance         = lp.HexMinDistance,
                    hexObstacleDensity     = lp.HexObstacleDensity,
                });
            }

            WriteJson(configDir, plan.name, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"[BuildPreProcessor] Exported CurriculumPlan '{plan.name}' ({plan.LessonCount} lessons) → {configDir}");
        }
    }

    // -----------------------------------------------------------------------
    // DroneRewardProfile export
    // -----------------------------------------------------------------------

    private static void ExportAllRewardProfiles(string configDir)
    {
        string[] guids = AssetDatabase.FindAssets("t:DroneRewardProfile");
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<DroneRewardProfile>(assetPath);
            if (profile == null) continue;

            WriteJson(configDir, profile.name, JsonUtility.ToJson(profile, prettyPrint: true));
            Debug.Log($"[BuildPreProcessor] Exported DroneRewardProfile '{profile.name}' → {configDir}");
        }
    }

    // -----------------------------------------------------------------------
    // Shared helper
    // -----------------------------------------------------------------------

    private static void WriteJson(string configDir, string assetName, string json)
    {
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        string path = Path.Combine(configDir, assetName + ".json");
        File.WriteAllText(path, json);
    }
}
