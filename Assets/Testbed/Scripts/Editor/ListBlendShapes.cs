using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ListBlendShapes : MonoBehaviour
{
    [MenuItem("Tools/List Blendshapes on Selected Mesh")]
    static void Run()
    {
        var go = Selection.activeGameObject;
        if (!go) { Debug.Log("Select a GameObject with a SkinnedMeshRenderer"); return; }

        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (!smr) { Debug.Log("No SkinnedMeshRenderer on selected object"); return; }

        var mesh = smr.sharedMesh;
        if (!mesh) { Debug.Log("No mesh assigned to SkinnedMeshRenderer"); return; }

        Debug.Log($"Blendshapes on {mesh.name} ({mesh.blendShapeCount} total):");
        for (int i = 0; i < mesh.blendShapeCount; i++)
            Debug.Log($"[{i}] {mesh.GetBlendShapeName(i)}");
    }
}
