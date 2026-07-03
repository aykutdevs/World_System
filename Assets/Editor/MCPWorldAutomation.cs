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
            "Terrain (Y-grounded, scale-aware)  →  NavMesh baked on visible hills.\n" +
            "Grass and Props are disabled until NavMesh is confirmed working.",
            MessageType.Info);

        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.35f);
        if (GUILayout.Button("▶   Generate Full World", GUILayout.Height(44)))
            RunFullPipeline();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);

        // ---- Individual steps ----
        DrawSection("Individual Steps");

        if (GUILayout.Button("1 — Regenerate Terrain Mesh + Colors", GUILayout.Height(30)))
            RegenerateTerrain();

        if (GUILayout.Button("2 — Bake NavMesh on Scaled Terrain", GUILayout.Height(30)))
            BakeNavMesh();

        EditorGUILayout.Space(4);
        GUI.enabled = false;
        GUILayout.Button("— Spawn Grass (disabled)", GUILayout.Height(26));
        GUILayout.Button("— Spawn Props (disabled)", GUILayout.Height(26));
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

    static void InjectPropSpawner()
    {
        if (Object.FindObjectOfType<PropSpawner>() != null) return;

        MapGenerator gen = Object.FindObjectOfType<MapGenerator>();
        if (gen == null)
        {
            Debug.LogWarning("[WorldAutomation] Cannot inject PropSpawner — MapGenerator not found in scene.");
            return;
        }

        PropSpawner ps = Undo.AddComponent<PropSpawner>(gen.gameObject);
        ps.mapGenerator = gen;

        // Copy MeshFilter reference from GrassSpawner if available; fall back to MapDisplay.
        GrassSpawner gs = Object.FindObjectOfType<GrassSpawner>();
        if (gs != null)
            ps.terrainMeshFilter = gs.terrainMeshFilter;
        else
        {
            MapDisplay md = Object.FindObjectOfType<MapDisplay>();
            if (md != null) ps.terrainMeshFilter = md.meshFilter;
        }

        Debug.Log($"[WorldAutomation] PropSpawner auto-injected onto '{gen.gameObject.name}'.");
    }

    static void RegenerateTerrain()
    {
        MapGenerator gen = FindRequired<MapGenerator>("MapGenerator");
        MapDisplay display = FindRequired<MapDisplay>("MapDisplay");
        if (gen == null || display == null) return;

        float[,] noiseMap = Noise.GenerateNoiseMap(
            gen.mapWidth, gen.mapHeight, gen.seed, gen.noiseScale,
            gen.octaves, gen.persistance, gen.lacunarity, gen.offset);
        gen.latestNoiseMap = noiseMap;

        display.DrawMesh(
            MeshGenerator.GenerateTerrainMesh(noiseMap, gen.meshHeightMultiplier,
                                              gen.meshHeightCurve, gen.waterLevel),
            TextureGenerator.TextureFromColourMap(
                gen.BuildColourMap(noiseMap), gen.mapWidth, gen.mapHeight));

        Debug.Log("[WorldAutomation] Terrain mesh regenerated.");
    }

    static void SpawnGrass()
    {
        GrassSpawner gs = FindRequired<GrassSpawner>("GrassSpawner");
        if (gs != null) { gs.SpawnGrass(); Debug.Log("[WorldAutomation] Grass spawned."); }
    }

    static void SpawnProps()
    {
        PropSpawner ps = FindRequired<PropSpawner>("PropSpawner");
        if (ps != null) { ps.SpawnProps(); Debug.Log("[WorldAutomation] Biome props spawned."); }
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
        MapGenerator   gen     = Object.FindObjectOfType<MapGenerator>();
        GrassSpawner   grass   = Object.FindObjectOfType<GrassSpawner>();
        PropSpawner    props   = Object.FindObjectOfType<PropSpawner>();
        AISpawnManager ai      = Object.FindObjectOfType<AISpawnManager>();
        MapDisplay     display = Object.FindObjectOfType<MapDisplay>();

        bool hasMesh  = display?.meshFilter?.sharedMesh != null;
        bool hasProps = GameObject.Find("Procedural_Props") != null;
        bool hasAI    = GameObject.Find("AI_Spawn_Manager") != null;

        bool meshAtGround = false;
        if (display?.meshFilter != null)
            meshAtGround = Mathf.Approximately(display.meshFilter.transform.position.y, 0f);

        StatusLine("MapGenerator in scene",    gen     != null);
        StatusLine("Terrain mesh generated",   hasMesh);
        StatusLine("Terrain at Y = 0",         meshAtGround);
        StatusLine("GrassSpawner in scene",    grass   != null);
        StatusLine("PropSpawner in scene",     props   != null);
        StatusLine("Procedural_Props present", hasProps);
        StatusLine("AISpawnManager in scene",  ai      != null);
        StatusLine("AI_Spawn_Manager present", hasAI);

        // PropSpawner settings summary
        PropSpawner ps = Object.FindObjectOfType<PropSpawner>();
        if (ps != null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("PropSpawner (autonomous — no prefabs needed):",
                                       EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                $"    Trees: {ps.treeBaseChance * 100f:F0}% base chance, " +
                $"min spacing {ps.treeMinSpacing} u, " +
                $"slope ≥ {ps.minNormalY_Trees:F2} normal-Y",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"    Rocks: {ps.rockBaseChance * 100f:F0}% base chance, " +
                $"size {ps.rockSizeMin}-{ps.rockSizeMax}, " +
                $"slope ≥ {ps.minNormalY_Rocks:F2} normal-Y",
                EditorStyles.miniLabel);
        }
        else if (gen != null)
        {
            EditorGUILayout.HelpBox(
                "PropSpawner not in scene — it will be auto-injected when you click\n" +
                "'▶ Generate Full World'  or  '3 — Inject PropSpawner'.",
                MessageType.Warning);
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
