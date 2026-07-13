using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F2.4 — procedural village generation, an orchestration layer over existing
/// systems: SettlementZoneFinder supplies the flat areas, VillageDef supplies
/// the data, BuildPlacer.PlaceCompletedAt supplies validated placement with
/// ground flattening. Runs as a pipeline step right after zone finding (and
/// before props), both from MapGenerator.GenerateMap and the automation panel.
///
/// Everything is deterministic from the island seed: the same seed rolls the
/// same villages in the same spots — which is also why village buildings are
/// EXCLUDED from the save file (PlacedBuilding.excludeFromSave): loading
/// regenerates the world from the seed and the villages come back for free.
///
/// Play-mode extra: first time the player enters a village's zone radius,
/// "village_discovered" is raised on the event channel (documentary material).
/// </summary>
public class VillageSpawner : MonoBehaviour
{
    public const string ROOT_NAME = "Village_Buildings";

    [Header("Village types (data-driven — add VillageDef assets here)")]
    [Tooltip("One def is picked per zone (seed-deterministic) before its spawn chance is rolled.")]
    public List<VillageDef> villageDefs = new List<VillageDef>();

    [Header("Layout")]
    [Tooltip("Keep this radius around the zone centre free — the player spawns there.")]
    public float centerClearRadius = 8f;
    [Tooltip("Random spots tried per building before giving up (steep/overlapping spots are rejected).")]
    public int maxPlacementAttempts = 40;

    [Header("Events (optional)")]
    public WorldEventChannel eventChannel;
    public string discoveredEventId = "village_discovered";

    [Header("Debug")]
    public bool showGizmos = true;
    public Color gizmoColor = new Color(1f, 0.55f, 0.1f);   // orange = zone has a village

    [System.Serializable]
    public class SpawnedVillage
    {
        public string defName;
        public Vector3 center;      // zone centre
        public float radius;        // zone radius — also the discovery trigger distance
        public int buildingCount;
        public bool discovered;     // play-mode session state
    }

    [Tooltip("Filled by SpawnVillages — one entry per zone that got a village.")]
    public List<SpawnedVillage> villages = new List<SpawnedVillage>();

    Transform player;   // cached for the discovery check

    // ------------------------------------------------------------------ //
    //  Pipeline step
    // ------------------------------------------------------------------ //

    /// <summary>Clears old villages and rolls new ones for the current island.
    /// Call AFTER SettlementZoneFinder.FindZones (needs fresh zones) and BEFORE
    /// the NavMesh bake (flattening changes the terrain mesh).</summary>
    public void SpawnVillages()
    {
        ClearVillages();
        villages.Clear();

        SettlementZoneFinder zones = FindObjectOfType<SettlementZoneFinder>();
        BuildPlacer placer         = FindObjectOfType<BuildPlacer>();
        MapGenerator gen           = FindObjectOfType<MapGenerator>();

        if (zones == null || placer == null || gen == null)
        {
            Debug.LogWarning("[VillageSpawner] SettlementZoneFinder/BuildPlacer/MapGenerator eksik — köyler atlandı.");
            return;
        }
        if (villageDefs.Count == 0 || zones.zones.Count == 0)
        {
            Debug.Log("[VillageSpawner] VillageDef listesi ya da zone yok — köy kurulmadı.");
            return;
        }

        int seed = gen.lastGeneratedSeed;
        Transform root = new GameObject(ROOT_NAME).transform;
        int totalBuildings = 0;

        for (int zi = 0; zi < zones.zones.Count; zi++)
        {
            SettlementZoneFinder.SettlementZone zone = zones.zones[zi];

            // Per-zone RNG stream: independent of other zones' outcomes, so one
            // zone failing placement never shifts its neighbours' villages.
            var rng = new System.Random(unchecked(seed * 486187739 + zi * 7919 + 13));

            VillageDef def = villageDefs[rng.Next(villageDefs.Count)];
            if (def == null || rng.NextDouble() > def.spawnChance) continue;

            int placed = PlaceVillage(def, zone, rng, placer, root);
            if (placed > 0)
            {
                villages.Add(new SpawnedVillage
                {
                    defName = def.displayName,
                    center = zone.center,
                    radius = zone.radius,
                    buildingCount = placed,
                });
                totalBuildings += placed;
            }
        }

        if (root.childCount == 0) DestroyRoot(root.gameObject);
        Debug.Log($"[VillageSpawner] {villages.Count} köy / {totalBuildings} yapı kuruldu " +
                  $"({zones.zones.Count} zone, seed {seed}).");
    }

