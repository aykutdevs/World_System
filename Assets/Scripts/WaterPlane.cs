using UnityEngine;

/// <summary>
/// WILDCUT — the actual water surface: a single flat translucent plane placed
/// at the water line over the (now sandy) seabed terrain. Deliberately its own
/// GameObject/script so wave animation, foam, etc. can be added later.
///
/// It has NO collider and is never a NavMesh source (the NavMesh bake uses an
/// explicit terrain-mesh source list), so it affects neither physics nor AI.
/// PlayerController reads SurfaceY to switch into the Swimming state.
/// </summary>
public class WaterPlane : MonoBehaviour
{
    public static WaterPlane Instance { get; private set; }

    [Tooltip("Local size of the plane mesh in use (Unity's Plane primitive is 10×10).")]
    public float meshBaseSize = 10f;

    /// <summary>World height of the water surface.</summary>
    public float SurfaceY => transform.position.y;

    void OnEnable()  { Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    /// <summary>Called by MapDisplay after every terrain rebuild.</summary>
    public void Configure(float worldSurfaceY, float worldSize)
    {
        transform.position = new Vector3(0f, worldSurfaceY, 0f);
        float s = worldSize / Mathf.Max(0.01f, meshBaseSize);
        transform.localScale = new Vector3(s, 1f, s);
    }
}
