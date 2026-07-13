using UnityEditor;
using UnityEngine;

/// <summary>
/// WILDCUT bridge for Core.ModularKit (the ONLY game-side piece): turns an
/// assembled prefab (Assets/AssembledPrefabs/...) into a BuildingPlan asset so
/// the existing Chapter 8 build system can place it — BuildPlacer itself is
/// untouched. Footprint is measured from the prefab's renderer bounds.
/// Usage: select the prefab, then WILDCUT ▸ Create Building Plan from Prefab.
/// </summary>
public static class AssembledBuildingPlanCreator
{
    [MenuItem("WILDCUT/Create Building Plan from Prefab")]
    static void CreateFromSelection()
    {
        var prefab = Selection.activeObject as GameObject;
        if (prefab == null)
        {
            Debug.LogWarning("[WILDCUT] Önce bir prefab seç (ör. Assets/AssembledPrefabs altından).");
            return;
        }
        CreatePlan(prefab);
    }

    /// <summary>Public for automation/tests. Returns the created plan asset.</summary>
    public static BuildingPlan CreatePlan(GameObject prefab)
    {
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }

        var plan = ScriptableObject.CreateInstance<BuildingPlan>();
        plan.displayName = prefab.name.Replace("_", " ");
        plan.finalPrefab = prefab;
        // Small margin so the slope/overlap checks cover the whole base.
        plan.footprintSize = new Vector2(Mathf.Max(1f, b.size.x + 0.4f),
                                         Mathf.Max(1f, b.size.z + 0.4f));

        if (!AssetDatabase.IsValidFolder("Assets/Buildings"))
            AssetDatabase.CreateFolder("Assets", "Buildings");
        string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/Buildings/Plan_{prefab.name}.asset");
        AssetDatabase.CreateAsset(plan, path);
        AssetDatabase.SaveAssets();

        Debug.Log($"[WILDCUT] BuildingPlan oluşturuldu: {path} " +
                  $"(footprint {plan.footprintSize.x:F1}×{plan.footprintSize.y:F1}). " +
                  "BuildPlacer'ın Plans listesine ekleyip B ile yerleştirebilirsin.");
        Selection.activeObject = plan;
        return plan;
    }
}
