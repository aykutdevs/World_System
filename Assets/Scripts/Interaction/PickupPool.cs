using System.Collections;
using Core.Pooling;
using UnityEngine;

/// <summary>
/// WILDCUT binder for Core.Pooling — a ready pool for harvest-drop pickup
/// visuals (Chapter 7). Today a depleted node spawns one pooled marker at its
/// spot for a few seconds (proof the pool cycles Get/Release in real play);
/// when the teammate's inventory adds real ground pickups, they reuse this
/// pool with a proper pickup prefab instead of instantiating per drop.
/// Null-safe: no PickupPool in the scene = producers simply skip the visual.
/// </summary>
public class PickupPool : MonoBehaviour
{
    public static PickupPool Instance { get; private set; }

    [Tooltip("Pickup prefab. Empty = a small yellow sphere placeholder is built.")]
    public GameObject pickupPrefab;
    [Min(0)] public int prewarmCount = 8;
    [Tooltip("Seconds a spawned pickup stays visible before returning to the pool.")]
    [Min(0.5f)] public float lifeSeconds = 6f;
    public bool logPoolActivity = true;

    GameObjectPool pool;

    void OnEnable()  { Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    void Awake()
    {
        GameObject template = pickupPrefab != null ? pickupPrefab : BuildDefaultPickup();
        pool = new GameObjectPool(template, prewarmCount, transform);
    }

    /// <summary>Producer-side hook — safe to call with no pool in the scene.</summary>
    public static void TrySpawn(Vector3 position)
    {
        if (Instance != null) Instance.Spawn(position);
    }

    public void Spawn(Vector3 position)
    {
        GameObject go = pool.Get(position, Quaternion.identity);
        StartCoroutine(ReleaseLater(go));
        if (logPoolActivity)
            Debug.Log($"[PickupPool] Pickup spawned @ {position} (havuzda bekleyen: {pool.CountInactive}).");
    }

    IEnumerator ReleaseLater(GameObject go)
    {
        yield return new WaitForSeconds(lifeSeconds);
        if (go != null && go.activeSelf) pool.Release(go);
    }

    // Placeholder visual so the pool works with zero asset setup: a small
    // yellow sphere, collider stripped (must never block raycasts/physics).
    GameObject BuildDefaultPickup()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Pickup (placeholder)";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.35f;
        go.transform.SetParent(transform, false);

        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"))
        { color = new Color(1f, 0.85f, 0.2f) };

        go.SetActive(false);
        return go;
    }
}
