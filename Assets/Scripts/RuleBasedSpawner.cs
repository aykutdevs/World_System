using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feature #6 follow-up — data-driven prop scatter.
/// Reads a list of PlacementRule assets and scatters each over the terrain.
/// Adding a new asset type = create a PlacementRule asset and add it to the
/// Rules list in the Inspector. No code changes.
/// Replaces the hard-coded PropSpawner in the world pipeline.
/// </summary>
public class RuleBasedSpawner : MonoBehaviour
{
    [Header("References")]
    public MapGenerator mapGenerator;
    [Tooltip("The terrain mesh object (same MeshFilter as on MapDisplay).")]
    public MeshFilter terrainMeshFilter;
    [Tooltip("Optional — density inside its zones is reduced per rule settings.")]
    public SettlementZoneFinder settlementZones;

    [Header("Rules")]
    public List<PlacementRule> rules = new List<PlacementRule>();

    public const string ROOT_NAME = "Procedural_Props";

    // ------------------------------------------------------------------ //

    public void SpawnAll()
    {
        ClearSpawned();
        if (!Validate()) return;

        if (settlementZones == null)
            settlementZones = FindObjectOfType<SettlementZoneFinder>();

        float[,]      noiseMap = mapGenerator.latestNoiseMap;
        TerrainType[] regions  = mapGenerator.regions;
        int mapW = noiseMap.GetLength(0);
        int mapH = noiseMap.GetLength(1);

        Mesh mesh = terrainMeshFilter.sharedMesh;
        Vector3[] verts   = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (verts.Length != mapW * mapH)
        {
            Debug.LogError("[RuleBasedSpawner] Vertex count mismatch — regenerate the map first.");
            return;
        }
        if (normals == null || normals.Length != verts.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        Transform meshTF    = terrainMeshFilter.transform;
        float     propScale = meshTF.lossyScale.y;

        // Pre-resolve region name per vertex height once (same banding as BuildColourMap).
        GameObject root = new GameObject(ROOT_NAME);

        int totalSpawned = 0;
        for (int r = 0; r < rules.Count; r++)
        {
            PlacementRule rule = rules[r];
            if (rule == null) continue;

            // No placeholder fallback: a rule without prefabs spawns nothing.
            bool hasPrefab = false;
            if (rule.prefabs != null)
                foreach (GameObject p in rule.prefabs)
                    if (p != null) { hasPrefab = true; break; }
            if (!hasPrefab)
            {
                Debug.Log($"[RuleBasedSpawner] '{rule.name}' prefab atanmadığı için atlandı.");
                continue;
            }

            // Deterministic per rule + per island: same seed → same layout.
            Random.InitState(mapGenerator.lastGeneratedSeed ^ (r + 1) * 7919);

            GameObject ruleRoot = new GameObject(rule.name);
            ruleRoot.transform.parent = root.transform;

            int spawned = SpawnRule(rule, ruleRoot.transform, noiseMap, regions,
                                    verts, normals, meshTF, propScale, mapW, mapH);
            totalSpawned += spawned;
            Debug.Log($"[RuleBasedSpawner] Rule '{rule.name}': {spawned} instances.");
        }

        if (totalSpawned == 0)
        {
            // Nothing spawned (e.g. no rule has prefabs yet) — leave no empty container.
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            Debug.Log("[RuleBasedSpawner] No props spawned — scene left clean.");
            return;
        }

        Debug.Log($"[RuleBasedSpawner] Total {totalSpawned} props under '{ROOT_NAME}'.");
    }

    public void ClearSpawned()
    {
        GameObject old = GameObject.Find(ROOT_NAME);
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
        }
    }

    // ------------------------------------------------------------------ //

