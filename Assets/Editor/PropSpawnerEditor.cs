using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PropSpawner))]
public class PropSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Spawn Props"))
            ((PropSpawner)target).SpawnProps();
    }
}
