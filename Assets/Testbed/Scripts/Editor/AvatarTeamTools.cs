using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Assigns the proposed avatar team partition to the MasterLevelController in the open scene
/// (use in "Master Level Control" scene). The partition was derived from an acoustic analysis of
/// all bundled Piper voices (calibration passage, 2026-08): teams are matched on median pitch (F0),
/// pitch variability, and accent (one British voice per team), with 2M+2F per team. Speaking rate
/// is equalized via per-prefab lengthScale calibration and loudness via runtime RMS normalization
/// in CrossPlatformTTS, so neither differs systematically between teams.
///
/// Team order = trial position (Team A -> trial 1, ... for every sequence). Condition order rotates
/// across the four StudySequences, so each team meets each condition exactly once across sequences.
/// Member order within a team = fixed casting (element 0 -> agent_1, etc.).
/// </summary>
public static class AvatarTeamTools
{
    private const string AvatarRoot = "Assets/Testbed/Prefabs/Avatars/";

    // Casting order (element i -> agent_{i+1}) alternates by team: A/C are M,F,M,F and B/D are
    // F,M,F,M, so each agent slot is voiced by 2 male + 2 female avatars across a session -
    // gender is not confounded with script role or spawn position.
    private static readonly (string teamName, string[] members)[] Partition =
    {
        ("Team A", new[] { "Male/Alan",     "Female/Layla",  "Male/Tony",       "Female/Kristin" }),
        ("Team B", new[] { "Female/Alba",   "Male/Norman",   "Female/Kathleen", "Male/Arctic"    }),
        ("Team C", new[] { "Male/John",     "Female/Cori",   "Male/Bryce",      "Female/Amy"     }),
        ("Team D", new[] { "Female/Jenny",  "Male/Joe",      "Female/Lessac",   "Male/Ryan"      }),
    };

    [MenuItem("Tools/iXR/Assign Proposed Avatar Teams")]
    public static void AssignProposedTeams()
    {
        var master = Object.FindObjectOfType<MasterLevelController>();
        if (master == null)
        {
            EditorUtility.DisplayDialog("Assign Avatar Teams",
                "No MasterLevelController found in the open scene.\nOpen 'Master Level Control' and try again.", "OK");
            return;
        }

        var teams = new List<MasterLevelController.AvatarTeam>();
        var missing = new List<string>();

        foreach (var (teamName, members) in Partition)
        {
            var team = new MasterLevelController.AvatarTeam { teamName = teamName };
            foreach (var member in members)
            {
                string path = AvatarRoot + member + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) missing.Add(path);
                team.members.Add(prefab);
            }
            teams.Add(team);
        }

        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("Assign Avatar Teams",
                "Missing prefabs:\n" + string.Join("\n", missing), "OK");
            return;
        }

        Undo.RecordObject(master, "Assign Avatar Teams");
        master.avatarTeams = teams;
        EditorUtility.SetDirty(master);
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);

        Debug.Log("[AvatarTeamTools] Assigned 4 avatar teams:\n" + string.Join("\n",
            teams.Select(t => $"  {t.teamName}: {string.Join(", ", t.members.Select(m => m.name))}")));
    }
}
