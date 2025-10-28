// GridGenerator.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GridGenerator : MonoBehaviour
{
    [Header("Layout")]
    public float cellSize = 1.2f;
    public Vector3 origin = Vector3.zero;     // world position of cell (0,0) center

    [Header("Rendering")]
    public Color lineColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    public float lineWidth = 0.02f;

    int _rows, _cols;
    readonly List<LineRenderer> _lines = new();

    public void Build(int rows, int cols)
    {
        _rows = Mathf.Max(1, rows);
        _cols = Mathf.Max(1, cols);

        ClearLines();

        // Outer bounds (top-left corner)
        var half = new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
        var topLeft = origin - new Vector3(0, 0, 0) - half;

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

    public Vector3 GridToWorld(int row, int col)
    {
        // Allow -1 row (used for staging default)
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
}
