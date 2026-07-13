using System;
using System.Collections.Generic;
using Core.Save;
using UnityEngine;

/// <summary>
/// WILDCUT binder for Core.Save — the game-specific side of save/load.
/// F5 saves, F9 loads (single debug slot). What is saved:
///   world     — island seed + useRandomSeed flag (+ variant info string).
///               The archetype variant derives deterministically from the seed,
///               so regenerating with the same seed rebuilds the same island.
///   buildings — every PlacedBuilding: plan asset name, position, yaw,
///               construction progress.
///   player    — position + body yaw.
/// Restore order (world → buildings → player) is guaranteed by the order of
/// the Saveables() list — SaveService restores in caller order.
/// </summary>
public class GameSaveController : MonoBehaviour
{
    [Header("Input")]
    public KeyCode saveKey = KeyCode.F5;
    public KeyCode loadKey = KeyCode.F9;

    [Header("Slot")]
    public string slotName = "wildcut_save";

    [Header("References (auto-found if empty)")]
    public MapGenerator mapGenerator;
    public PlayerController player;
    public BuildPlacer buildPlacer;

    WorldBinder worldBinder;
    BuildingsBinder buildingsBinder;
    PlayerBinder playerBinder;

    void Awake()
    {
        if (mapGenerator == null) mapGenerator = FindObjectOfType<MapGenerator>();
        if (player == null)       player       = FindObjectOfType<PlayerController>();
        if (buildPlacer == null)  buildPlacer  = FindObjectOfType<BuildPlacer>();

        worldBinder     = new WorldBinder(this);
        buildingsBinder = new BuildingsBinder(this);
        playerBinder    = new PlayerBinder(this);
    }

    void Update()
    {
        if (Input.GetKeyDown(saveKey)) SaveNow();
        if (Input.GetKeyDown(loadKey)) LoadNow();
    }

    // Dependency order: the world regenerate wipes buildings and re-places the
    // player, so buildings and player must be restored AFTER it.
    IEnumerable<ISaveable> Saveables()
    {
        yield return worldBinder;
        yield return buildingsBinder;
        yield return playerBinder;
    }

    /// <summary>Public for MCP play-mode tests (no keyboard needed).</summary>
    public void SaveNow()
    {
        SaveService.Save(slotName, Saveables());
        Debug.Log($"[GameSaveController] Kaydedildi (seed {mapGenerator?.lastGeneratedSeed}).");
    }

    /// <summary>Public for MCP play-mode tests (no keyboard needed).</summary>
    public bool LoadNow()
    {
        bool ok = SaveService.Load(slotName, Saveables());
        Debug.Log(ok ? "[GameSaveController] Yükleme tamam." : "[GameSaveController] Yükleme başarısız.");
        return ok;
    }

    // ---- World: seed + variant ------------------------------------------ //

    [Serializable]
    class WorldData
    {
        public int seed;
        public bool useRandomSeed;
        public string variantInfo;   // informational — derived from seed on restore
    }

    class WorldBinder : ISaveable
    {
        readonly GameSaveController c;
        public WorldBinder(GameSaveController c) { this.c = c; }

        public string SaveId => "world";

        public string CaptureState() => JsonUtility.ToJson(new WorldData
        {
            seed          = c.mapGenerator.lastGeneratedSeed,
            useRandomSeed = c.mapGenerator.useRandomSeed,
            variantInfo   = c.mapGenerator.lastVariantInfo,
        });

        public void RestoreState(string json)
        {
            var d   = JsonUtility.FromJson<WorldData>(json);
            var gen = c.mapGenerator;
            if (gen == null) { Debug.LogError("[GameSaveController] MapGenerator yok — dünya yüklenemedi."); return; }

            // Full pipeline with the saved seed: terrain, zones, props, NavMesh,
            // character placement. Buildings/player are re-restored right after.
            gen.useRandomSeed = false;
            gen.seed          = d.seed;
            gen.GenerateMap();
            gen.useRandomSeed = d.useRandomSeed;   // user's original setting back

            Debug.Log($"[GameSaveController] Dünya yeniden üretildi (seed {d.seed}, varyant: {gen.lastVariantInfo}).");
        }
    }

    // ---- Buildings: plan + transform + construction progress ------------- //

    [Serializable]
    class BuildingData
    {
        public string planName;
        public Vector3 position;
        public float yaw;
        public bool completed;
        public int delivered;
    }

    [Serializable]
    class BuildingsData
    {
        public List<BuildingData> items = new List<BuildingData>();
    }

    class BuildingsBinder : ISaveable
    {
        readonly GameSaveController c;
        public BuildingsBinder(GameSaveController c) { this.c = c; }