    int PlaceVillage(VillageDef def, SettlementZoneFinder.SettlementZone zone,
                     System.Random rng, BuildPlacer placer, Transform root)
    {
        // Roll the building set from the def's pool.
        var toPlace = new List<BuildingPlan>();
        foreach (VillageDef.BuildingEntry e in def.buildings)
        {
            if (e.plan == null) continue;
            int count = rng.Next(e.minCount, e.maxCount + 1);
            for (int i = 0; i < count; i++) toPlace.Add(e.plan);
        }

        var positions = new List<Vector3>();
        float minSpacingSq = def.minBuildingSpacing * def.minBuildingSpacing;

        foreach (BuildingPlan plan in toPlace)
        {
            float margin = Mathf.Max(plan.footprintSize.x, plan.footprintSize.y);
            float maxR = zone.radius - margin;
            if (maxR <= centerClearRadius) continue;   // zone too small for this plan

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                // Uniform point in the annulus between the clear centre and the rim.
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float r   = Mathf.Lerp(centerClearRadius, maxR, Mathf.Sqrt((float)rng.NextDouble()));
                Vector3 pos = zone.center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;

                bool tooClose = false;
                foreach (Vector3 p in positions)
                {
                    float dx = p.x - pos.x, dz = p.z - pos.z;
                    if (dx * dx + dz * dz < minSpacingSq) { tooClose = true; break; }
                }
                if (tooClose) continue;

                // Face the zone centre (doors toward the "village square").
                float yaw = Mathf.Atan2(zone.center.x - pos.x, zone.center.z - pos.z) * Mathf.Rad2Deg;

                GameObject building = placer.PlaceCompletedAt(plan, pos, yaw, root);
                if (building == null) continue;   // steep / wet / overlapping — try elsewhere

                building.GetComponent<PlacedBuilding>().excludeFromSave = true;
                AddNavMeshObstacle(building);
                positions.Add(building.transform.position);
                break;
            }
        }
        return positions.Count;
    }

    // Buildings are not part of the NavMesh bake source (terrain only), so agents
    // would path straight through them. Runtime carving is the roadmap's plan B.
    static void AddNavMeshObstacle(GameObject building)
    {
        Renderer[] rends = building.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        NavMeshObstacle obs = building.AddComponent<NavMeshObstacle>();
        obs.shape   = NavMeshObstacleShape.Box;
        obs.carving = true;
        obs.center  = building.transform.InverseTransformPoint(b.center);
        Vector3 ls  = building.transform.lossyScale;
        obs.size    = new Vector3(b.size.x / Mathf.Max(0.001f, ls.x),
                                  b.size.y / Mathf.Max(0.001f, ls.y),
                                  b.size.z / Mathf.Max(0.001f, ls.z));
    }

    // ------------------------------------------------------------------ //
    //  Cleanup (regenerate policy — villages belong to the old island)
    // ------------------------------------------------------------------ //

    /// <summary>Static so MapGenerator can clear stale villages even when no
    /// spawner is in the scene (same pattern as BuildPlacer.ClearPlacedBuildings).</summary>
    public static void ClearVillages()
    {
        GameObject root = GameObject.Find(ROOT_NAME);
        if (root != null) DestroyRoot(root);
    }

    static void DestroyRoot(GameObject root)
    {
        if (Application.isPlaying)
        {
            // Destroy defers to end of frame — rename first so a same-frame
            // SpawnVillages creates a fresh root instead of adopting this one,
            // and deactivate so the dying colliders stop deflecting the
            // raycast/overlap checks of same-frame placements.
            root.name = ROOT_NAME + " (clearing)";
            root.SetActive(false);
            Destroy(root);
        }
        else DestroyImmediate(root);
    }

    // ------------------------------------------------------------------ //
    //  Discovery event (play mode)
    // ------------------------------------------------------------------ //

    void Update()
    {
        if (eventChannel == null || villages.Count == 0) return;

        if (player == null)
        {
            PlayerController pc = FindObjectOfType<PlayerController>();
            if (pc == null) return;
            player = pc.transform;
        }

        foreach (SpawnedVillage v in villages)
        {
            if (v.discovered) continue;
            float dx = player.position.x - v.center.x;
            float dz = player.position.z - v.center.z;
            if (dx * dx + dz * dz > v.radius * v.radius) continue;

            v.discovered = true;
            eventChannel.Raise(discoveredEventId, v.center);
            Debug.Log($"[VillageSpawner] Köy keşfedildi: '{v.defName}' ({v.buildingCount} yapı).");
        }
    }

    // ------------------------------------------------------------------ //

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || villages == null) return;
        UnityEditor.Handles.color = gizmoColor;
        foreach (SpawnedVillage v in villages)
        {
            UnityEditor.Handles.DrawWireDisc(v.center + Vector3.up * 2.5f, Vector3.up, v.radius);
            UnityEditor.Handles.DrawWireDisc(v.center + Vector3.up * 2.5f, Vector3.up, v.radius - 0.6f);
        }
    }
#endif
}
