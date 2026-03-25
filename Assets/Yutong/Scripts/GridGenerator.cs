using System.Collections.Generic;
using UnityEngine;
using TMPro;


[DisallowMultipleComponent]
public class GridGenerator : MonoBehaviour
{
    [Header("Layout")]
    public float cellWidth = 1.2f;
    public float cellDepth = 1.2f;
    public Vector3 origin = Vector3.zero;  // world position of cell (0,0) center

    [Header("Rendering")]
    public Color lineColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    public float lineWidth = 0.02f;

    [Header("Column Labels (TextMesh)")]
    public bool showColumnLabels = true;
    public float labelY = 0.02f;                       // slight lift above grid
    public Vector3 labelRotationEuler = new Vector3(90f, 0f, 0f); // face up
    public float labelScale = 0.25f;                   // transform scale

    public Font labelFontLegacy;                       // assign any readable font
    public int labelFontSize = 64;                     // mesh resolution
    public float labelCharacterSize = 0.12f;           // world size per glyph
    public Color labelColor = Color.black;
    
    [Header("Prefab Labels")]
    public GameObject labelPrefab;
    public Camera mainCamera;



    public enum LabelAlignment { Bottom, Right, Left }
    public LabelAlignment labelAlignment = LabelAlignment.Bottom;

    int _rows, _cols;
    readonly List<LineRenderer> _lines = new();
    readonly List<GameObject> _labels = new();


    public void Build(int rows, int cols)
    {
        _rows = Mathf.Max(1, rows);
        _cols = Mathf.Max(1, cols);

        ClearLines();
        ClearLabels();

        // Draw verticals (cols+1)
        for (int c = 0; c <= _cols; c++)
        {
            Vector3 a = origin + new Vector3((c - 0.5f) * cellWidth, 0f, (-0.5f) * cellDepth);
            Vector3 b = origin + new Vector3((c - 0.5f) * cellWidth, 0f, (_rows - 0.5f) * cellDepth);
            AddLine(a, b);
        }
        // Draw horizontals (rows+1)
        for (int r = 0; r <= _rows; r++)
        {
            Vector3 a = origin + new Vector3((-0.5f) * cellWidth, 0f, (r - 0.5f) * cellDepth);
            Vector3 b = origin + new Vector3((_cols - 0.5f) * cellWidth, 0f, (r - 0.5f) * cellDepth);
            AddLine(a, b);
        }
    }

    public void BuildLabels(List<string> labels)
    {
        ClearLabels();
        if (!showColumnLabels) return;
        if (labels == null || labels.Count == 0) return;
        if (labelPrefab == null)
        {
            Debug.LogWarning("GridGenerator: Label Prefab is not assigned!");
            return;
        }

        int count = labels.Count;
        for (int i = 0; i < count; i++)
        {
            string txt = labels[i] ?? "";
            Vector3 pos;
            
            if (labelAlignment == LabelAlignment.Right)
            {
                if (i >= _rows) break;
                pos = GridToWorld(i, _cols - 1) + new Vector3(cellWidth * 0.5f, labelY, 0f);
            }
            else if (labelAlignment == LabelAlignment.Left)
            {
                if (i >= _rows) break;
                pos = GridToWorld(i, 0) + new Vector3(-cellWidth * 0.5f, labelY, 0f);
            }
            else
            {
                if (i >= _cols) break;
                pos = GridToWorld(-0.5f, i) + new Vector3(0f, labelY, 0f); 
            }

            // Instantiate from prefab
            GameObject go = Instantiate(labelPrefab, pos, Quaternion.Euler(labelRotationEuler), transform);
            go.name = $"grid-label-{i}";
            go.transform.localScale = Vector3.one * labelScale;

            // Setup Canvas Camera
            Canvas canvas = go.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = mainCamera;
            }

            // Setup TMP Text
            TMP_Text tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = txt;
            }
            else
            {
                // Fallback to legacy if TMP_Text not found (though unlikely given requirements)
                TextMesh tm = go.GetComponentInChildren<TextMesh>();
                if (tm != null) tm.text = txt;
            }

            _labels.Add(go);
        }
    }

    public Vector3 GridToWorld(float row, float col)
    {
        // Allow fractional rows/cols (used for staging/labels)
        return origin + new Vector3(col * cellWidth, 0f, row * cellDepth);
    }

    void AddLine(Vector3 a, Vector3 b)
    {
        var go = new GameObject("grid-line");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPositions(new[] { a, b });
        lr.startWidth = lr.endWidth = lineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = lineColor;
        _lines.Add(lr);
    }

    void ClearLines()
    {
        foreach (var lr in _lines) if (lr) Destroy(lr.gameObject);
        _lines.Clear();
    }

    void ClearLabels()
    {
        foreach (var go in _labels) if (go) Destroy(go);
        _labels.Clear();
    }

}
