using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws the text on every avatar's augmentation card on top of the scene, like the card's background and seating
/// image already are (UIAlwaysOnTop material, ZTest Always). With the font's default material the text is depth
/// tested, so a hand or arm that passes in front of the card plane cuts holes in the text while the background stays
/// whole.
///
/// Tools/iXR/Card Text/Draw On Top gives every TextMeshPro text under the Canvas of each prefab in
/// Assets/Testbed/Prefabs/Avatars the material at OverlayMaterialPath: a copy of the font's own material (same atlas
/// and SDF settings) with the "TextMeshPro/Distance Field Overlay" shader (ZTest Always, Overlay queue). The material
/// is created on first use. Check reports what each prefab uses without changing anything; Restore Default Material
/// puts the font's own material back.
/// </summary>
public static class CardTextTools
{
    private const string AvatarFolder = "Assets/Testbed/Prefabs/Avatars";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    // Named "<font name> - ..." so TMP lists it under Material Preset for this font.
    private const string OverlayMaterialPath = "Assets/Testbed/Materials/LiberationSans SDF - Overlay.mat";
    private const string OverlayShaderName = "TextMeshPro/Distance Field Overlay";
    private const int ExpectedPrefabCount = 16;
    private const string Title = "Card Text";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    [MenuItem("Tools/iXR/Card Text/Draw On Top")]
    public static void DrawOnTop()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog(Title, $"Font asset not found at\n{FontPath}", "OK");
            return;
        }
        var overlay = GetOrCreateOverlayMaterial(font);
        if (overlay == null) return;

        EditCards("Draw On Top", save: true, texts =>
        {
            int changed = 0;
            var otherFont = new List<string>();
            foreach (var tmp in texts)
            {
                // The overlay material carries this font's atlas, so it can only be used with this font.
                if (tmp.font != font)
                {
                    otherFont.Add(tmp.name);
                    continue;
                }
                if (tmp.fontSharedMaterial == overlay) continue;
                tmp.fontSharedMaterial = overlay;
                EditorUtility.SetDirty(tmp);
                changed++;
            }
            string report = $"{changed} of {texts.Length} texts changed";
            if (otherFont.Count > 0)
                report += $"; skipped (not {font.name}): {string.Join(", ", otherFont)}";
            return (changed, report);
        });
    }

    [MenuItem("Tools/iXR/Card Text/Restore Default Material")]
    public static void RestoreDefault()
    {
        EditCards("Restore Default Material", save: true, texts =>
        {
            int changed = 0;
            foreach (var tmp in texts)
            {
                if (tmp.font == null || tmp.fontSharedMaterial == tmp.font.material) continue;
                tmp.fontSharedMaterial = tmp.font.material;
                EditorUtility.SetDirty(tmp);
                changed++;
            }
            return (changed, $"{changed} of {texts.Length} texts changed");
        });
    }

    [MenuItem("Tools/iXR/Card Text/Check")]
    public static void Check()
    {
        EditCards("Check", save: false, texts =>
        {
            var byMaterial = texts
                .GroupBy(tmp => tmp.fontSharedMaterial != null ? tmp.fontSharedMaterial.name : "(none)")
                .Select(g => $"{g.Count()} x {g.Key}");
            return (0, $"{texts.Length} texts: {string.Join(", ", byMaterial)}");
        });
    }

    /// <summary>
    /// Opens each avatar prefab, runs <paramref name="edit"/> on the TextMeshPro texts under its card Canvas, and saves
    /// the prefab when <paramref name="save"/> is set and the edit changed something. Shows a summary when done.
    /// </summary>
    private static void EditCards(string action, bool save, Func<TextMeshProUGUI[], (int changed, string report)> edit)
    {
        string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { AvatarFolder })
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .OrderBy(p => p)
            .ToArray();

        var lines = new List<string>();
        if (paths.Length != ExpectedPrefabCount)
            lines.Add($"WARNING: found {paths.Length} prefabs in {AvatarFolder}, expected {ExpectedPrefabCount}.");

        int savedPrefabs = 0;
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar($"{Title}: {action}", name, (float)i / paths.Length);

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var canvas = root.GetComponentInChildren<Canvas>(true);
                    if (canvas == null)
                    {
                        lines.Add($"{name}: no card Canvas, skipped");
                        continue;
                    }

                    var texts = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                    var (changed, report) = edit(texts);
                    if (save && changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                        if (ok) savedPrefabs++;
                        else report += " - SAVE FAILED";
                    }
                    lines.Add($"{name}: {report}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (save) AssetDatabase.SaveAssets();

        string summary = (save ? $"{savedPrefabs} prefab(s) saved.\n\n" : "") + string.Join("\n", lines);
        Debug.Log($"[CardTextTools] {action}\n{summary}");
        EditorUtility.DisplayDialog($"{Title}: {action}", summary, "OK");
    }

    /// <summary>
    /// The overlay text material: loaded if it exists (its shader and atlas are corrected if they no longer match),
    /// otherwise created as a copy of the font's own material with the overlay shader.
    /// </summary>
    private static Material GetOrCreateOverlayMaterial(TMP_FontAsset font)
    {
        var shader = Shader.Find(OverlayShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(Title, $"Shader '{OverlayShaderName}' not found. " +
                                               "Import the TMP Essential Resources and try again.", "OK");
            return null;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
        if (material == null)
        {
            // A copy of the font's material keeps its atlas and SDF settings; only the shader changes.
            material = new Material(font.material) { name = System.IO.Path.GetFileNameWithoutExtension(OverlayMaterialPath) };
            material.shader = shader;
            AssetDatabase.CreateAsset(material, OverlayMaterialPath);
            Debug.Log($"[CardTextTools] Created {OverlayMaterialPath}");
        }
        else if (material.shader != shader || material.GetTexture(MainTexId) != font.atlasTexture)
        {
            material.shader = shader;
            material.SetTexture(MainTexId, font.atlasTexture);
            EditorUtility.SetDirty(material);
            Debug.Log($"[CardTextTools] Updated the shader and atlas of {OverlayMaterialPath}");
        }
        return material;
    }
}
