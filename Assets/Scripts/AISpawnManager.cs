using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class AISpawnManager : MonoBehaviour
{
    [Header("References")]
    public MapGenerator mapGenerator;
    [Tooltip("Same MeshFilter as GrassSpawner and PropSpawner.")]
    public MeshFilter   terrainMeshFilter;

    [Header("Settings")]
    [Range(5, 10)]
    public int   spawnPointCount          = 7;
    public float minDistanceBetweenPoints = 15f;
    [Tooltip("Max summed noise-height difference across the 4 cardinal neighbours. Lower = flatter.")]
    public float maxSlopeDelta            = 0.05f;

    const string PARENT_NAME = "AI_Spawn_Manager";
    const string NODE_NAME   = "Cannibal_Spawn_Point";

    // ------------------------------------------------------------------ //

    public void PlaceSpawnPoints()
    {
        GameObject existing = GameObject.Find(PARENT_NAME);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing);
            else DestroyImmediate(existing);
        }

        if (!Validate()) return;

        float[,]      noiseMap  = mapGenerator.latestNoiseMap;
        TerrainType[] regions   = mapGenerator.regions;
        int           mapWidth  = noiseMap.GetLength(0);
        int           mapHeight = noiseMap.GetLength(1);

        Vector3[] meshVerts = terrainMeshFilter.sharedMesh.vertices;
        Transform meshTF    = terrainMeshFilter.transform;

        // ---- Determine walkable height band (prefer grass) ----
        float walkMin = mapGenerator.waterLevel + 0.01f;
        float walkMax = 0.85f;

        for (int i = 0; i < regions.Length; i++)
        {
            string n = regions[i].name.ToLower();
            if (n.Contains("grass") || n.Contains("çimen") || n.Contains("cimen"))
            {
                walkMin = (i > 0) ? regions[i - 1].height : 0f;
                walkMax = regions[i].height;
                break;
            }
        }

        // ---- Collect flat candidates ----
        List<Vector3> candidates = CollectCandidates(noiseMap, meshVerts, meshTF,
                                                     mapWidth, mapHeight, walkMin, walkMax, maxSlopeDelta);

        if (candidates.Count < spawnPointCount)
        {
            Debug.LogWarning($"AISpawnManager: Only {candidates.Count} flat candidates — relaxing slope filter.");
            candidates = CollectCandidates(noiseMap, meshVerts, meshTF,
                                           mapWidth, mapHeight, walkMin, walkMax, float.MaxValue);
        }

        // ---- Shuffle, then pick well-spread points ----
        ShuffleInPlace(candidates);

        GameObject parent = new GameObject(PARENT_NAME);
        var placed = new List<Vector3>();
        int idx    = 1;

        foreach (Vector3 candidate in candidates)
        {
            if (placed.Count >= spawnPointCount) break;

            bool tooClose = false;
            foreach (Vector3 p in placed)
            {
                if (Vector3.Distance(candidate, p) < minDistanceBetweenPoints)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            placed.Add(candidate);

            GameObject node       = new GameObject($"{NODE_NAME}_{idx++}");
            node.transform.parent   = parent.transform;
            node.transform.position = candidate;
        }

        Debug.Log($"AISpawnManager: Placed {placed.Count}/{spawnPointCount} cannibal spawn points under '{PARENT_NAME}'.");
    }

    // ------------------------------------------------------------------ //

    static List<Vector3> CollectCandidates(
        float[,] noiseMap, Vector3[] verts, Transform meshTF,
        int w, int h, float minH, float maxH, float maxSlope)
    {
        var list = new List<Vector3>();
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                float hh = noiseMap[x, y];
                if (hh < minH || hh > maxH) continue;

                float slope = Mathf.Abs(noiseMap[x, y - 1] - hh)
                            + Mathf.Abs(noiseMap[x, y + 1] - hh)
                            + Mathf.Abs(noiseMap[x - 1, y] - hh)
                            + Mathf.Abs(noiseMap[x + 1, y] - hh);
                if (slope > maxSlope) continue;

                list.Add(meshTF.TransformPoint(verts[y * w + x]));
            }
        }
        return list;
    }

    static void ShuffleInPlace(List<Vector3> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int     j   = Random.Range(0, i + 1);
            Vector3 tmp = list[i];
            list[i]     = list[j];
            list[j]     = tmp;
        }
    }

    bool Validate()
    {
        if (mapGenerator == null || mapGenerator.latestNoiseMap == null)
        { Debug.LogWarning("AISpawnManager: Generate map first."); return false; }

        if (terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null)
        { Debug.LogWarning("AISpawnManager: Assign terrain MeshFilter."); return false; }

        return true;
    }
}
