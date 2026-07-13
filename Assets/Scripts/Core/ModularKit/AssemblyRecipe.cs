using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.ModularKit
{
    /// <summary>
    /// A building described as DATA: a root part plus ordered "attach part X
    /// to point P of earlier part N" steps. The same recipe drives manual
    /// re-assembly, procedural generation and MCP automation — run it via
    /// ModularAssembler.AssembleFromRecipe. The Assembler window can export
    /// one from a hand-built session.
    /// Create via: Assets ▸ Create ▸ Core ▸ ModularKit ▸ Assembly Recipe.
    /// </summary>
    [CreateAssetMenu(menuName = "Core/ModularKit/Assembly Recipe",
                     fileName = "NewAssemblyRecipe")]
    public class AssemblyRecipe : ScriptableObject
    {
        [Serializable]
        public class Step
        {
            [Tooltip("Prefab of the part this step adds.")]
            public GameObject partPrefab;
            [Tooltip("Which earlier part owns the target point: 0 = root part, " +
                     "1 = part added by step 1, ...")]
            public int targetPartIndex;
            [Tooltip("GameObject name of the SnapPoint on the target part.")]
            public string targetPointName;
            [Tooltip("GameObject name of the SnapPoint on the NEW part. " +
                     "Empty = first compatible point.")]
            public string partPointName;
            [Tooltip("90° roll steps around the mating axis (vertical points: yaw).")]
            public int rollSteps;
        }

        public string displayName = "Assembly";
        [Tooltip("First part, placed at the assembly origin.")]
        public GameObject rootPart;
        [Tooltip("Rules used while attaching (types + alignment modes).")]
        public SnapCompatibilityTable compatibilityTable;
        public List<Step> steps = new List<Step>();
    }
}
