using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F2.4 — data definition of one procedural village/camp type. The
/// VillageSpawner rolls this def against each settlement zone: pass the
/// spawn-chance dice and a deterministic building set (picked from the pool
/// below) is placed inside the zone, already completed.
/// A new village type = a new asset of this type in VillageSpawner's list.
/// No code. Create via: Assets ▸ Create ▸ WILDCUT ▸ Village Def.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Village Def", fileName = "NewVillageDef")]
public class VillageDef : ScriptableObject
{
    [System.Serializable]
    public class BuildingEntry
    {
        public BuildingPlan plan;
        [Tooltip("How many of this plan a village gets (inclusive range, rolled per village).")]
        [Min(0)] public int minCount = 1;
        [Min(0)] public int maxCount = 2;
    }

    [Header("Presentation")]
    public string displayName = "Köy";

    [Header("Building pool")]
    [Tooltip("Every entry rolls its own count; the village is the union of all entries.")]
    public List<BuildingEntry> buildings = new List<BuildingEntry>();

    [Header("Layout")]
    [Tooltip("Minimum centre-to-centre distance between two village buildings (world units).")]
    public float minBuildingSpacing = 7f;

    [Header("Spawning")]
    [Tooltip("Chance that a settlement zone gets this village at all (rolled per zone).")]
    [Range(0f, 1f)] public float spawnChance = 0.6f;

    void OnValidate()
    {
        foreach (BuildingEntry e in buildings)
            if (e.maxCount < e.minCount) e.maxCount = e.minCount;
    }
}
