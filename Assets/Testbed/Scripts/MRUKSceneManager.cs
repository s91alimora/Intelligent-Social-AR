using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Collections;
using System.Collections.Generic;

public class MRUKSceneManager : MonoBehaviour
{
    [Header("Hierarchy Targets")]
    public GameObject environmentParent; // Optional: parent of plane and wall
    public GameObject floorPlane;        // The 'Plane' inside Environment
    public GameObject wallPlane;         // The 'wall' inside Environment

    [Header("Scripts")]
    public SessionController sessionController;

    [Header("Settings")]
    public float wallOffsetFromSurface = 0.05f;
    public Vector3 wallRotationOffset = new Vector3(90, 180, 0); 
    public int wallIndex = 0; 
    public bool useFurthestWall = true;
    public float minWallArea = 2.0f; // Minimum area to count as a 'real' wall

    private List<MRUKAnchor> _cachedWallAnchors = new List<MRUKAnchor>();

    [ContextMenu("Manual Align")]
    public void ManualAlign()
    {
        if (_cachedWallAnchors.Count > 0)
        {
            ApplyWallAlignment();
        }
        else if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            OnSceneLoaded();
        }
    }

    void Start()
    {
        // SessionController should be disabled in the Inspector at start
        // to wait for this script to map the room first.
        if (MRUK.Instance != null)
        {
            MRUK.Instance.SceneLoadedEvent.AddListener(OnSceneLoaded);
        }
    }

    private void OnSceneLoaded()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        // 1. Align Floor
        MRUKAnchor floorAnchor = room.FloorAnchor;
        if (floorAnchor != null && floorPlane != null)
        {
            floorPlane.transform.position = floorAnchor.transform.position;
            floorPlane.transform.rotation = floorAnchor.transform.rotation;
            
            if (sessionController != null && sessionController.gridGenerator != null)
            {
                sessionController.gridGenerator.origin = floorPlane.transform.position;
            }
        }

        // 2. Discover and Cache Walls
        _cachedWallAnchors.Clear();
        Debug.Log($"[MRUK] --- Scanning ALL {room.Anchors.Count} Anchors in Room ---");

        for (int i = 0; i < room.Anchors.Count; i++)
        {
            var anchor = room.Anchors[i];
            string labels = string.Join(", ", anchor.AnchorLabels);
            
            // Calculate approximate area
            float area = 0f;
            if (anchor.VolumeBounds.HasValue) 
                area = anchor.VolumeBounds.Value.size.x * anchor.VolumeBounds.Value.size.y;
            else if (anchor.PlaneRect.HasValue) 
                area = anchor.PlaneRect.Value.width * anchor.PlaneRect.Value.height;

            Debug.Log($"Index {i}: Name={anchor.name}, Labels=[{labels}], Area={area:F2}m2, Pos={anchor.transform.position}");

            // Heuristic for 'Real' Wall: Labeled WALL or is a large vertical surface
            if (anchor.HasLabel("WALL") || anchor.HasLabel("OTHER") || area > minWallArea)
            {
                if (anchor != room.FloorAnchor && anchor != room.CeilingAnchor)
                {
                    _cachedWallAnchors.Add(anchor);
                }
            }
        }

        // Sort by distance to keep index stable
        _cachedWallAnchors.Sort((a, b) => {
            float dA = Vector3.Distance(a.transform.position, room.FloorAnchor.transform.position);
            float dB = Vector3.Distance(b.transform.position, room.FloorAnchor.transform.position);
            return dA.CompareTo(dB);
        });

        if (useFurthestWall && _cachedWallAnchors.Count > 0) 
            wallIndex = _cachedWallAnchors.Count - 1;

        ApplyWallAlignment();

        // 3. Start experiment
        if (sessionController != null && !sessionController.gameObject.activeSelf)
        {
            sessionController.gameObject.SetActive(true);
        }
    }

    public void ApplyWallAlignment()
    {
        if (_cachedWallAnchors == null || _cachedWallAnchors.Count == 0) return;
        
        int idx = Mathf.Clamp(wallIndex, 0, _cachedWallAnchors.Count - 1);
        MRUKAnchor targetWall = _cachedWallAnchors[idx];

        if (wallPlane != null)
        {
            wallPlane.transform.position = targetWall.transform.position + (targetWall.transform.forward * wallOffsetFromSurface);
            wallPlane.transform.rotation = targetWall.transform.rotation * Quaternion.Euler(wallRotationOffset);
            Debug.Log($"[MRUK] Switched to Wall Index {idx}: {targetWall.name}");
        }
    }
}
