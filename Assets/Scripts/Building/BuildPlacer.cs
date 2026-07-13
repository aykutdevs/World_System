using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT Chapter 8, flow steps 1-3 — plan selection, ghost preview, placement.
/// B toggles build mode; the selected BuildingPlan's ghost follows the aim point
/// on the terrain, tinted GREEN (valid) or RED (slope too steep / in water /
/// overlapping another structure). Left click places: the ground is locally
/// flattened, the structure starts as a ConstructionSite ("Malzeme ver") and
/// the NavMesh is re-baked. Settlement zones are NOT mandatory — the HUD just
/// marks "uygun bölge" while inside one (future village logic hooks in there).
/// New buildings = new BuildingPlan assets in the Plans list. No code.
/// </summary>
public class BuildPlacer : MonoBehaviour
{
    [Header("Input")]
    public KeyCode buildModeKey = KeyCode.B;
    public KeyCode cyclePlanKey = KeyCode.Tab;

    [Header("Plans (data-driven — add BuildingPlan assets here)")]
    public List<BuildingPlan> plans = new List<BuildingPlan>();

    [Header("Placement")]
    [Tooltip("Max distance from the camera the ghost can be placed at.")]
    public float placeRange = 14f;
    [Tooltip("World clearance above the water surface the WHOLE footprint must keep.")]
    public float minHeightAboveWater = 0.3f;

    public const string ROOT_NAME = "Placed_Buildings";

    public bool BuildMode  { get; private set; }
    public bool GhostValid { get; private set; }
    /// <summary>Ghost currently inside a settlement zone (informational, not required).</summary>
    public bool InSettlementZone { get; private set; }

    int        planIndex;
    GameObject ghost;
    Renderer[] ghostRenderers;
    Material   ghostMatValid, ghostMatInvalid;
    float      ghostGroundY;
    Camera     cachedCamera;
    SettlementZoneFinder zones;
    string     invalidReason;

    BuildingPlan ActivePlan =>
        (plans.Count > 0 && planIndex < plans.Count) ? plans[planIndex] : null;

    // ------------------------------------------------------------------ //

    void Update()
    {
        if (Input.GetKeyDown(buildModeKey)) SetBuildMode(!BuildMode);
        if (!BuildMode) return;

        if (Input.GetKeyDown(cyclePlanKey)) CyclePlan();

        UpdateGhost();

        if (GhostValid && Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
            PlaceNow();
    }

    /// <summary>Public for MCP play-mode tests (toggle without keyboard).</summary>
    public void SetBuildMode(bool on)
    {
        if (on && ActivePlan == null)
        {
            Debug.LogWarning("[BuildPlacer] No BuildingPlan assets in the Plans list.");
            return;
        }
        BuildMode = on;
        if (!on) DestroyGhost();
        Debug.Log($"[BuildPlacer] Build mode {(on ? "ON" : "OFF")}" +
                  (on ? $" — plan: '{ActivePlan.displayName}'" : "") + ".");
    }

    void CyclePlan()
    {
        if (plans.Count < 2) return;
        planIndex = (planIndex + 1) % plans.Count;
        DestroyGhost();   // rebuild with the new plan's shape next frame
        Debug.Log($"[BuildPlacer] Plan: '{ActivePlan.displayName}'.");
    }

    // ---- Ghost ----------------------------------------------------------- //

    void UpdateGhost()
    {
        BuildingPlan plan = ActivePlan;
        if (plan == null) return;

        if (ghost == null) CreateGhost(plan);
        if (ghost == null) return;

        Transform cam = ResolveCamera();
        if (cam == null) return;

        // Terrain = the procedural MeshCollider (project-wide convention).
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, placeRange) ||
            !(hit.collider is MeshCollider))
        {
            ghost.SetActive(false);
            GhostValid    = false;
            invalidReason = "Menzilde zemin yok";
            return;
        }

        ghost.SetActive(true);

        // Building yaw follows the player's view direction.
        float yaw = cam.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        GhostValid = ValidatePlacement(plan, hit.point, rot, out ghostGroundY, out invalidReason);
        InSettlementZone = IsInSettlementZone(hit.point);

        ghost.transform.SetPositionAndRotation(
            new Vector3(hit.point.x, GhostValid ? ghostGroundY : hit.point.y, hit.point.z), rot);

        Material mat = GhostValid ? ghostMatValid : ghostMatInvalid;
        foreach (Renderer r in ghostRenderers)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    void CreateGhost(BuildingPlan plan)
    {
        GameObject source = plan.ghostPrefab != null ? plan.ghostPrefab : plan.finalPrefab;
        if (source == null)
        {
            Debug.LogWarning($"[BuildPlacer] Plan '{plan.displayName}' has no prefab assigned.");
            return;
        }

        EnsureGhostMaterials();

        ghost = Instantiate(source);
        ghost.name = $"Ghost_{plan.displayName}";

        // Preview only: no colliders (would block the aim ray and overlap checks),
        // no gameplay components.
        foreach (Collider c in ghost.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (MonoBehaviour m in ghost.GetComponentsInChildren<MonoBehaviour>()) Destroy(m);

        ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
    }

    void DestroyGhost()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostRenderers = null;
        GhostValid = false;
    }

