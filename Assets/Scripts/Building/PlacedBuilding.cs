using UnityEngine;

/// <summary>
/// WILDCUT Chapter 8 — marker on the root of every placed structure.
/// BuildPlacer's overlap check looks for this component, so anything carrying
/// it blocks new placements on the same spot (construction sites included).
/// </summary>
public class PlacedBuilding : MonoBehaviour
{
    public BuildingPlan plan;

    [Tooltip("F2.4: procedural (village) structures derive from the island seed — " +
             "the save system skips them, since loading regenerates them with the world.")]
    public bool excludeFromSave;

    /// <summary>Guarantees the structure has a collider (needed both for the
    /// overlap check and for PlayerInteractor's raycast).</summary>
    public static void EnsureCollider(GameObject root, Vector2 footprint)
    {
        if (root.GetComponentInChildren<Collider>() != null) return;

        BoxCollider bc = root.AddComponent<BoxCollider>();
        Bounds b = CalculateRendererBounds(root);
        if (b.size.sqrMagnitude > 0.001f)
        {
            bc.center = root.transform.InverseTransformPoint(b.center);
            Vector3 ls = root.transform.lossyScale;
            bc.size = new Vector3(b.size.x / Mathf.Max(0.001f, ls.x),
                                  b.size.y / Mathf.Max(0.001f, ls.y),
                                  b.size.z / Mathf.Max(0.001f, ls.z));
        }
        else
        {
            bc.center = new Vector3(0f, 1f, 0f);
            bc.size   = new Vector3(footprint.x, 2f, footprint.y);
        }
    }

    static Bounds CalculateRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }
}
