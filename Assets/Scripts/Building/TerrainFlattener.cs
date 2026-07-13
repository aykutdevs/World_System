using UnityEngine;

/// <summary>
/// WILDCUT Chapter 8 — local terrain flattening under a building footprint.
/// Edits the existing terrain mesh in place (vertices inside the footprint snap
/// to the base height, a margin ring blends smoothly into the surroundings),
/// then refreshes the MeshCollider. The NavMesh re-bake is triggered by the
/// caller (BuildPlacer) since it already owns the placement flow.
/// Note: mapGenerator.latestNoiseMap is NOT rewritten — a regenerate rebuilds
/// the whole island anyway and clears placed buildings with it.
/// </summary>
public static class TerrainFlattener
{
    /// <param name="worldCenter">Footprint centre in world space.</param>
    /// <param name="worldSize">Footprint size in world units (X × Z), axis-aligned.
    /// For rotated buildings pass the rotated footprint's bounding box.</param>
    /// <param name="targetWorldY">World height the flattened ground should sit at.</param>
    /// <param name="blendMargin">Extra ring (world units) blending back to the original terrain.</param>
    public static void FlattenArea(MapDisplay display, Vector3 worldCenter, Vector2 worldSize,
                                   float targetWorldY, float blendMargin = 2.5f)
    {
        if (display == null || display.meshFilter == null || display.meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[TerrainFlattener] No terrain mesh to flatten.");
            return;
        }

        MeshFilter mf   = display.meshFilter;
        Transform  tf   = mf.transform;
        Mesh       mesh = mf.sharedMesh;
        Vector3[]  verts = mesh.vertices;

        Vector3 localCenter  = tf.InverseTransformPoint(worldCenter);
        Vector3 ls           = tf.lossyScale;
        float   halfX        = worldSize.x * 0.5f / Mathf.Max(0.001f, ls.x);
        float   halfZ        = worldSize.y * 0.5f / Mathf.Max(0.001f, ls.z);
        float   marginLocal  = blendMargin / Mathf.Max(0.001f, ls.x);
        float   targetLocalY = (targetWorldY - tf.position.y) / Mathf.Max(0.001f, ls.y);

        int touched = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            float dx = Mathf.Abs(verts[i].x - localCenter.x);
            float dz = Mathf.Abs(verts[i].z - localCenter.z);
            if (dx > halfX + marginLocal || dz > halfZ + marginLocal) continue;

            // 1 inside the footprint → smooth falloff to 0 across the margin ring.
            float overshoot = Mathf.Max(dx - halfX, dz - halfZ);
            float t = overshoot <= 0f
                ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, overshoot / marginLocal);

            verts[i].y = Mathf.Lerp(verts[i].y, targetLocalY, t);
            touched++;
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Null-first swap flushes the physics cache (same pattern as MapDisplay.DrawMesh).
        if (display.meshCollider != null)
        {
            display.meshCollider.sharedMesh = null;
            display.meshCollider.sharedMesh = mesh;
        }

        Debug.Log($"[TerrainFlattener] Flattened {touched} vertices under footprint at {worldCenter}.");
    }
}
