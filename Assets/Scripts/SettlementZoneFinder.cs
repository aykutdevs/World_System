using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feature #6 follow-up — reserved settlement (village) areas.
/// Scans the generated island for large, flat, dry patches and stores them as
/// center + radius. Spawners lower their density inside these zones so future
/// Build-system content (Chapter 8) has clear ground.
/// Zones are visualised as yellow wire circles in the Scene view (Editor only).
/// </summary>
public class SettlementZoneFinder : MonoBehaviour
{
    [System.Serializable]
    public struct SettlementZone
    {
        public Vector3 center;   // world space, on the terrain surface
        public float   radius;   // world units
    }

    [Header("References")]
    public MapGenerator mapGenerator;
    public MeshFilter   terrainMeshFilter;

    [Header("Suitability filters")]
    [Tooltip("Maximum surface slope (degrees) a cell may have to count as buildable.")]
    [Range(2f, 30f)] public float maxSlopeAngle = 18f;
    [Tooltip("Normalized height margin above water level — keeps villages off the beach.")]
    [Range(0f, 0.2f)] public float heightAboveWater = 0.04f;
    [Tooltip("Maximum normalized height — keeps villages out of the mountains.")]
    [Range(0.3f, 1f)] public float maxHeight = 0.68f;

    [Header("Zone selection")]
    [Range(1, 8)] public int maxZones = 4;
    [Tooltip("Smallest useful village radius in world units.")]
    public float minZoneRadius = 25f;

    [Header("Debug")]
    [Tooltip("Draw the reserved zones as wire circles in the Scene view (Editor only).")]
    public bool showGizmos = true;
    public Color gizmoColor = Color.yellow;

    public List<SettlementZone> zones = new List<SettlementZone>();

    // ------------------------------------------------------------------ //

    public void FindZones()
    {
        zones.Clear();

        if (mapGenerator == null) mapGenerator = FindObjectOfType<MapGenerator>();
        if (terrainMeshFilter == null)
        {
            MapDisplay md = FindObjectOfType<MapDisplay>();
            if (md != null) terrainMeshFilter = md.meshFilter;
        }
        if (mapGenerator == null || mapGenerator.latestNoiseMap == null ||
            terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[SettlementZoneFinder] Generate the terrain first.");
            return;
        }

        float[,] noiseMap = mapGenerator.latestNoiseMap;
        int mapW = noiseMap.GetLength(0);
        int mapH = noiseMap.GetLength(1);

        Mesh mesh = terrainMeshFilter.sharedMesh;
        Vector3[] verts   = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (verts.Length != mapW * mapH)
        {
            Debug.LogWarning("[SettlementZoneFinder] Vertex count mismatch — regenerate the map.");
            return;
        }

        Transform meshTF   = terrainMeshFilter.transform;
        // One heightmap cell spans lossyScale/mult world units when supersampled.
        int   resMult      = (mapGenerator != null) ? Mathf.Max(1, mapGenerator.meshResolutionMultiplier) : 1;
        float cellWorld    = meshTF.lossyScale.x / resMult;          // world width of one cell
        float minNormalY   = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);
        float minH         = mapGenerator.waterLevel + heightAboveWater;

        // ---- 1. Suitability mask ----
        bool[,] ok = new bool[mapW, mapH];
        for (int y = 0; y < mapH; y++)
            for (int x = 0; x < mapW; x++)
            {
                float h = noiseMap[x, y];
                if (h < minH || h > maxHeight) continue;
                Vector3 wn = meshTF.TransformDirection(normals[y * mapW + x]);
                ok[x, y] = wn.normalized.y >= minNormalY;
            }

        // ---- 2. Chamfer distance transform (distance to nearest unsuitable cell) ----
        float[,] dist = new float[mapW, mapH];
        const float BIG = 1e9f;
        for (int y = 0; y < mapH; y++)
            for (int x = 0; x < mapW; x++)
                dist[x, y] = ok[x, y] ? BIG : 0f;

        for (int y = 0; y < mapH; y++)          // forward pass
            for (int x = 0; x < mapW; x++)
            {
                if (dist[x, y] == 0f) continue;
                float d = dist[x, y];
                if (x > 0)          d = Mathf.Min(d, dist[x - 1, y] + 1f);
                if (y > 0)          d = Mathf.Min(d, dist[x, y - 1] + 1f);
                if (x > 0 && y > 0) d = Mathf.Min(d, dist[x - 1, y - 1] + 1.414f);
                if (x < mapW - 1 && y > 0) d = Mathf.Min(d, dist[x + 1, y - 1] + 1.414f);
                dist[x, y] = d;
            }
        for (int y = mapH - 1; y >= 0; y--)     // backward pass
            for (int x = mapW - 1; x >= 0; x--)
            {
                if (dist[x, y] == 0f) continue;
                float d = dist[x, y];
                if (x < mapW - 1)              d = Mathf.Min(d, dist[x + 1, y] + 1f);
                if (y < mapH - 1)              d = Mathf.Min(d, dist[x, y + 1] + 1f);
                if (x < mapW - 1 && y < mapH - 1) d = Mathf.Min(d, dist[x + 1, y + 1] + 1.414f);
                if (x > 0 && y < mapH - 1)     d = Mathf.Min(d, dist[x - 1, y + 1] + 1.414f);
                dist[x, y] = d;
            }

        // ---- 3. Greedy peak picking: biggest clear circle first ----
        for (int zi = 0; zi < maxZones; zi++)
        {
            float best = 0f; int bx = -1, by = -1;
            for (int y = 0; y < mapH; y++)
                for (int x = 0; x < mapW; x++)
                    if (dist[x, y] > best) { best = dist[x, y]; bx = x; by = y; }

            float radiusWorld = best * cellWorld * 0.9f;   // slight margin inside the clear area
            if (bx < 0 || radiusWorld < minZoneRadius) break;

            Vector3 center = meshTF.TransformPoint(verts[by * mapW + bx]);
            zones.Add(new SettlementZone { center = center, radius = radiusWorld });

            // Suppress this zone plus breathing room so the next pick lands elsewhere.
            float suppress = best * 2.2f;
            float supSq    = suppress * suppress;
            for (int y = 0; y < mapH; y++)
                for (int x = 0; x < mapW; x++)
                {
                    float dx = x - bx, dy = y - by;
                    if (dx * dx + dy * dy < supSq) dist[x, y] = 0f;
                }
        }

        Debug.Log($"[SettlementZoneFinder] Found {zones.Count} settlement zone(s)" +
                  (zones.Count > 0 ? $" — largest radius {zones[0].radius:F0} world units." : "."));
    }

    public bool IsInsideAnyZone(Vector3 worldPos)
    {
        foreach (SettlementZone z in zones)
        {
            float dx = worldPos.x - z.center.x;
            float dz = worldPos.z - z.center.z;
            if (dx * dx + dz * dz < z.radius * z.radius) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ //

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || zones == null) return;
        UnityEditor.Handles.color = gizmoColor;
        foreach (SettlementZone z in zones)
        {
            UnityEditor.Handles.DrawWireDisc(z.center + Vector3.up * 2f, Vector3.up, z.radius);
            UnityEditor.Handles.DrawWireDisc(z.center + Vector3.up * 2f, Vector3.up, 2f);
        }
    }
#endif
}