    int SpawnRule(PlacementRule rule, Transform parent, float[,] noiseMap, TerrainType[] regions,
                  Vector3[] verts, Vector3[] normals, Transform meshTF, float propScale,
                  int mapW, int mapH)
    {
        float minNormY = rule.MinNormalY;   // steepest allowed
        float maxNormY = rule.MaxNormalY;   // flattest allowed
        var   placed   = rule.minSpacing > 0f ? new List<Vector3>(256) : null;
        float spacingWorld = rule.minSpacing * propScale;
        int   count    = 0;

        // Supersampled maps have mult² more cells per world area; divide the per-cell
        // chance so a rule's density means the same world-space density at any resolution.
        int   resMult     = Mathf.Max(1, mapGenerator.meshResolutionMultiplier);
        float densityComp = 1f / (resMult * resMult);

        // Cluster noise offset — vary per rule/island so patches move with the seed.
        float clusterOffX = Random.Range(0f, 1000f);
        float clusterOffY = Random.Range(0f, 1000f);

        for (int y = 1; y < mapH - 1; y++)
        {
            for (int x = 1; x < mapW - 1; x++)
            {
                float h = noiseMap[x, y];
                if (h < rule.minHeight || h > rule.maxHeight) continue;
                if (!RegionMatches(rule, regions, h)) continue;

                int     vi        = y * mapW + x;
                Vector3 worldNorm = meshTF.TransformDirection(normals[vi]).normalized;
                if (worldNorm.y < minNormY || worldNorm.y > maxNormY) continue;

                float chance = rule.density * densityComp;

                if (rule.useClusterNoise)
                {
                    float cluster = Mathf.PerlinNoise(
                        x * rule.clusterFrequency + clusterOffX,
                        y * rule.clusterFrequency + clusterOffY);
                    if (cluster < rule.clusterThreshold) continue;
                    chance *= cluster;
                }

                Vector3 worldPos = meshTF.TransformPoint(verts[vi]);

                if (settlementZones != null && settlementZones.IsInsideAnyZone(worldPos))
                    chance *= rule.settlementDensityMultiplier;

                if (Random.value >= chance) continue;

                if (placed != null && TooClose(worldPos, placed, spacingWorld)) continue;

                GameObject go = InstantiateFor(rule, worldPos, parent, propScale);
                if (go == null) continue;

                placed?.Add(worldPos);
                count++;
            }
        }
        return count;
    }

    GameObject InstantiateFor(PlacementRule rule, Vector3 pos, Transform parent, float propScale)
    {
        GameObject prefab = rule.prefabs[Random.Range(0, rule.prefabs.Length)];
        if (prefab == null) return null;

        GameObject go = Instantiate(prefab, pos, Quaternion.identity, parent);
        if (rule.randomYRotation)
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale *= Random.Range(rule.scaleRange.x, rule.scaleRange.y);
        return go;
    }

    static bool RegionMatches(PlacementRule rule, TerrainType[] regions, float h)
    {
        if (rule.allowedRegionKeywords == null || rule.allowedRegionKeywords.Length == 0)
            return true;

        // Same banding as MapGenerator.BuildColourMap: first region whose height >= h.
        string regionName = null;
        for (int i = 0; i < regions.Length; i++)
        {
            if (h <= regions[i].height) { regionName = regions[i].name; break; }
        }
        if (regionName == null) return false;

        string lower = regionName.ToLowerInvariant();
        foreach (string keyword in rule.allowedRegionKeywords)
        {
            if (!string.IsNullOrEmpty(keyword) && lower.Contains(keyword.ToLowerInvariant()))
                return true;
        }
        return false;
    }

    static bool TooClose(Vector3 pos, List<Vector3> placed, float minDist)
    {
        float minSq = minDist * minDist;
        foreach (Vector3 p in placed)
            if ((pos - p).sqrMagnitude < minSq) return true;
        return false;
    }

    bool Validate()
    {
        if (mapGenerator == null) mapGenerator = FindObjectOfType<MapGenerator>();
        if (mapGenerator == null || mapGenerator.latestNoiseMap == null)
        { Debug.LogWarning("[RuleBasedSpawner] Run MapGenerator.GenerateMap() first."); return false; }

        if (terrainMeshFilter == null)
        {
            MapDisplay md = FindObjectOfType<MapDisplay>();
            if (md != null) terrainMeshFilter = md.meshFilter;
        }
        if (terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null)
        { Debug.LogWarning("[RuleBasedSpawner] Assign the terrain MeshFilter reference."); return false; }

        if (rules.Count == 0)
        { Debug.LogWarning("[RuleBasedSpawner] No PlacementRule assets in the Rules list."); return false; }

        return true;
    }
}
