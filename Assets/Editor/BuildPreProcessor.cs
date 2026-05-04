using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically exports all <see cref="CurriculumPlan"/> and
/// <see cref="DroneRewardProfile"/> assets to
/// <c>StreamingAssets/{assetName}.json</c> before every build.
/// </summary>
public class BuildPreProcessor : IPreprocessBuildWithReport
{
    // Run before other preprocessors that might depend on StreamingAssets.
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        ExportAllCurriculumPlans();
        ExportAllRewardProfiles();
    }

    // -----------------------------------------------------------------------
    // CurriculumPlan export
    // -----------------------------------------------------------------------

    [System.Serializable]
    private class LessonData
    {
        public string lessonName;
        public float spawnRadius;
        public float maxEpisodeDistance;
        public int maxObstacleCount;
        public float obstacleSpawnRadius;
        public float minObstacleSpawnRadius;
        public float hexSpacing;
        public float hexMinDistance;
        public float hexObstacleDensity;
    }

    [System.Serializable]
    private class CurriculumData
    {
        public string planName;
        public List<LessonData> lessons = new List<LessonData>();
    }

    private static void ExportAllCurriculumPlans()
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

            var data = new CurriculumData { planName = plan.name };
            for (int i = 0; i < plan.LessonCount; i++)
            {
                LessonProfile lp = plan.GetLesson(i);
                data.lessons.Add(new LessonData
                {
                    lessonName             = lp.name,
                    spawnRadius            = lp.SpawnRadius,
                    maxEpisodeDistance     = lp.MaxEpisodeDistance,
                    maxObstacleCount       = lp.MaxObstacleCount,
                    obstacleSpawnRadius    = lp.ObstacleSpawnRadius,
                    minObstacleSpawnRadius = lp.MinObstacleSpawnRadius,
                    hexSpacing             = lp.HexSpacing,
                    hexMinDistance         = lp.HexMinDistance,
                    hexObstacleDensity     = lp.HexObstacleDensity,
                });
            }

            WriteJson(plan.name, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"[BuildPreProcessor] Exported CurriculumPlan '{plan.name}' ({plan.LessonCount} lessons).");
        }
    }

    // -----------------------------------------------------------------------
    // DroneRewardProfile export
    // -----------------------------------------------------------------------

    private static void ExportAllRewardProfiles()
    {
        string[] guids = AssetDatabase.FindAssets("t:DroneRewardProfile");
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<DroneRewardProfile>(assetPath);
            if (profile == null) continue;

            WriteJson(profile.name, JsonUtility.ToJson(profile, prettyPrint: true));
            Debug.Log($"[BuildPreProcessor] Exported DroneRewardProfile '{profile.name}'.");
        }
    }

    // -----------------------------------------------------------------------
    // Shared helper
    // -----------------------------------------------------------------------

    private static void WriteJson(string assetName, string json)
    {
        if (!Directory.Exists(Application.streamingAssetsPath))
            Directory.CreateDirectory(Application.streamingAssetsPath);

        string path = Path.Combine(Application.streamingAssetsPath, assetName + ".json");
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();
    }
}
