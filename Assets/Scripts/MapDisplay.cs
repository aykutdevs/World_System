using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class MapDisplay : MonoBehaviour
{
    public Renderer       textureRender;
    public MeshFilter     meshFilter;
    public MeshRenderer   meshRenderer;
    public MeshCollider   meshCollider;
    public NavMeshSurface navMeshSurface;

    [Header("NavMesh Settings")]
    [Range(0f, 90f)]
    public float maxAgentSlope    = 80f;
    public float agentClimbHeight = 5f;

    // ------------------------------------------------------------------ //

    public void DrawTexture(Texture2D texture)
    {
        textureRender.sharedMaterial.mainTexture = texture;
        textureRender.transform.localScale = new Vector3(texture.width, 1, texture.height);
    }

    public void DrawMesh(MeshData meshData, Texture2D texture)
    {
        Mesh generatedMesh = meshData.CreateMesh();

        meshFilter.sharedMesh                   = generatedMesh;
        meshRenderer.sharedMaterial.mainTexture = texture;

        // MeshGenerator normalises vertices so their minimum Y is 0 in local space.
        // Enforce that the mesh object itself also sits at world Y = 0 so the terrain
        // never floats in the sky regardless of where the GameObject was moved.
        Vector3 pos = meshFilter.transform.position;
        if (!Mathf.Approximately(pos.y, 0f))
        {
            meshFilter.transform.position = new Vector3(pos.x, 0f, pos.z);
            Debug.Log("[MapDisplay] Terrain Y-position was non-zero — reset to Y = 0 to ground the mesh.");
        }

        // Keep the MeshCollider in sync.
        // Null-first assignment forces Unity's physics cache to fully flush before
        // accepting the new mesh — prevents the collider from serving stale geometry
        // to the NavMesh baker on the very next line.
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = generatedMesh;
        }

        // NavMesh bake is intentionally deferred:
        // MapGenerator calls BakeNavMesh() after grass, props, and AI nodes are placed.
    }

    // ------------------------------------------------------------------ //

    public void BakeNavMesh()
    {
        if (navMeshSurface == null)
        {
            Debug.LogWarning("[MapDisplay] NavMeshSurface missing — assign it in the Inspector.");
            return;
        }
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[MapDisplay] No terrain mesh — generate the terrain first.");
            return;
        }

        navMeshSurface.RemoveData();

        // ---- Agent settings ----
        NavMeshBuildSettings bs = NavMesh.GetSettingsByID(navMeshSurface.agentTypeID);
        bs.agentSlope = maxAgentSlope;
        // Scale agentClimb by terrain Y lossyScale so agents can traverse the visible hills.
        bs.agentClimb = agentClimbHeight * meshFilter.transform.lossyScale.y;

        // ---- Sources ----
        // Explicit mesh source with localToWorldMatrix — embeds the (10,10,10) scale so
        // the builder receives vertices in true world space (Y=0..300 for typical terrain),
        // not the unscaled local space (Y=0..30).
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape        = NavMeshBuildSourceShape.Mesh,
                sourceObject = meshFilter.sharedMesh,
                transform    = meshFilter.transform.localToWorldMatrix,
                area         = 0   // Walkable
            }
        };

        // ---- Bake region & position ----
        // ROOT CAUSE FIX:
        // NavMesh.AddNavMeshData (called inside navMeshSurface.AddData) places the
        // NavMesh data at navMeshSurface.transform.position in the world.
        // BuildNavMeshData stores geometry RELATIVE to the 'position' argument.
        // Both must use the SAME origin — otherwise the NavMesh is offset by the
        // difference (e.g. wCenter.y = 150 → NavMesh appears 150 units too low).
        //
        // Fix: 'position' = navMeshSurface.transform.position (world origin of the
        // "Mesh" object = (0,0,0)).  Because position = (0,0,0), the NavMesh data's
        // local coordinate system IS world space, so bakeRegion can be specified
        // directly as the world-space AABB of the scaled terrain.
        Transform tf      = meshFilter.transform;
        Vector3   ls      = tf.lossyScale;
        Bounds    localB  = meshFilter.sharedMesh.bounds;
        Vector3   wCenter = tf.position + Vector3.Scale(localB.center, ls);   // e.g. (0,150,0)
        Vector3   wSize   = Vector3.Scale(localB.size, ls);
        wSize.y           = Mathf.Max(wSize.y + 100f, 200f);                  // vertical padding
        Bounds bakeRegion = new Bounds(wCenter, wSize + new Vector3(50f, 0f, 50f));

        Vector3    dataPosition = navMeshSurface.transform.position;   // = (0,0,0)
        Quaternion dataRotation = navMeshSurface.transform.rotation;   // = identity

        NavMeshData navData = NavMeshBuilder.BuildNavMeshData(
            bs, sources, bakeRegion,
            dataPosition, dataRotation
        );

        if (navData != null)
        {
            navMeshSurface.navMeshData = navData;
            navMeshSurface.AddData();
            Debug.Log($"[MapDisplay] NavMesh baked. DataOrigin={dataPosition}  " +
                      $"TerrainCenter={wCenter}  Scale={ls}  Climb={bs.agentClimb:F1}");
        }
        else
        {
            Debug.LogWarning("[MapDisplay] NavMeshBuilder returned null. Check NavMeshSurface agent type in Inspector.");
        }
    }
}
