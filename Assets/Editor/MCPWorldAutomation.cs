using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Feature #6 — Procedural World System Automation Panel
/// Open via  Window ▶ World System ▶ Automation Panel
/// </summary>
public class MCPWorldAutomation : EditorWindow
{
    [MenuItem("Window/World System/Automation Panel")]
    static void Open() => GetWindow<MCPWorldAutomation>("World Automation");

    Vector2 scroll;

    const string AutoBakePref  = "WorldAutomation.AutoBakeAfterRegenerate";
    const string DebugViewPref = "WorldAutomation.DebugViewMode";
    const string FactorIdPref  = "WorldAutomation.DebugFactorId";

    static readonly string[] DebugViewNames = { "Normal", "Walkability", "Regions", "Factor" };

    // High-contrast palette for the Regions debug view (index = region order).
    static readonly Color[] RegionDebugColors =
    {
        new Color(0.05f, 0.15f, 0.60f),   // 0 deep water  — navy
        new Color(0.00f, 0.75f, 1.00f),   // 1 shallow     — cyan
        new Color(1.00f, 0.90f, 0.20f),   // 2 sand        — yellow
        new Color(0.20f, 0.85f, 0.20f),   // 3 grass       — bright green
        new Color(0.00f, 0.45f, 0.10f),   // 4 grass dark  — dark green
        new Color(1.00f, 0.50f, 0.00f),   // 5 rock        — orange
        new Color(0.85f, 0.20f, 0.85f),   // 6 rock high   — magenta
        new Color(1.00f, 1.00f, 1.00f),   // 7 snow        — white
        new Color(0.60f, 0.30f, 0.10f),   // extras, if more regions are added
        new Color(0.20f, 0.20f, 0.20f),
    };

    // ------------------------------------------------------------------ //
    //  Layout
    // ------------------------------------------------------------------ //

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawHeader("World System  |  Feature #6");

