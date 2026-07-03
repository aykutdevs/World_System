using UnityEngine;
using UnityEngine.Rendering;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(float[,] heightMap, float heightMultiplier, AnimationCurve _heightCurve, float waterLevel)
    {
        // Copy curve so this method is safe even if called off the main thread later
        AnimationCurve heightCurve = (_heightCurve != null && _heightCurve.length > 0)
            ? new AnimationCurve(_heightCurve.keys)
            : AnimationCurve.Linear(0f, 0f, 1f, 1f);

        int width  = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        float topLeftX = (width  - 1) / -2f;
        float topLeftZ = (height - 1) /  2f;

        MeshData meshData    = new MeshData(width, height);
        int      vertexIndex = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = heightMap[x, y];

                // Flatten everything below waterLevel to a perfectly flat water surface
                if (h < waterLevel) h = waterLevel;

                float vertexHeight = heightCurve.Evaluate(h) * heightMultiplier;

                meshData.vertices[vertexIndex] = new Vector3(topLeftX + x, vertexHeight, topLeftZ - y);
                meshData.uvs[vertexIndex]      = new Vector2(x / (float)width, y / (float)height);

                if (x < width - 1 && y < height - 1)
                {
                    meshData.AddTriangle(vertexIndex, vertexIndex + width + 1, vertexIndex + width);
                    meshData.AddTriangle(vertexIndex + width + 1, vertexIndex, vertexIndex + 1);
                }

                vertexIndex++;
            }
        }

        // Shift every vertex down so the lowest point sits at Y = 0 in local space.
        // Without this, the water floor sits at (heightCurve(waterLevel) * multiplier) units
        // above the origin, making the entire terrain float in the sky.
        float minY = float.MaxValue;
        for (int i = 0; i < meshData.vertices.Length; i++)
            if (meshData.vertices[i].y < minY) minY = meshData.vertices[i].y;
        for (int i = 0; i < meshData.vertices.Length; i++)
            meshData.vertices[i] = new Vector3(meshData.vertices[i].x,
                                               meshData.vertices[i].y - minY,
                                               meshData.vertices[i].z);

        return meshData;
    }
}

public class MeshData
{
    public Vector3[] vertices;
    public int[]     triangles;
    public Vector2[] uvs;

    int triangleIndex;

    public MeshData(int meshWidth, int meshHeight)
    {
        vertices  = new Vector3[meshWidth * meshHeight];
        uvs       = new Vector2[meshWidth * meshHeight];
        triangles = new int[(meshWidth - 1) * (meshHeight - 1) * 6];
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles[triangleIndex]     = a;
        triangles[triangleIndex + 1] = b;
        triangles[triangleIndex + 2] = c;
        triangleIndex += 3;
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        // UInt32 index format supports meshes with more than 65535 vertices
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices    = vertices;
        mesh.triangles   = triangles;
        mesh.uv          = uvs;
        mesh.RecalculateNormals();
        return mesh;
    }
}
