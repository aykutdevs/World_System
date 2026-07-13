using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.ModularKit
{
    /// <summary>How two mated snap points are oriented relative to each other.</summary>
    public enum SnapAlignMode
    {
        /// <summary>Points face each other: part point forward = −target forward
        /// (walls butting, a roof socket dropping onto an upward seat...).
        /// Roll steps rotate around the shared mating axis.</summary>
        FaceToFace,
        /// <summary>Part point copies the target point's orientation (both
        /// forwards same direction — e.g. vertical corner posts where roll
        /// steps become 90° yaw).</summary>
        Aligned,
    }

    /// <summary>
    /// Data-only compatibility rules: which pointType mates with which, and in
    /// which alignment mode. Identical tags mate FaceToFace by default even
    /// without a row; add a row to pair different tags ("door_slot" ↔
    /// "door_frame") or to override the mode for a same-tag pair. Adding a new
    /// asset pack = adding rows here — no code changes.
    /// Create via: Assets ▸ Create ▸ Core ▸ ModularKit ▸ Snap Compatibility Table.
    /// </summary>
    [CreateAssetMenu(menuName = "Core/ModularKit/Snap Compatibility Table",
                     fileName = "SnapCompatibilityTable")]
    public class SnapCompatibilityTable : ScriptableObject
    {
        [Serializable]
        public class Rule
        {
            public string typeA;
            public string typeB;
            public SnapAlignMode align = SnapAlignMode.FaceToFace;
        }

        [Tooltip("Explicit pairs. Order of A/B does not matter.")]
        public List<Rule> rules = new List<Rule>();

        [Tooltip("When on, two points with the SAME tag mate FaceToFace even " +
                 "without an explicit rule (a rule for the pair overrides the mode).")]
        public bool sameTypeCompatibleByDefault = true;

        public bool TryGetAlign(string a, string b, out SnapAlignMode mode)
        {
            foreach (Rule r in rules)
            {
                if ((Matches(r.typeA, a) && Matches(r.typeB, b)) ||
                    (Matches(r.typeA, b) && Matches(r.typeB, a)))
                {
                    mode = r.align;
                    return true;
                }
            }

            if (sameTypeCompatibleByDefault && !string.IsNullOrEmpty(a) && a == b)
            {
                mode = SnapAlignMode.FaceToFace;
                return true;
            }

            mode = SnapAlignMode.FaceToFace;
            return false;
        }

        static bool Matches(string ruleType, string t) =>
            !string.IsNullOrEmpty(ruleType) &&
            string.Equals(ruleType, t, StringComparison.OrdinalIgnoreCase);
    }
}
