using UnityEngine;
using System.Collections;
using UnityEditor;

[CustomEditor(typeof(MapGenerator))]
public class MapGeneratorEditor : Editor
{

    public override void OnInspectorGUI()
    {
        MapGenerator mapGen = (MapGenerator)target;

        if (DrawDefaultInspector())
        {
            if (mapGen.autoUpdate)
            {
                // Preview only: terrain mesh + colours on the SAME seed. Zones,
                // props, NavMesh and characters are intentionally skipped — they
                // would make every slider tick heavy. Full pipeline = Generate.
                mapGen.GenerateMap(reuseLastSeed: true, previewOnly: true);
            }
        }

        if (mapGen.autoUpdate)
            EditorGUILayout.HelpBox(
                "Auto Update = TERRAIN PREVIEW ONLY (same seed).\n" +
                "Settlement zones, props, NavMesh and player/enemy placement are NOT " +
                "recalculated while sliding. Click 'Generate' (or the World Automation " +
                "panel) to run the full pipeline.",
                MessageType.Info);

        if (mapGen.islandArchetype != null && !string.IsNullOrEmpty(mapGen.lastVariantInfo))
            EditorGUILayout.HelpBox(
                "Archetype active — noise/height/shape fields show the LAST VARIANT and are " +
                $"overwritten on each generation.\nCurrent: {mapGen.lastVariantInfo}",
                MessageType.None);

        if (GUILayout.Button("Generate (full pipeline)"))
        {
            mapGen.GenerateMap();
        }
    }
}