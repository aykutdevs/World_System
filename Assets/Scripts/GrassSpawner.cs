using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class GrassSpawner : MonoBehaviour
{
    [Header("References")]
    public MapGenerator mapGenerator;
    [Tooltip("Drag the terrain's MeshFilter here (the GameObject that has MapDisplay).")]
    public MeshFilter   terrainMeshFilter;
    public Material     grassMaterial;

    [Header("Density — Sub-Grid Jitter")]
    [Tooltip(
        "Divides each 1-unit vertex cell into an NxN sub-grid. " +
        "SubGridSize=5 → 25 blades/cell, 8 → 64 blades/cell. " +
        "Increase for denser, more natural-looking coverage.")]
    [Range(1, 12)]
    public int subGridSize = 6;   // default: 36 blades per grass vertex cell

    [Header("Blade Shape")]
    public float bladeHeight    = 0.55f;
    public float bladeHalfWidth = 0.12f;
    [Range(0f, 0.5f)]
    public float heightVariation = 0.25f;

    private Mesh              bladeMesh;
    private List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
    private const int         BATCH   = 1023;

    // ------------------------------------------------------------------ //

    public void SpawnGrass()
    {
        batches.Clear();

        if (mapGenerator == null || mapGenerator.latestNoiseMap == null)
        {
            Debug.LogWarning("GrassSpawner: Click Generate on MapGenerator first.");
            return;
        }
        if (terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null)
        {
            Debug.LogWarning("GrassSpawner: Assign the terrain MeshFilter and generate the map.");
            return;
        }
        if (grassMaterial == null)
        {
            Debug.LogWarning("GrassSpawner: Assign a Material that uses GrassShader.");
            return;
        }

        grassMaterial.enableInstancing = true;
        bladeMesh = CreateBladeMesh();
        BuildBatches();
    }

    // ------------------------------------------------------------------ //

    void BuildBatches()
    {
        float[,]      noiseMap  = mapGenerator.latestNoiseMap;
        TerrainType[] regions   = mapGenerator.regions;
        int           mapWidth  = noiseMap.GetLength(0);
        int           mapHeight = noiseMap.GetLength(1);

        // ---- Grass height band ----
        float grassMinH = 0f, grassMaxH = 1f;
        bool  found     = false;
        for (int i = 0; i < regions.Length; i++)
        {
            string n = regions[i].name.ToLower();
            if (n.Contains("grass") || n.Contains("çimen") || n.Contains("cimen") ||
                n.Contains("yeşil") || n.Contains("yesil"))
            {
                grassMinH = (i > 0) ? regions[i - 1].height : 0f;
                grassMaxH = regions[i].height;
                found     = true;
                break;
            }
        }
        if (!found)
        {
            Debug.LogWarning("GrassSpawner: No region named 'grass' or 'çimen'. Using middle region.");
            if (regions.Length >= 3)
            {
                int mid   = regions.Length / 2;
                grassMinH = (mid > 0) ? regions[mid - 1].height : 0f;
                grassMaxH = regions[mid].height;
            }
        }

        // ---- Terrain vertex array (LOCAL space → world via TransformPoint) ----
        Vector3[] meshVerts = terrainMeshFilter.sharedMesh.vertices;
        Transform meshTF    = terrainMeshFilter.transform;

        if (meshVerts.Length != mapWidth * mapHeight)
        {
            Debug.LogError(
                $"GrassSpawner: Vertex count mismatch — mesh has {meshVerts.Length} " +
                $"but map is {mapWidth}x{mapHeight}={mapWidth * mapHeight}. Regenerate the map.");
            return;
        }

        // Terrain world scale (lossyScale captures all parent transforms).
        // With Mesh.localScale = (10,10,10): cells are 10 world units wide,
        // so jitter must be ×10 and blade sizes must be ×10 to remain visible.
        float xzScale = meshTF.lossyScale.x;
        float yScale  = meshTF.lossyScale.y;

        // ---- Sub-grid jitter loop ----
        // Each terrain vertex covers a (xzScale)-world-unit cell.
        // We divide that cell into subGridSize×subGridSize sub-cells and place
        // one blade randomly within each sub-cell → subGridSize² blades per vertex.
        float cellStep = 1f / subGridSize;
        float halfCell = 0.5f;

        var currentBatch = new List<Matrix4x4>(BATCH);
        int totalBlades  = 0;

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float noiseH = noiseMap[x, y];
                if (noiseH < grassMinH || noiseH > grassMaxH) continue;

                // TransformPoint converts local vertex → world space, applying all transform scales.
                Vector3 worldPos = meshTF.TransformPoint(meshVerts[y * mapWidth + x]);

                for (int sy = 0; sy < subGridSize; sy++)
                {
                    for (int sx = 0; sx < subGridSize; sx++)
                    {
                        // Jitter in world units: the ±0.5 local-space offset is scaled by
                        // xzScale so blades spread across the full visible cell width.
                        float localX = (-halfCell + (sx + Random.value) * cellStep) * xzScale;
                        float localZ = (-halfCell + (sy + Random.value) * cellStep) * xzScale;

                        Vector3    pos   = new Vector3(worldPos.x + localX,
                                                       worldPos.y,
                                                       worldPos.z + localZ);
                        Quaternion rot   = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                        // Scale blades by terrain Y scale so they remain visible against scaled hills.
                        float      scale = yScale * (1f + Random.Range(-heightVariation, heightVariation));

                        currentBatch.Add(Matrix4x4.TRS(pos, rot, Vector3.one * scale));
                        totalBlades++;

                        if (currentBatch.Count == BATCH)
                        {
                            batches.Add(currentBatch.ToArray());
                            currentBatch = new List<Matrix4x4>(BATCH);
                        }
                    }
                }
            }
        }

        if (currentBatch.Count > 0)
            batches.Add(currentBatch.ToArray());

        Debug.Log($"GrassSpawner: {totalBlades:N0} blades ({subGridSize}²={subGridSize * subGridSize}/cell) " +
                  $"across {batches.Count} GPU draw calls.");
    }

    // ------------------------------------------------------------------ //

    void Update()
    {
        if (bladeMesh == null || grassMaterial == null || batches.Count == 0) return;
        foreach (var batch in batches)
            Graphics.DrawMeshInstanced(bladeMesh, 0, grassMaterial, batch);
    }

    // ------------------------------------------------------------------ //

    Mesh CreateBladeMesh()
    {
        float hw = bladeHalfWidth;
        float h  = bladeHeight;

        // Cross of two tapered quads, both windings present → visible from all angles
        var vertices = new Vector3[]
        {
            // Quad A  (XY plane)
            new Vector3(-hw,        0f, 0f),
            new Vector3( hw,        0f, 0f),
            new Vector3(-hw * 0.3f, h,  0f),
            new Vector3( hw * 0.3f, h,  0f),
            // Quad B  (ZY plane)
            new Vector3(0f, 0f, -hw),
            new Vector3(0f, 0f,  hw),
            new Vector3(0f, h,  -hw * 0.3f),
            new Vector3(0f, h,   hw * 0.3f),
        };

        var triangles = new int[]
        {
            // Quad A  front + back
            0, 2, 1,  2, 3, 1,
            0, 1, 2,  2, 1, 3,
            // Quad B  front + back
            4, 6, 5,  6, 7, 5,
            4, 5, 6,  6, 5, 7,
        };

        // UV.y = 0 at root, 1 at tip — drives wind sway and colour gradient in GrassShader
        var uvs = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };

        Mesh mesh      = new Mesh();
        mesh.vertices  = vertices;
        mesh.triangles = triangles;
        mesh.uv        = uvs;
        mesh.RecalculateNormals();
        return mesh;
    }
}