    void EnsureGhostMaterials()
    {
        if (ghostMatValid != null) return;
        // Legacy transparent shader: reliable alpha without Standard's keyword dance.
        Shader sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        ghostMatValid   = new Material(sh) { color = new Color(0.25f, 1f, 0.35f, 0.45f) };
        ghostMatInvalid = new Material(sh) { color = new Color(1f, 0.2f, 0.15f, 0.45f) };
    }

    // ---- Validity ---------------------------------------------------------- //

    bool ValidatePlacement(BuildingPlan plan, Vector3 center, Quaternion rot,
                           out float groundY, out string reason)
    {
        groundY = center.y;
        float hx = plan.footprintSize.x * 0.5f;
        float hz = plan.footprintSize.y * 0.5f;

        // Sample the ground under the centre + the 4 rotated footprint corners.
        Vector3[] offsets =
        {
            Vector3.zero,
            rot * new Vector3( hx, 0f,  hz),
            rot * new Vector3(-hx, 0f,  hz),
            rot * new Vector3( hx, 0f, -hz),
            rot * new Vector3(-hx, 0f, -hz),
        };

        float minY = float.MaxValue, maxY = float.MinValue, sumY = 0f;
        foreach (Vector3 off in offsets)
        {
            Vector3 origin = center + off + Vector3.up * 40f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f) ||
                !(hit.collider is MeshCollider))
            {
                reason = "Zemin köşede kesiliyor";
                return false;
            }
            minY  = Mathf.Min(minY, hit.point.y);
            maxY  = Mathf.Max(maxY, hit.point.y);
            sumY += hit.point.y;
        }
        groundY = sumY / offsets.Length;

        // Water: the lowest footprint corner must clear the water surface.
        float waterY = WaterPlane.Instance != null ? WaterPlane.Instance.SurfaceY : float.NegativeInfinity;
        if (minY < waterY + minHeightAboveWater)
        {
            reason = "Suda / su kenarında";
            return false;
        }

        // Slope: height spread across the footprint vs the allowed angle.
        float maxSpread = Mathf.Tan(plan.maxGroundSlopeAngle * Mathf.Deg2Rad)
                        * Mathf.Max(plan.footprintSize.x, plan.footprintSize.y);
        if (maxY - minY > maxSpread)
        {
            reason = "Eğim çok dik";
            return false;
        }

        // Overlap with existing structures (anything under a PlacedBuilding).
        Vector3 halfExtents = new Vector3(hx * 0.9f, 2f, hz * 0.9f);
        foreach (Collider c in Physics.OverlapBox(center + Vector3.up * 2f, halfExtents, rot))
        {
            if (c.GetComponentInParent<PlacedBuilding>() != null)
            {
                reason = "Başka yapıyla çakışıyor";
                return false;
            }
        }

        reason = null;
        return true;
    }

    bool IsInSettlementZone(Vector3 pos)
    {
        if (zones == null) zones = FindObjectOfType<SettlementZoneFinder>();
        return zones != null && zones.IsInsideAnyZone(pos);
    }

    // ---- Placement ---------------------------------------------------------- //

    /// <summary>Places the current ghost if valid. Public for MCP play-mode tests.</summary>
    public bool PlaceNow()
    {
        BuildingPlan plan = ActivePlan;
        if (!BuildMode || !GhostValid || ghost == null || plan == null) return false;

        Vector3    pos = new Vector3(ghost.transform.position.x, ghostGroundY, ghost.transform.position.z);
        Quaternion rot = ghost.transform.rotation;

        MapDisplay display = FindObjectOfType<MapDisplay>();

        // 1 — flatten the ground to the building base (axis-aligned bounding box
        //     of the rotated footprint, plus a blend ring).
        if (plan.flattenGround && display != null)
        {
            Vector2 rotatedAabb = RotatedFootprintAabb(plan.footprintSize, rot);
            TerrainFlattener.FlattenArea(display, pos, rotatedAabb, pos.y);
        }

        // 2 — spawn the structure in "under construction" state.
        GameObject sourcePrefab = plan.constructionPrefab != null ? plan.constructionPrefab : plan.finalPrefab;
        GameObject building = Instantiate(sourcePrefab, pos, rot, GetRoot().transform);
        building.name = $"{plan.displayName} (inşaat)";
        building.AddComponent<PlacedBuilding>().plan = plan;
        PlacedBuilding.EnsureCollider(building, plan.footprintSize);

        ConstructionSite site = building.AddComponent<ConstructionSite>();
        site.plan = plan;
        site.swapToFinalOnComplete = plan.constructionPrefab != null;

        // Two guards against stacking a second building on this one:
        // the new collider is not queryable until physics syncs, and GhostValid
        // is only recomputed in Update — so sync now and drop the cached flag
        // (next UpdateGhost re-validates against the building just placed).
        Physics.SyncTransforms();
        GhostValid = false;

        // 3 — refresh the NavMesh on the modified terrain (full bake — the island
        //     mesh is small enough that a partial update isn't worth the machinery).
        if (display != null) display.BakeNavMesh();

        Debug.Log($"[BuildPlacer] '{plan.displayName}' yerleştirildi @ {pos}" +
                  (InSettlementZone ? " (settlement zone içinde)" : "") + ".");
        return true;
    }

    /// <summary>
    /// F2.4 — programmatic placement for procedural content (VillageSpawner).
    /// Same validity rules and ground flattening as the player flow, but the
    /// structure spawns already COMPLETED (no ConstructionSite) and the caller
    /// owns parenting and the NavMesh bake (batch once after all placements).
    /// Returns the instance, or null when the spot fails validation.
    /// </summary>
    public GameObject PlaceCompletedAt(BuildingPlan plan, Vector3 approxPos, float yawDegrees,
                                       Transform parent = null)
    {
        if (plan == null || plan.finalPrefab == null) return null;

        Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);
        if (!ValidatePlacement(plan, approxPos, rot, out float groundY, out _)) return null;

        Vector3 pos = new Vector3(approxPos.x, groundY, approxPos.z);

        MapDisplay display = FindObjectOfType<MapDisplay>();
        if (plan.flattenGround && display != null)
            TerrainFlattener.FlattenArea(display, pos,
                RotatedFootprintAabb(plan.footprintSize, rot), pos.y);

        GameObject building = Instantiate(plan.finalPrefab, pos, rot,
                                          parent != null ? parent : GetRoot().transform);
        building.name = plan.displayName;
        building.AddComponent<PlacedBuilding>().plan = plan;
        PlacedBuilding.EnsureCollider(building, plan.footprintSize);

        // Same stacking guard as PlaceNow: make the new collider queryable so the
        // NEXT programmatic placement's overlap check sees this building.
        Physics.SyncTransforms();
        return building;
    }

    /// <summary>Public: GameSaveController reuses this when restoring saved buildings.</summary>
    public static Vector2 RotatedFootprintAabb(Vector2 size, Quaternion rot)
    {
        Vector3 x = rot * new Vector3(size.x, 0f, 0f);
        Vector3 z = rot * new Vector3(0f, 0f, size.y);
        return new Vector2(Mathf.Abs(x.x) + Mathf.Abs(z.x),
                           Mathf.Abs(x.z) + Mathf.Abs(z.z));
    }

    /// <summary>Public: GameSaveController parents restored buildings under the same root.</summary>
    public static GameObject GetRoot()
    {
        GameObject root = GameObject.Find(ROOT_NAME);
        if (root == null) root = new GameObject(ROOT_NAME);
        return root;
    }

    /// <summary>Called by MapGenerator before each full regenerate — buildings
    /// belong to the old island's terrain, so they never survive a new one.</summary>
    public static void ClearPlacedBuildings()
    {
        GameObject root = GameObject.Find(ROOT_NAME);
        if (root == null) return;
        if (Application.isPlaying)
        {
            // Destroy is deferred to end of frame — rename first so a GetRoot()
            // in the SAME frame (save/load restores buildings right after the
            // world regenerate) creates a fresh root instead of finding this
            // dying one and losing its children with it. Deactivate too: the
            // dying colliders otherwise still deflect the raycast/overlap checks
            // of same-frame placements (F2.4 village determinism on load).
            root.name = ROOT_NAME + " (clearing)";
            root.SetActive(false);
            Destroy(root);
        }
        else DestroyImmediate(root);
        Debug.Log("[BuildPlacer] Placed buildings cleared (new island).");
    }

    Transform ResolveCamera()
    {
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            cachedCamera = Camera.main;
        return cachedCamera != null ? cachedCamera.transform : null;
    }

    // ---- HUD ------------------------------------------------------------------ //

    void OnGUI()
    {
        if (!BuildMode) return;

        BuildingPlan plan = ActivePlan;
        if (plan == null) return;

        var style = new GUIStyle(GUI.skin.box)
        { alignment = TextAnchor.MiddleLeft, fontSize = 13, fontStyle = FontStyle.Bold };

        string status = GhostValid ? "<YEŞİL — yerleştirilebilir>" : $"<KIRMIZI — {invalidReason}>";
        string zone   = InSettlementZone ? "\n★ Uygun bölge (settlement zone)" : "";
        string text   = $"İNŞA MODU — {plan.displayName}\n{status}{zone}\n" +
                        $"Sol tık: yerleştir   {cyclePlanKey}: plan değiştir   {buildModeKey}: çık";

        GUI.Box(new Rect(12, Screen.height - 96, 380, 84), text, style);
    }
}
