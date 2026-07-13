using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Core.ModularKit.EditorTools
{
    /// <summary>
    /// "Core ▸ ModularKit ▸ Assembler" — click-to-snap assembly tool.
    /// Pick a part from the palette; every compatible, unoccupied SnapPoint
    /// under the assembly root lights up as a clickable sphere in the Scene
    /// view; clicking one snaps the part on (position + rotation). Supports
    /// Undo, 90° re-rolling of the last part, deleting the last part, saving
    /// the root as a prefab and exporting the session as an AssemblyRecipe.
    /// Deliberately plain IMGUI — an internal tool, not a product.
    /// </summary>
    public class AssemblerWindow : EditorWindow
    {
        const string PrefabFolder = "Assets/AssembledPrefabs";

        [MenuItem("Core/ModularKit/Assembler")]
        public static AssemblerWindow Open() => GetWindow<AssemblerWindow>("ModularKit Assembler");

        // ---- Session state (public-ish for automation via reflection-free calls) ----
        public Transform root;
        public SnapCompatibilityTable table;
        public GameObject selectedPart;
        public int rollSteps;

        // One entry per placed part — doubles as the recipe export source.
        class PlacedRecord
        {
            public GameObject instance;
            public GameObject sourcePrefab;
            public int targetPartIndex = -1;       // -1 = root part (no target)
            public string targetPointName, partPointName;
            public int rollSteps;
            public SnapPoint targetPoint, partPoint;
            public SnapAlignMode mode;
        }
        readonly List<PlacedRecord> placed = new List<PlacedRecord>();

        List<GameObject> palette = new List<GameObject>();
        string search = "";
        Vector2 scroll;

        void OnEnable()
        {
            RefreshPalette();
            if (table == null)
                table = AssetDatabase.FindAssets("t:SnapCompatibilityTable")
                    .Select(g => AssetDatabase.LoadAssetAtPath<SnapCompatibilityTable>(
                                     AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        public void RefreshPalette()
        {
            palette = AssetDatabase.FindAssets("t:Prefab")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null && p.GetComponentsInChildren<SnapPoint>(true).Length > 0)
                .OrderBy(p => p.name)
                .ToList();
        }

        // ------------------------------------------------------------------ //

        void OnGUI()
        {
            table = (SnapCompatibilityTable)EditorGUILayout.ObjectField("Compatibility Table", table,
                        typeof(SnapCompatibilityTable), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                root = (Transform)EditorGUILayout.ObjectField("Assembly Root", root, typeof(Transform), true);
                if (GUILayout.Button("New Root", GUILayout.Width(80))) NewRoot();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField("Search", search);
                if (GUILayout.Button("↻", GUILayout.Width(26))) RefreshPalette();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120));
            foreach (GameObject p in palette)
            {
                if (!string.IsNullOrEmpty(search) &&
                    p.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool sel = p == selectedPart;
                bool now = GUILayout.Toggle(sel, p.name, "Button");
                if (now && !sel) { selectedPart = p; SceneView.RepaintAll(); }
            }
            EditorGUILayout.EndScrollView();

            rollSteps = EditorGUILayout.IntSlider("Roll (90° steps)", rollSteps, 0, 3);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = root != null && selectedPart != null && placed.Count == 0;
                if (GUILayout.Button("Place First Part @ Origin")) PlaceFirstPart();
                GUI.enabled = placed.Count > 1 || (placed.Count == 1 && placed[0].targetPoint == null);
                if (GUILayout.Button("Rotate Last +90°")) RotateLast(1);
                if (GUILayout.Button("Delete Last")) DeleteLast();
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = root != null && root.childCount > 0;
                if (GUILayout.Button("Save as Prefab")) SaveAssemblyAsPrefab();
                GUI.enabled = placed.Count > 0;
                if (GUILayout.Button("Export Recipe")) ExportRecipe();
                GUI.enabled = true;
            }

            EditorGUILayout.HelpBox(
                selectedPart == null
                    ? "Paletten parça seç; sahnedeki uyumlu snap noktaları küre olarak vurgulanır — tıkla, parça oraya SNAP edilir."
                    : $"Seçili: {selectedPart.name} — Scene view'da yeşil kürelere tıkla.",
                MessageType.Info);
        }

        // ---- Scene interaction --------------------------------------------- //

        void OnSceneGUI(SceneView sv)
        {
            if (root == null || selectedPart == null) return;

            foreach (SnapPoint target in CompatibleTargets())
            {
                Handles.color = Color.green;
                float s = Mathf.Max(0.12f, target.size * 0.35f);
                if (Handles.Button(target.transform.position, Quaternion.identity,
                                   s, s * 1.4f, Handles.SphereHandleCap))
                {
                    AttachSelectedTo(target);
                    break;
                }
            }
        }

        /// <summary>Unoccupied points under the root the selected part can mate with.</summary>
        public List<SnapPoint> CompatibleTargets()
        {
            var result = new List<SnapPoint>();
            if (root == null || selectedPart == null) return result;

            var partTypes = selectedPart.GetComponentsInChildren<SnapPoint>(true)
                                        .Select(p => p.pointType).Distinct().ToList();
            foreach (SnapPoint t in root.GetComponentsInChildren<SnapPoint>(true))
            {
                if (t.occupied) continue;
                foreach (string pt in partTypes)
                    if (ModularAssembler.AreCompatible(pt, t.pointType, table, out _))
                    { result.Add(t); break; }
            }
            return result;
        }

        // ---- Operations (public so MCP/automation can drive the same flow) ---- //

        public void NewRoot()
        {
            var go = new GameObject("Assembly");
            Undo.RegisterCreatedObjectUndo(go, "New Assembly Root");
            root = go.transform;
            placed.Clear();
        }

        public GameObject PlaceFirstPart()
        {
            if (root == null || selectedPart == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(selectedPart);
            inst.transform.SetParent(root, false);
            inst.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(inst, "Place First Part");
            placed.Add(new PlacedRecord { instance = inst, sourcePrefab = selectedPart });
            SceneView.RepaintAll();
            return inst;
        }

        public GameObject AttachSelectedTo(SnapPoint target)
        {
            if (root == null || selectedPart == null || target == null) return null;

            Undo.RecordObject(target, "Attach Part");   // 'occupied' flag undo-safe
            GameObject inst = ModularAssembler.AttachPart(
                root, selectedPart, target, null, rollSteps, table,
                prefab => (GameObject)PrefabUtility.InstantiatePrefab(prefab));
            if (inst == null) return null;

            Undo.RegisterCreatedObjectUndo(inst, "Attach Part");

            SnapPoint partPoint = inst.GetComponentsInChildren<SnapPoint>(true)
                                      .FirstOrDefault(p => p.occupied);
            ModularAssembler.AreCompatible(partPoint != null ? partPoint.pointType : "",
                                           target.pointType, table, out SnapAlignMode mode);
            placed.Add(new PlacedRecord
            {
                instance = inst,
                sourcePrefab = selectedPart,
                targetPartIndex = OwnerIndex(target),
                targetPointName = target.gameObject.name,
                partPointName = partPoint != null ? partPoint.gameObject.name : "",
                rollSteps = rollSteps,
                targetPoint = target,
                partPoint = partPoint,
                mode = mode,
            });
            Debug.Log($"[Assembler] '{selectedPart.name}' → '{target.gameObject.name}' " +
                      $"(part {OwnerIndex(target)}, roll {rollSteps}).");
            SceneView.RepaintAll();
            return inst;
        }

        public void RotateLast(int deltaSteps)
        {
            if (placed.Count == 0) return;
            PlacedRecord r = placed[placed.Count - 1];
            if (r.targetPoint == null || r.partPoint == null) return;
            Undo.RecordObject(r.instance.transform, "Rotate Part");
            r.rollSteps = (r.rollSteps + deltaSteps) & 3;
            ModularAssembler.AlignPart(r.instance.transform, r.partPoint, r.targetPoint,
                                       r.mode, r.rollSteps);
            SceneView.RepaintAll();
        }

        public void DeleteLast()
        {
            if (placed.Count == 0) return;
            PlacedRecord r = placed[placed.Count - 1];
            if (r.targetPoint != null)
            {
                Undo.RecordObject(r.targetPoint, "Delete Part");
                r.targetPoint.occupied = false;
            }
            if (r.instance != null) Undo.DestroyObjectImmediate(r.instance);
            placed.RemoveAt(placed.Count - 1);
            SceneView.RepaintAll();
        }

        public GameObject SaveAssemblyAsPrefab()
        {
            if (root == null) return null;
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "AssembledPrefabs");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{root.name}.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                root.gameObject, path, InteractionMode.AutomatedAction);

            Bounds b = new Bounds(root.position, Vector3.zero);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Debug.Log($"[Assembler] Prefab saved: {path} — {root.childCount} parça, " +
                      $"bounds {b.size.x:F1}×{b.size.y:F1}×{b.size.z:F1}.");
            return prefab;
        }

        public AssemblyRecipe ExportRecipe()
        {
            if (placed.Count == 0) return null;
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "AssembledPrefabs");

            var recipe = CreateInstance<AssemblyRecipe>();
            recipe.displayName = root != null ? root.name : "Assembly";
            recipe.rootPart = placed[0].sourcePrefab;
            recipe.compatibilityTable = table;
            for (int i = 1; i < placed.Count; i++)
            {
                PlacedRecord r = placed[i];
                recipe.steps.Add(new AssemblyRecipe.Step
                {
                    partPrefab = r.sourcePrefab,
                    targetPartIndex = r.targetPartIndex,
                    targetPointName = r.targetPointName,
                    partPointName = r.partPointName,
                    rollSteps = r.rollSteps,
                });
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{PrefabFolder}/{recipe.displayName}_Recipe.asset");
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Assembler] Recipe exported: {path} ({recipe.steps.Count} adım).");
            return recipe;
        }

        int OwnerIndex(SnapPoint point)
        {
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].instance != null &&
                    point.transform.IsChildOf(placed[i].instance.transform)) return i;
            return -1;
        }
    }
}