        public string SaveId => "buildings";

        public string CaptureState()
        {
            var data = new BuildingsData();
            foreach (PlacedBuilding pb in FindObjectsOfType<PlacedBuilding>())
            {
                // Village buildings derive from the island seed — saving them too
                // would duplicate every village on load (seed regenerate + restore).
                if (pb.plan == null || pb.excludeFromSave) continue;
                ConstructionSite site = pb.GetComponent<ConstructionSite>();
                data.items.Add(new BuildingData
                {
                    planName  = pb.plan.name,
                    position  = pb.transform.position,
                    yaw       = pb.transform.eulerAngles.y,
                    completed = site == null || site.Completed,
                    delivered = site != null ? site.Delivered : 0,
                });
            }
            return JsonUtility.ToJson(data);
        }

        public void RestoreState(string json)
        {
            var data = JsonUtility.FromJson<BuildingsData>(json);
            if (data == null || data.items == null) return;

            // World regenerate already cleared old buildings; be safe anyway.
            BuildPlacer.ClearPlacedBuildings();

            MapDisplay display = FindObjectOfType<MapDisplay>();
            int restored = 0;

            foreach (BuildingData b in data.items)
            {
                BuildingPlan plan = FindPlan(b.planName);
                if (plan == null)
                {
                    Debug.LogWarning($"[GameSaveController] BuildingPlan '{b.planName}' bulunamadı — yapı atlandı.");
                    continue;
                }
                SpawnSavedBuilding(plan, b, display);
                restored++;
            }

            // One bake at the end — flattening changed the terrain mesh.
            if (restored > 0 && display != null) display.BakeNavMesh();
            Debug.Log($"[GameSaveController] {restored}/{data.items.Count} yapı geri yüklendi.");
        }

        BuildingPlan FindPlan(string assetName)
        {
            if (c.buildPlacer == null) return null;
            foreach (BuildingPlan p in c.buildPlacer.plans)
                if (p != null && p.name == assetName) return p;
            return null;
        }

        // Mirrors BuildPlacer.PlaceNow() minus ghost/input and the per-building
        // NavMesh bake (the caller bakes once after the whole batch).
        static void SpawnSavedBuilding(BuildingPlan plan, BuildingData b, MapDisplay display)
        {
            Quaternion rot = Quaternion.Euler(0f, b.yaw, 0f);

            if (plan.flattenGround && display != null)
                TerrainFlattener.FlattenArea(display, b.position,
                    BuildPlacer.RotatedFootprintAabb(plan.footprintSize, rot), b.position.y);

            Transform root = BuildPlacer.GetRoot().transform;

            if (b.completed)
            {
                GameObject go = Instantiate(plan.finalPrefab, b.position, rot, root);
                go.name = plan.displayName;
                go.AddComponent<PlacedBuilding>().plan = plan;
                PlacedBuilding.EnsureCollider(go, plan.footprintSize);
            }
            else
            {
                GameObject src = plan.constructionPrefab != null ? plan.constructionPrefab : plan.finalPrefab;
                GameObject go  = Instantiate(src, b.position, rot, root);
                go.name = $"{plan.displayName} (inşaat)";
                go.AddComponent<PlacedBuilding>().plan = plan;
                PlacedBuilding.EnsureCollider(go, plan.footprintSize);

                ConstructionSite site = go.AddComponent<ConstructionSite>();
                site.plan = plan;
                site.swapToFinalOnComplete = plan.constructionPrefab != null;
                site.RestoreProgress(b.delivered);
            }
        }
    }

    // ---- Player: position + yaw ------------------------------------------ //

    [Serializable]
    class PlayerData
    {
        public Vector3 position;
        public float yaw;
    }

    class PlayerBinder : ISaveable
    {
        readonly GameSaveController c;
        public PlayerBinder(GameSaveController c) { this.c = c; }

        public string SaveId => "player";

        public string CaptureState() => JsonUtility.ToJson(new PlayerData
        {
            position = c.player.transform.position,
            yaw      = c.player.transform.eulerAngles.y,
        });

        public void RestoreState(string json)
        {
            var d = JsonUtility.FromJson<PlayerData>(json);
            if (c.player == null) { Debug.LogError("[GameSaveController] PlayerController yok."); return; }

            // Runs AFTER the world regenerate (which re-placed the player at the
            // settlement zone) — the saved position wins.
            c.player.Teleport(d.position);
            c.player.transform.rotation = Quaternion.Euler(0f, d.yaw, 0f);
            Debug.Log($"[GameSaveController] Player {d.position} konumuna geri kondu.");
        }
    }
}
