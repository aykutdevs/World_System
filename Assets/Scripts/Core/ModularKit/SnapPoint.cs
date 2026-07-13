using UnityEngine;

// ---- Core.ModularKit — modular asset snapping kit (reusable across projects) ----
// Parts (walls, roofs, doors, ship pieces...) carry SnapPoint children; the
// assembler mates two points by aligning position + rotation. No game code
// here: copy the Core/ModularKit folder (with its Editor subfolder) into any
// project and it works as-is.

namespace Core.ModularKit
{
    /// <summary>
    /// One attachment socket on a modular part — an empty child placed on the
    /// prefab. Convention: local FORWARD (+Z) is the outward-facing mating
    /// direction, local UP (+Y) disambiguates roll. pointType is a free-form
    /// tag ("wall_edge", "roof_seat", "door_slot"...); which tags mate with
    /// which — and how — lives in a SnapCompatibilityTable asset, so a new
    /// asset pack means new table rows, not code.
    /// </summary>
    public class SnapPoint : MonoBehaviour
    {
        [Tooltip("Free-form tag matched via the SnapCompatibilityTable " +
                 "(same tag mates with itself by default).")]
        public string pointType = "edge";

        [Tooltip("Approximate socket size in world units — gizmo scale and a " +
                 "hint for humans; alignment itself doesn't use it.")]
        [Min(0.01f)] public float size = 0.5f;

        [Tooltip("Set by the assembler once something is attached here — " +
                 "occupied points are not offered as snap targets.")]
        public bool occupied;

        /// <summary>Outward mating direction (world space).</summary>
        public Vector3 Direction => transform.forward;

        // Colour is derived from the tag so every type reads distinctly in the
        // Scene view without any configuration.
        public Color GizmoColor
        {
            get
            {
                if (string.IsNullOrEmpty(pointType)) return Color.white;
                float hue = Mathf.Abs(pointType.GetHashCode() % 1000) / 1000f;
                return Color.HSVToRGB(hue, 0.75f, 1f);
            }
        }

        void OnDrawGizmos()
        {
            Color c = GizmoColor;
            if (occupied) c.a = 0.25f;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, size * 0.25f);
            Gizmos.DrawRay(transform.position, transform.forward * size * 0.8f);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.5f);
            Gizmos.DrawRay(transform.position, transform.up * size * 0.4f);
        }
    }
}
