using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.ModularKit
{
    /// <summary>
    /// The assembly engine: pure transform math + instantiation, usable from
    /// editor tools, runtime code and automation (MCP) alike. Editor callers
    /// can pass their own instantiate delegate (e.g. PrefabUtility.
    /// InstantiatePrefab) so prefab links survive; the default is a plain
    /// Object.Instantiate.
    /// </summary>
    public static class ModularAssembler
    {
        // ---- Compatibility -------------------------------------------------- //

        /// <summary>Type-level check. Without a table, only identical tags mate.</summary>
        public static bool AreCompatible(string typeA, string typeB,
                                         SnapCompatibilityTable table, out SnapAlignMode mode)
        {
            if (table != null) return table.TryGetAlign(typeA, typeB, out mode);
            mode = SnapAlignMode.FaceToFace;
            return !string.IsNullOrEmpty(typeA) && typeA == typeB;
        }

        /// <summary>First unoccupied point on the part that mates with the target.</summary>
        public static SnapPoint FindCompatiblePoint(GameObject part, SnapPoint target,
                                                    SnapCompatibilityTable table, out SnapAlignMode mode)
        {
            foreach (SnapPoint p in part.GetComponentsInChildren<SnapPoint>(true))
                if (!p.occupied && AreCompatible(p.pointType, target.pointType, table, out mode))
                    return p;
            mode = SnapAlignMode.FaceToFace;
            return null;
        }

        // ---- Alignment (the core math) --------------------------------------- //

        /// <summary>
        /// Moves/rotates an already-instantiated part so its snap point mates
        /// with the target point. rollSteps rotates in 90° increments around
        /// the mating axis (the points' shared forward), which for vertical
        /// points means yaw — that is how corners are turned.
        /// </summary>
        public static void AlignPart(Transform part, SnapPoint partPoint, SnapPoint targetPoint,
                                     SnapAlignMode mode, int rollSteps = 0)
        {
            Quaternion desired = targetPoint.transform.rotation;
            if (mode == SnapAlignMode.FaceToFace)
                desired *= Quaternion.AngleAxis(180f, Vector3.up);   // forward → −forward, up kept
            desired *= Quaternion.AngleAxis(90f * rollSteps, Vector3.forward); // roll about mating axis

            // Rotate the part so its point takes the desired world orientation,
            // then translate so the two points coincide.
            Quaternion pointLocal = Quaternion.Inverse(part.rotation) * partPoint.transform.rotation;
            part.rotation = desired * Quaternion.Inverse(pointLocal);
            part.position += targetPoint.transform.position - partPoint.transform.position;
        }

        // ---- Attach ----------------------------------------------------------- //

        /// <summary>
        /// Instantiates partPrefab under parent and snaps it onto targetPoint.
        /// partPointName selects the socket on the new part by GameObject name;
        /// null = first compatible point. Returns the instance (null on failure).
        /// </summary>
        public static GameObject AttachPart(Transform parent, GameObject partPrefab,
                                            SnapPoint targetPoint,
                                            string partPointName = null,
                                            int rollSteps = 0,
                                            SnapCompatibilityTable table = null,
                                            Func<GameObject, GameObject> instantiate = null)
        {
            if (partPrefab == null || targetPoint == null)
            {
                Debug.LogError("[ModularAssembler] AttachPart: prefab or target point is null.");
                return null;
            }

            instantiate = instantiate ?? (go => UnityEngine.Object.Instantiate(go));
            GameObject instance = instantiate(partPrefab);
            instance.name = partPrefab.name;
            if (parent != null) instance.transform.SetParent(parent, true);

            SnapPoint partPoint;
            SnapAlignMode mode;
            if (!string.IsNullOrEmpty(partPointName))
            {
                partPoint = FindPointByName(instance, partPointName);
                if (partPoint == null ||
                    !AreCompatible(partPoint.pointType, targetPoint.pointType, table, out mode))
                {
                    Debug.LogError($"[ModularAssembler] '{partPrefab.name}' has no compatible point " +
                                   $"named '{partPointName}' for target '{targetPoint.pointType}'.");
                    UnityEngine.Object.DestroyImmediate(instance);
                    return null;
                }
            }
            else
            {
                partPoint = FindCompatiblePoint(instance, targetPoint, table, out mode);
                if (partPoint == null)
                {
                    Debug.LogError($"[ModularAssembler] '{partPrefab.name}' has no point compatible " +
                                   $"with target '{targetPoint.pointType}'.");
                    UnityEngine.Object.DestroyImmediate(instance);
                    return null;
                }
            }

            AlignPart(instance.transform, partPoint, targetPoint, mode, rollSteps);
            partPoint.occupied = true;
            targetPoint.occupied = true;
            return instance;
        }

        // ---- Recipes ------------------------------------------------------------ //

        /// <summary>
        /// Rebuilds an AssemblyRecipe: root part at the origin of a fresh root
        /// GameObject, then each step attaches a part to a point of an earlier
        /// part (index 0 = the root part, 1 = first step's part, ...).
        /// </summary>
        public static GameObject AssembleFromRecipe(AssemblyRecipe recipe,
                                                    Transform parent = null,
                                                    Func<GameObject, GameObject> instantiate = null)
        {
            if (recipe == null || recipe.rootPart == null)
            {
                Debug.LogError("[ModularAssembler] Recipe or its root part is null.");
                return null;
            }

            instantiate = instantiate ?? (go => UnityEngine.Object.Instantiate(go));

            var rootGo = new GameObject(string.IsNullOrEmpty(recipe.displayName)
                                        ? recipe.name : recipe.displayName);
            if (parent != null) rootGo.transform.SetParent(parent, false);

            var placed = new List<GameObject>();
            GameObject first = instantiate(recipe.rootPart);
            first.name = recipe.rootPart.name;
            first.transform.SetParent(rootGo.transform, false);
            placed.Add(first);

            for (int i = 0; i < recipe.steps.Count; i++)
            {
                AssemblyRecipe.Step step = recipe.steps[i];
                if (step.targetPartIndex < 0 || step.targetPartIndex >= placed.Count)
                {
                    Debug.LogError($"[ModularAssembler] Recipe step {i}: target index " +
                                   $"{step.targetPartIndex} out of range (placed: {placed.Count}).");
                    continue;
                }

                SnapPoint target = FindPointByName(placed[step.targetPartIndex], step.targetPointName);
                if (target == null)
                {
                    Debug.LogError($"[ModularAssembler] Recipe step {i}: point " +
                                   $"'{step.targetPointName}' not found on part {step.targetPartIndex}.");
                    continue;
                }

                GameObject inst = AttachPart(rootGo.transform, step.partPrefab, target,
                                             step.partPointName, step.rollSteps,
                                             recipe.compatibilityTable, instantiate);
                if (inst != null) placed.Add(inst);
            }

            return rootGo;
        }

        /// <summary>Snap point lookup by its GameObject name (recipe convention).</summary>
        public static SnapPoint FindPointByName(GameObject part, string pointName)
        {
            foreach (SnapPoint p in part.GetComponentsInChildren<SnapPoint>(true))
                if (p.gameObject.name == pointName) return p;
            return null;
        }
    }
}