        // ---- Full pipeline ----
        DrawSection("Full Pipeline");
        EditorGUILayout.HelpBox(
            "Runs every step in the correct order:\n" +
            "Terrain (island falloff + slope smoothing)  →  Settlement zones  →\n" +
            "Grass + rule-based Props (auto-cleared & re-scattered)  →  NavMesh.",
            MessageType.Info);

        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.35f);
        if (GUILayout.Button("▶   Generate Full World", GUILayout.Height(44)))
            RunFullPipeline();
        GUI.backgroundColor = Color.white;

        // ---- Archetype variants ----
        MapGenerator genRef = Object.FindObjectOfType<MapGenerator>();
        if (genRef != null && genRef.islandArchetype != null)
        {
            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
            if (GUILayout.Button("🎲  Generate Archetype Variant  (full pipeline)", GUILayout.Height(32)))
                RunFullPipeline();   // random seed → new combination from the archetype ranges
            GUI.backgroundColor = Color.white;

            if (!string.IsNullOrEmpty(genRef.lastVariantInfo))
                EditorGUILayout.HelpBox(
                    $"Current Variant (seed {genRef.lastGeneratedSeed}):\n{genRef.lastVariantInfo}" +
                    (string.IsNullOrEmpty(genRef.lastLandmassReport)
                        ? "" : $"\n{genRef.lastLandmassReport}"),
                    MessageType.None);
        }
        else if (genRef != null)
        {
            EditorGUILayout.HelpBox(
                "No Island Archetype assigned on MapGenerator — fixed parameters in use.",
                MessageType.None);
        }

        EditorGUILayout.Space(10);

        // ---- Individual steps ----
        DrawSection("Individual Steps");

        if (GUILayout.Button("1 — Regenerate Terrain Mesh + Colors", GUILayout.Height(30)))
            RegenerateTerrain();

        bool autoBake = EditorPrefs.GetBool(AutoBakePref, false);
        bool newAutoBake = EditorGUILayout.ToggleLeft(
            "      Auto-bake NavMesh after regenerate", autoBake);
        if (newAutoBake != autoBake) EditorPrefs.SetBool(AutoBakePref, newAutoBake);

        if (GUILayout.Button("2 — Bake NavMesh on Scaled Terrain", GUILayout.Height(30)))
            BakeNavMesh();

        DrawSeedRow();

        EditorGUILayout.Space(4);

        if (GUILayout.Button("3 — Find Settlement Zones", GUILayout.Height(26)))
            FindSettlementZones();
        if (GUILayout.Button("4 — Spawn Villages (seed-deterministic)", GUILayout.Height(26)))
            SpawnVillages();
        if (GUILayout.Button("5 — Spawn Props (rule-based)", GUILayout.Height(26)))
            SpawnProps();

        GUI.enabled = false;
        GUILayout.Button("— Spawn Grass (rule-based; assign a prefab to Rule_Grass)", GUILayout.Height(26));
        GUILayout.Button("— Place AI Nodes (disabled)", GUILayout.Height(26));
        GUI.enabled = true;

        EditorGUILayout.Space(10);

        // ---- Utilities ----
        DrawSection("Utilities");

        GUI.backgroundColor = new Color(0.9f, 0.85f, 0.4f);
        if (GUILayout.Button("⚓  Force Terrain to Y = 0  (ground fix)", GUILayout.Height(28)))
            ForceGroundTerrain();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
        if (GUILayout.Button("Clear Props  (remove Procedural_Props)", GUILayout.Height(26)))
            ClearProps();
        if (GUILayout.Button("Clear AI Nodes  (remove AI_Spawn_Manager)", GUILayout.Height(26)))
            ClearAINodes();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);

        // ---- Debug views ----
        DrawSection("Debug View");
        DrawDebugViewControls();

        EditorGUILayout.Space(10);

        // ---- Scene status ----
        DrawSection("Scene Status");
        DrawStatus();

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ //
    //  Pipeline
    // ------------------------------------------------------------------ //

    static void RunFullPipeline()
    {
        MapGenerator gen = FindRequired<MapGenerator>("MapGenerator");
        if (gen == null) return;

        // Terrain generation + NavMesh bake only (grass/props disabled in MapGenerator).
        gen.GenerateMap();
        Debug.Log("[WorldAutomation] Terrain + NavMesh pipeline complete.");
    }

    static void RegenerateTerrain()
    {
        MapGenerator gen = FindRequired<MapGenerator>("MapGenerator");
        MapDisplay display = FindRequired<MapDisplay>("MapDisplay");
        if (gen == null || display == null) return;

        // Shared pipeline: handles random/fixed seed + map-concept falloff.
        float[,] noiseMap = gen.GenerateHeightMap();
        int texW = noiseMap.GetLength(0);
        int texH = noiseMap.GetLength(1);
        float vertexSpacing = 1f / Mathf.Max(1, gen.meshResolutionMultiplier);

        display.DrawMesh(
            MeshGenerator.GenerateTerrainMesh(noiseMap, gen.meshHeightMultiplier,
                                              gen.meshHeightCurve, gen.waterLevel, vertexSpacing),
            TextureGenerator.TextureFromColourMap(
                gen.BuildColourMap(noiseMap), texW, texH));

        Debug.Log($"[WorldAutomation] Terrain mesh regenerated (seed {gen.lastGeneratedSeed}).");

        // Auto-chained redistribution: zones → villages → props follow every new
        // island so old scatter never lingers. Grass is rule-based now (Rule_Grass
        // placement rule) and spawns via SpawnProps once a real grass prefab is assigned.
        FindSettlementZones();
        SpawnVillages();
        SpawnProps();

        // Keep the active debug overlay in sync with the fresh terrain.
        int debugMode = EditorPrefs.GetInt(DebugViewPref, 0);
        if (debugMode != 0) ApplyDebugView(debugMode);

        if (EditorPrefs.GetBool(AutoBakePref, false))
        {
            display.BakeNavMesh();
            Debug.Log("[WorldAutomation] Auto-baked NavMesh after regenerate.");
        }
    }

    // ------------------------------------------------------------------ //
    //  Debug views (Editor-only texture overrides — never ship in a build)
    // ------------------------------------------------------------------ //

    static void DrawDebugViewControls()
    {
        EditorGUILayout.HelpBox(
            "Normal = game colours.  Walkability = green (flat) → yellow → red (steep), blue water.\n" +
            "Regions = flat high-contrast colour per region band.\n" +
            "Factor = heatmap of an environmental factor (blue cold → red hot).",
            MessageType.None);

        int mode    = EditorPrefs.GetInt(DebugViewPref, 0);
        int newMode = GUILayout.Toolbar(mode, DebugViewNames, GUILayout.Height(24));
        if (newMode != mode)
        {
            EditorPrefs.SetInt(DebugViewPref, newMode);
            ApplyDebugView(newMode);
        }

        if (newMode == 3)
        {
            EditorGUILayout.BeginHorizontal();
            string fid    = EditorPrefs.GetString(FactorIdPref, "temperature");
            string newFid = EditorGUILayout.TextField("Show Factor:", fid);
            if (newFid != fid) EditorPrefs.SetString(FactorIdPref, newFid);
            if (GUILayout.Button("Apply", GUILayout.Width(60)))
                ApplyDebugView(3);
            EditorGUILayout.EndHorizontal();
        }

        SettlementZoneFinder zones = Object.FindObjectOfType<SettlementZoneFinder>();
        if (zones != null)
        {
            bool show = EditorGUILayout.ToggleLeft(
                $"Show Settlement Zones ({zones.zones.Count} gizmo circles)", zones.showGizmos);
            if (show != zones.showGizmos)
            {
                zones.showGizmos = show;
                EditorUtility.SetDirty(zones);
                SceneView.RepaintAll();
            }
        }
    }

    static void ApplyDebugView(int mode)
    {
        MapGenerator gen     = Object.FindObjectOfType<MapGenerator>();
        MapDisplay   display = Object.FindObjectOfType<MapDisplay>();
        if (gen == null || display == null || display.meshRenderer == null) return;
        if (gen.latestNoiseMap == null)
        {
            Debug.LogWarning("[WorldAutomation] No heightmap in memory — click '1 — Regenerate' first.");
            return;
        }

        float[,] noise = gen.latestNoiseMap;
        int w = noise.GetLength(0);
        int h = noise.GetLength(1);

        Texture2D tex;
        switch (mode)
        {
            case 1:  tex = BuildWalkabilityTexture(gen, display, noise, w, h); break;
            case 2:  tex = BuildRegionsTexture(gen, noise, w, h);              break;
            case 3:  tex = BuildFactorTexture(gen, display, noise, w, h);      break;
            default: tex = TextureGenerator.TextureFromColourMap(gen.BuildColourMap(noise), w, h); break;
        }
        if (tex == null) return;

        display.meshRenderer.sharedMaterial.mainTexture = tex;
        SceneView.RepaintAll();
        Debug.Log($"[WorldAutomation] Debug view: {DebugViewNames[mode]}.");
    }

    static Texture2D BuildWalkabilityTexture(MapGenerator gen, MapDisplay display,
                                             float[,] noise, int w, int h)
    {
        Mesh mesh = display.meshFilter != null ? display.meshFilter.sharedMesh : null;
        if (mesh == null || mesh.vertexCount != w * h)
        {
            Debug.LogWarning("[WorldAutomation] Mesh out of sync with heightmap — regenerate first.");
            return null;
        }

        Vector3[] normals = mesh.normals;
        Transform tf      = display.meshFilter.transform;

        Color water  = new Color(0.15f, 0.35f, 0.70f);
        Color flat   = new Color(0.15f, 0.75f, 0.25f);   // ≤ 20°: comfortable run/walk
        Color mid    = new Color(0.95f, 0.85f, 0.15f);   // ~ 35°
        Color steep  = new Color(0.90f, 0.15f, 0.10f);   // ≥ 50°: unwalkable

        Color[] cols = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (noise[x, y] < gen.waterLevel) { cols[i] = water; continue; }

                float ny    = Mathf.Clamp01(tf.TransformDirection(normals[i]).normalized.y);
                float angle = Mathf.Acos(ny) * Mathf.Rad2Deg;
                if      (angle <= 20f) cols[i] = flat;
                else if (angle <= 35f) cols[i] = Color.Lerp(flat, mid,   (angle - 20f) / 15f);
                else if (angle <= 50f) cols[i] = Color.Lerp(mid,  steep, (angle - 35f) / 15f);
                else                   cols[i] = steep;
            }
        return TextureGenerator.TextureFromColourMap(cols, w, h);
    }

    // Chapter 7 — heatmap of one environmental factor sampled per terrain vertex
    // (blue = min value on this island, red = max). Water cells stay dark blue.
    static Texture2D BuildFactorTexture(MapGenerator gen, MapDisplay display,
                                        float[,] noise, int w, int h)
    {
        WorldEnvironment env = Object.FindObjectOfType<WorldEnvironment>();
        if (env == null)
        {
            Debug.LogWarning("[WorldAutomation] No WorldEnvironment in scene — add one for the Factor view.");
            return null;
        }
        Mesh mesh = display.meshFilter != null ? display.meshFilter.sharedMesh : null;
        if (mesh == null || mesh.vertexCount != w * h)
        {
            Debug.LogWarning("[WorldAutomation] Mesh out of sync with heightmap — regenerate first.");
            return null;
        }

        string factorId = EditorPrefs.GetString(FactorIdPref, "temperature");
        if (!env.HasFactor(factorId))
        {
            Debug.LogWarning($"[WorldAutomation] Factor '{factorId}' not defined for this world " +
                             "(check the archetype's Environmental Factors list).");
            return null;
        }

        Vector3[] verts = mesh.vertices;
        Transform tf    = display.meshFilter.transform;

        // Two passes: measure the value range on land, then colour-map it.
        float[] values = new float[w * h];
        float vMin = float.MaxValue, vMax = float.MinValue;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (noise[x, y] < gen.waterLevel) continue;
                values[i] = env.GetFactor(factorId, tf.TransformPoint(verts[i]));
                vMin = Mathf.Min(vMin, values[i]);
                vMax = Mathf.Max(vMax, values[i]);
            }
        if (vMin >= vMax) vMax = vMin + 0.001f;

        Color water = new Color(0.08f, 0.15f, 0.35f);
        Color cold  = new Color(0.20f, 0.40f, 1.00f);
        Color midC  = new Color(0.95f, 0.95f, 0.30f);
        Color hot   = new Color(1.00f, 0.20f, 0.10f);

        Color[] cols = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (noise[x, y] < gen.waterLevel) { cols[i] = water; continue; }
                float t = Mathf.InverseLerp(vMin, vMax, values[i]);
                cols[i] = t < 0.5f ? Color.Lerp(cold, midC, t * 2f)
                                   : Color.Lerp(midC, hot, (t - 0.5f) * 2f);
            }

        Debug.Log($"[WorldAutomation] Factor '{factorId}' heatmap: min {vMin:F1} (blue) → max {vMax:F1} (red).");
        return TextureGenerator.TextureFromColourMap(cols, w, h);
    }

    static Texture2D BuildRegionsTexture(MapGenerator gen, float[,] noise, int w, int h)
    {
        Color[] cols = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = Color.black;
                for (int i = 0; i < gen.regions.Length; i++)
                    if (noise[x, y] <= gen.regions[i].height)
                    { c = RegionDebugColors[i % RegionDebugColors.Length]; break; }
                cols[y * w + x] = c;
            }
        Texture2D tex = TextureGenerator.TextureFromColourMap(cols, w, h);
        tex.filterMode = FilterMode.Point;   // crisp band edges for debugging
        return tex;
    }

    static void FindSettlementZones()
    {
        SettlementZoneFinder finder = Object.FindObjectOfType<SettlementZoneFinder>();
        if (finder == null)
        {
            Debug.LogWarning("[WorldAutomation] SettlementZoneFinder not in scene — skipped.");
            return;
        }
        finder.FindZones();
    }

    void DrawSeedRow()
    {
        MapGenerator gen = Object.FindObjectOfType<MapGenerator>();
        if (gen == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Last seed:  {gen.lastGeneratedSeed}" +
            (gen.useRandomSeed ? "   (random mode)" : "   (fixed mode)"),
            EditorStyles.miniLabel);
        if (GUILayout.Button("Copy Seed", GUILayout.Width(80)))
        {
            EditorGUIUtility.systemCopyBuffer = gen.lastGeneratedSeed.ToString();
            Debug.Log($"[WorldAutomation] Seed {gen.lastGeneratedSeed} copied to clipboard.");
        }
        EditorGUILayout.EndHorizontal();
    }

    // Procedural grass blades retired: grass now goes through the PlacementRule
    // system (Rule_Grass) like every other asset — no prefab, no spawn.

    static void SpawnVillages()
    {
        VillageSpawner vs = Object.FindObjectOfType<VillageSpawner>();
        if (vs == null)
        {
            Debug.LogWarning("[WorldAutomation] VillageSpawner not in scene — villages skipped.");
            return;
        }
        vs.SpawnVillages();
    }

    static void SpawnProps()
    {
        RuleBasedSpawner rs = FindRequired<RuleBasedSpawner>("RuleBasedSpawner");
        if (rs != null) { rs.SpawnAll(); Debug.Log("[WorldAutomation] Rule-based props spawned."); }
    }

    static void PlaceAINodes()
    {
        AISpawnManager ai = FindRequired<AISpawnManager>("AISpawnManager");
        if (ai != null) { ai.PlaceSpawnPoints(); Debug.Log("[WorldAutomation] AI nodes placed."); }
    }

    static void BakeNavMesh()
    {
        MapDisplay display = FindRequired<MapDisplay>("MapDisplay");
        if (display != null) { display.BakeNavMesh(); Debug.Log("[WorldAutomation] NavMesh baked."); }
    }

    // ------------------------------------------------------------------ //
    //  Utilities
    // ------------------------------------------------------------------ //

    static void ForceGroundTerrain()
    {
        MapDisplay display = Object.FindObjectOfType<MapDisplay>();
        if (display == null) { Debug.LogWarning("[WorldAutomation] No MapDisplay in scene."); return; }
        if (display.meshFilter == null) { Debug.LogWarning("[WorldAutomation] MapDisplay.meshFilter is null."); return; }

        Transform tf  = display.meshFilter.transform;
        Vector3   pos = tf.position;
        if (!Mathf.Approximately(pos.y, 0f))
        {
            Undo.RecordObject(tf, "Ground Terrain");
            tf.position = new Vector3(pos.x, 0f, pos.z);
            Debug.Log($"[WorldAutomation] Terrain moved from Y={pos.y:F2} to Y=0.");
        }
        else
        {
            Debug.Log("[WorldAutomation] Terrain is already at Y=0.");
        }
    }

    static void ClearProps()
    {
        GameObject go = GameObject.Find("Procedural_Props");
        if (go != null) { Undo.DestroyObjectImmediate(go); Debug.Log("[WorldAutomation] Procedural_Props cleared."); }
        else Debug.Log("[WorldAutomation] No Procedural_Props found.");
    }

    static void ClearAINodes()
    {
        GameObject go = GameObject.Find("AI_Spawn_Manager");
        if (go != null) { Undo.DestroyObjectImmediate(go); Debug.Log("[WorldAutomation] AI_Spawn_Manager cleared."); }
        else Debug.Log("[WorldAutomation] No AI_Spawn_Manager found.");
    }

    // ------------------------------------------------------------------ //
    //  Status panel
    // ------------------------------------------------------------------ //

    void DrawStatus()
    {
        MapGenerator         gen     = Object.FindObjectOfType<MapGenerator>();
        GrassSpawner         grass   = Object.FindObjectOfType<GrassSpawner>();
        RuleBasedSpawner     spawner = Object.FindObjectOfType<RuleBasedSpawner>();
        SettlementZoneFinder zones   = Object.FindObjectOfType<SettlementZoneFinder>();
        AISpawnManager       ai      = Object.FindObjectOfType<AISpawnManager>();
        MapDisplay           display = Object.FindObjectOfType<MapDisplay>();

        bool hasMesh  = display?.meshFilter?.sharedMesh != null;
        bool hasProps = GameObject.Find(RuleBasedSpawner.ROOT_NAME) != null;
        bool hasAI    = GameObject.Find("AI_Spawn_Manager") != null;

        bool meshAtGround = false;
        if (display?.meshFilter != null)
            meshAtGround = Mathf.Approximately(display.meshFilter.transform.position.y, 0f);

        StatusLine("MapGenerator in scene",       gen     != null);
        StatusLine("Terrain mesh generated",      hasMesh);
        StatusLine("Terrain at Y = 0",            meshAtGround);
        StatusLine("GrassSpawner in scene",       grass   != null);
        StatusLine("RuleBasedSpawner in scene",   spawner != null);
        StatusLine("Procedural_Props present",    hasProps);
        StatusLine("SettlementZoneFinder in scene", zones != null);
        StatusLine("AISpawnManager in scene",     ai      != null);
        StatusLine("AI_Spawn_Manager present",    hasAI);

        if (spawner != null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                $"Placement rules: {spawner.rules.Count} " +
                "(add assets via Create ▸ WILDCUT ▸ Placement Rule)",
                EditorStyles.miniBoldLabel);
        }
        if (zones != null)
        {
            string zoneInfo = zones.zones.Count == 0
                ? "Settlement zones: none found yet (regenerate terrain)"
                : $"Settlement zones: {zones.zones.Count} reserved (yellow circles in Scene view)";
            EditorGUILayout.LabelField(zoneInfo, EditorStyles.miniBoldLabel);
        }

        VillageSpawner villageSp = Object.FindObjectOfType<VillageSpawner>();
        StatusLine("VillageSpawner in scene", villageSp != null);
        if (villageSp != null)
        {
            int vbuildings = 0;
            foreach (VillageSpawner.SpawnedVillage v in villageSp.villages) vbuildings += v.buildingCount;
            EditorGUILayout.LabelField(
                $"Villages: {villageSp.villages.Count} ({vbuildings} buildings, orange circles in Scene view)",
                EditorStyles.miniBoldLabel);
        }

        // Repaint continuously so status stays live
        Repaint();
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    static void DrawHeader(string title)
    {
        EditorGUILayout.Space(6);
        var s = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField(title, s, GUILayout.Height(26));
        EditorGUILayout.Space(4);
    }

    static void DrawSection(string title)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        Rect r = GUILayoutUtility.GetLastRect();
        r.y    += EditorGUIUtility.singleLineHeight;
        r.height = 1f;
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(4);
    }

    static void StatusLine(string label, bool ok)
    {
        GUI.color = ok ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.45f, 0.45f);
        EditorGUILayout.LabelField($"  {(ok ? "✓" : "✗")}  {label}");
        GUI.color = Color.white;
    }

    static T FindRequired<T>(string label) where T : Object
    {
        T obj = Object.FindObjectOfType<T>();
        if (obj == null)
            Debug.LogWarning($"[WorldAutomation] '{label}' component not found in scene.");
        return obj;
    }
}
