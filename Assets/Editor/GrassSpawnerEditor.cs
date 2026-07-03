using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GrassSpawner))]
public class GrassSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Spawn Grass"))
        {
            ((GrassSpawner)target).SpawnGrass();
        }
    }
}
