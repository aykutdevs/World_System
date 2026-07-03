using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AISpawnManager))]
public class AISpawnManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Place Cannibal Spawn Points"))
            ((AISpawnManager)target).PlaceSpawnPoints();
    }
}
