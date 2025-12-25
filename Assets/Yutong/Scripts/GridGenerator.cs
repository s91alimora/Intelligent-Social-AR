using System.Collections.Generic;
using UnityEngine;
//using TMPro;


[DisallowMultipleComponent]
public class GridGenerator : MonoBehaviour
{
    [Header("Layout")]
    public float cellSize = 1.2f;
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


    public enum LabelAlignment { Bottom, Right, Left }
    public LabelAlignment labelAlignment = LabelAlignment.Bottom;

    int _rows, _cols;
    readonly List<LineRenderer> _lines = new();
    readonly List<TextMesh> _labels = new();

    public void Build(int rows, int cols)
    {
        _rows = Mathf.Max(1, rows);
        _cols = Mathf.Max(1, cols);

        ClearLines();
        ClearLabels();

        // Draw verticals (cols+1)
        for (int c = 0; c <= _cols; c++)
        {
            Vector3 a = origin + new Vector3((c - 0.5f) * cellSize, 0f, (-0.5f) * cellSize);
            Vector3 b = origin + new Vector3((c - 0.5f) * cellSize, 0f, (_rows - 0.5f) * cellSize);
            AddLine(a, b);
        }
        // Draw horizontals (rows+1)
        for (int r = 0; r <= _rows; r++)
        {
            Vector3 a = origin + new Vector3((-0.5f) * cellSize, 0f, (r - 0.5f) * cellSize);
            Vector3 b = origin + new Vector3((_cols - 0.5f) * cellSize, 0f, (r - 0.5f) * cellSize);
            AddLine(a, b);
        }
    }

    public void BuildLabels(List<string> labels)
    {
        ClearLabels();
        if (!showColumnLabels) return;
        if (labels == null || labels.Count == 0) return;

        int count = labels.Count;
        for (int i = 0; i < count; i++)
        {
            string txt = labels[i] ?? "";
            Vector3 pos;
            
            if (labelAlignment == LabelAlignment.Right)
            {
                // Align to right side (X+), iterating rows
                if (i >= _rows) break;
                pos = GridToWorld(i, _cols - 1) + new Vector3(cellSize * 1.0f, labelY, 0f);
            }
            else if (labelAlignment == LabelAlignment.Left)
            {
                // Align to left side (X-), iterating rows
                if (i >= _rows) break;
                pos = GridToWorld(i, 0) + new Vector3(-cellSize * 1.0f, labelY, 0f);
            }
            else
            {
                // Original bottom alignment, iterating columns
                if (i >= _cols) break;
                pos = GridToWorld(-1, i) + new Vector3(0f, labelY, 0f); 
            }

            var go = new GameObject($"grid-label-{i}");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(labelRotationEuler);
            go.transform.localScale = Vector3.one * labelScale;

            var tm = go.AddComponent<TextMesh>();  // Legacy TextMesh
            tm.text = txt;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            if (labelFontLegacy != null)
            {
                tm.font = labelFontLegacy;
                var mr = go.GetComponent<MeshRenderer>();
                if (labelFontLegacy.material != null) mr.sharedMaterial = labelFontLegacy.material;
            }

            tm.fontSize = Mathf.Max(1, labelFontSize);
            tm.characterSize = Mathf.Max(0.001f, labelCharacterSize);
            tm.color = labelColor;

            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                rend.material = new Material(rend.sharedMaterial);
                rend.material.color = labelColor;
            }

            _labels.Add(tm);
        }
    }

    public Vector3 GridToWorld(int row, int col)
    {
        // Allow -1 row (used for staging/labels)
        float r = row + 0.0f;
        float c = col + 0.0f;
        return origin + new Vector3(c * cellSize, 0f, r * cellSize);
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
        foreach (var t in _labels) if (t) Destroy(t.gameObject);
        _labels.Clear();
    }

}
