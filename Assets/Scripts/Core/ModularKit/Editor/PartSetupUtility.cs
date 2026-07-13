using UnityEditor;
using UnityEngine;

namespace Core.ModularKit.EditorTools
{
    /// <summary>
    /// "Core ▸ ModularKit ▸ Setup Part" — turns a store-bought (or homemade)
    /// model into a kit-ready part in one step: wraps it so the pivot sits at
    /// the bounds' bottom centre, then sprinkles CANDIDATE SnapPoints on the
    /// side centres, top, bottom and the four bottom corners. The user then
    /// fixes tags / deletes extras in the Inspector — 80% automation is the
    /// goal, not perfection. Also saves the result to Assets/ModularParts/.
    /// </summary>
    public static class PartSetupUtility
    {
        const string PartsFolder = "Assets/ModularParts";

        [MenuItem("Core/ModularKit/Setup Part")]
        static void SetupSelected()
        {
            GameObject src = Selection.activeGameObject;
            if (src == null)
            {
                Debug.LogWarning("[ModularKit] Setup Part: select a model/prefab first.");
                return;
            }
            GameObject part = SetupPart(src);
            if (part != null) Selection.activeGameObject = part;
        }

        /// <summary>
        /// Public entry so automation (tests, MCP) can run the same flow.
        /// Accepts a scene object or an asset (assets are instantiated first).
        /// Returns the scene wrapper; a prefab is saved to Assets/ModularParts/.
        /// </summary>
        public static GameObject SetupPart(GameObject source)
        {
            GameObject sceneObj = source;
            if (!source.scene.IsValid())   // asset → work on a scene instance
            {
                sceneObj = (GameObject)PrefabUtility.InstantiatePrefab(source);
                if (sceneObj == null) sceneObj = Object.Instantiate(source);
            }

            if (sceneObj.GetComponentInChildren<SnapPoint>(true) != null)
            {
                Debug.LogWarning($"[ModularKit] '{sceneObj.name}' already has SnapPoints — not set up twice.");
                return sceneObj;
            }

            Bounds b = RendererBounds(sceneObj);
            if (b.size.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"[ModularKit] '{sceneObj.name}' has no renderers — cannot analyse bounds.");
                return null;
            }

            // Wrapper with the pivot at the bounds' bottom centre: modular maths
            // stays sane no matter how broken the imported pivot is.
            var wrapper = new GameObject($"{CleanName(sceneObj.name)}_Part");
            Undo.RegisterCreatedObjectUndo(wrapper, "ModularKit Setup Part");
            wrapper.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
            Undo.SetTransformParent(sceneObj.transform, wrapper.transform, "ModularKit Setup Part");

            float half = Mathf.Max(b.extents.x, b.extents.z);
            float size = Mathf.Clamp(half * 0.3f, 0.15f, 1f);

            // Candidates — side centres (outward), top (up), bottom (down),
            // and vertical corner posts (forward = up so roll steps = yaw).
            AddPoint(wrapper, "Snap_Xpos", new Vector3(b.extents.x, b.extents.y, 0), Vector3.right, Vector3.up, "edge", size);
            AddPoint(wrapper, "Snap_Xneg", new Vector3(-b.extents.x, b.extents.y, 0), Vector3.left, Vector3.up, "edge", size);
            AddPoint(wrapper, "Snap_Zpos", new Vector3(0, b.extents.y, b.extents.z), Vector3.forward, Vector3.up, "edge", size);
            AddPoint(wrapper, "Snap_Zneg", new Vector3(0, b.extents.y, -b.extents.z), Vector3.back, Vector3.up, "edge", size);
            AddPoint(wrapper, "Snap_Top", new Vector3(0, b.size.y, 0), Vector3.up, Vector3.forward, "top", size);
            AddPoint(wrapper, "Snap_Bottom", Vector3.zero, Vector3.down, Vector3.forward, "bottom", size);
            AddPoint(wrapper, "Snap_CornerA", new Vector3(b.extents.x, 0, b.extents.z), Vector3.up, Vector3.right, "corner", size * 0.8f);
            AddPoint(wrapper, "Snap_CornerB", new Vector3(-b.extents.x, 0, b.extents.z), Vector3.up, Vector3.left, "corner", size * 0.8f);
            AddPoint(wrapper, "Snap_CornerC", new Vector3(-b.extents.x, 0, -b.extents.z), Vector3.up, Vector3.left, "corner", size * 0.8f);
            AddPoint(wrapper, "Snap_CornerD", new Vector3(b.extents.x, 0, -b.extents.z), Vector3.up, Vector3.right, "corner", size * 0.8f);

            GameObject prefab = SaveAsPart(wrapper);
            Debug.Log($"[ModularKit] '{wrapper.name}' set up: pivot at base, 10 candidate SnapPoints " +
                      $"(fix tags / delete extras in the Inspector). Prefab: " +
                      $"{(prefab != null ? AssetDatabase.GetAssetPath(prefab) : "NOT saved")}");
            return wrapper;
        }

        static void AddPoint(GameObject parent, string name, Vector3 localPos,
                             Vector3 forward, Vector3 up, string type, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(forward, up);
            SnapPoint p = go.AddComponent<SnapPoint>();
            p.pointType = type;
            p.size = size;
        }

        static GameObject SaveAsPart(GameObject wrapper)
        {
            if (!AssetDatabase.IsValidFolder(PartsFolder))
                AssetDatabase.CreateFolder("Assets", "ModularParts");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{PartsFolder}/{wrapper.name}.prefab");
            return PrefabUtility.SaveAsPrefabAssetAndConnect(wrapper, path, InteractionMode.AutomatedAction);
        }

        static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        static string CleanName(string n) =>
            n.Replace("(Clone)", "").Replace(" ", "_").Trim('_');
    }
}
