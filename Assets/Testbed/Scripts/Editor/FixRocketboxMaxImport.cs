using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class FixRocketboxMaxImport : AssetPostprocessor
{
    bool usingMixamoAnimations = true; 

    // Only Rocketbox content gets these fixes; every other model and texture (the Oculus LipSync demo, packages,
    // anything added later) imports with Unity's defaults. Assets/Testbed/Models is included because its
    // "Breathing Idle" clip is a Rocketbox (Bip01) animation that every study avatar uses.
    static readonly string[] RocketboxFolders = { "Assets/Avatars/", "Assets/Testbed/Models/", "Assets/Resources/Locomotion/" };

    bool IsRocketboxAsset => RocketboxFolders.Any(f => assetPath.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    void OnPostprocessMaterial(Material material)
    {
        if (!IsRocketboxAsset) return;

        // This fixes two problems with importing 3DSMax materials. The first is that the Max materials
        // assumed that diffuse material was set by the texture, whereas Unity multiplies the texture 
        // colour with the flat colour. 
        material.color = Color.white;
        // Second Unity's transparent  materials still show specular highlights and thus hair looks 
        // like glass sheets. The material mode "Fade" goes to full transparent. 
        if (material.GetFloat("_Mode") == 3f)
            material.SetFloat("_Mode", 2f);
    }

    void OnPostprocessMeshHierarchy(GameObject gameObject)
    {
        if (!IsRocketboxAsset) return;

        // This function selects only the highest resolution mesh as being activated by default.
        // You might choose another poly level (they are "hipoly", "midpoly", "lowpoly" and "ultralowpoly")
        // to be selected. Or you could choose not to import, by changing OnPreprocessMeshHierarchy
        if (gameObject.name.ToLower().Contains("poly") &&
            !gameObject.name.ToLower().Contains("hipoly"))
            gameObject.SetActive(false);
    }
    
    void OnPreprocessTexture()
    {
        if (!IsRocketboxAsset) return;

        // This function changes textures that are labelled with "normal" in their title to be loaded as 
        // NormalMaps. This just avoids a warning dialogue box that would otherwise fix it.
        if (assetPath.ToLower().Contains("normal"))
        {
            TextureImporter textureImporter = (TextureImporter)assetImporter;
            textureImporter.textureType = TextureImporterType.NormalMap;
            textureImporter.convertToNormalmap = false;
        }
    }

    void OnPostprocessModel(GameObject g)
    {
        if (!IsRocketboxAsset) return;

        if (g.transform.Find("Bip02") != null) RenameBip(g);

        // Only Rocketbox rigs (Bip01 root) need these fixes; skip other models such as the Oculus LipSync demo heads.
        Transform bip01 = g.transform.Find("Bip01");
        Transform pelvis = bip01 != null ? bip01.Find("Bip01 Pelvis") : null;
        if (pelvis == null) return;
        Transform spine2 = pelvis.Find("Bip01 Spine").Find("Bip01 Spine1").Find("Bip01 Spine2");
        Transform RClavicle = spine2.Find("Bip01 Neck").Find("Bip01 R Clavicle");
        Transform LClavicle = spine2.Find("Bip01 Neck").Find("Bip01 L Clavicle");


        if(!usingMixamoAnimations){
            pelvis.Find("Bip01 Spine").Find("Bip01 L Thigh").parent = pelvis;
            pelvis.Find("Bip01 Spine").Find("Bip01 R Thigh").parent = pelvis;
            LClavicle.parent = spine2;
            RClavicle.parent = spine2;


            LClavicle.rotation = new Quaternion(-0.7215106f, 0, 0, 0.6924035f);
            RClavicle.rotation = new Quaternion(0, -0.6925546f, 0.721365f, 0);
            LClavicle.Find("Bip01 L UpperArm").rotation = new Quaternion(0, 0, 0, 0);
            RClavicle.Find("Bip01 R UpperArm").rotation = new Quaternion(0, 0, 0, 0);
        }


        var importer = (ModelImporter)assetImporter;
        //If you need a humanoid avatar, change it here
        importer.animationType = ModelImporterAnimationType.Generic;
    }
    private void RenameBip(GameObject currentBone)
    {
        currentBone.name = currentBone.name.Replace("Bip02", "Bip01");
        for (int i = 0; i < currentBone.transform.childCount; i++)
        {
            RenameBip(currentBone.transform.GetChild(i).gameObject);
        }

    }
}
