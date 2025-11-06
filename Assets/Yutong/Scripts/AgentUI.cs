using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class AgentUI : MonoBehaviour
{
    [Header("Placement")]
    public float height = 0.6f;
    public Vector2 panelSize = new Vector2(0.35f, 0.16f);

    [Header("Style")]
    public Sprite panelSprite;                    // optional 9-slice
    public Color fallbackColor = new Color(0.2f, 0.6f, 1f, 0.85f);

    GameObject _canvasGO;
    Image _panelImage;

    public void EnsureBuilt(Color? preferred = null)
    {
        if (_canvasGO) return;

        _canvasGO = new GameObject("AgentUI_Canvas",
                                   typeof(RectTransform), typeof(Canvas),
                                   typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvasGO.transform.SetParent(transform, false);

        var canvas = _canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        var scaler = _canvasGO.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 200f;

        var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(_canvasGO.transform, false);

        _panelImage = panelGO.GetComponent<Image>();
        _panelImage.sprite = panelSprite;
        _panelImage.type = panelSprite ? Image.Type.Sliced : Image.Type.Simple;

        var ca = GetComponent<ConversationalAgent>();
        _panelImage.color = preferred ?? (ca ? ca.speakingColor : fallbackColor);

        var canvasRT = (RectTransform)_canvasGO.transform;
        canvasRT.sizeDelta = panelSize;
        var panelRT = (RectTransform)panelGO.transform;
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = panelSize;

        _canvasGO.transform.localPosition = Vector3.up * height;
        _canvasGO.transform.localRotation = Quaternion.identity;
        _canvasGO.transform.localScale = Vector3.one;

        _canvasGO.SetActive(false);
    }

    public void Show()
    {
        if (!_canvasGO) EnsureBuilt(null);
        _canvasGO.SetActive(true);
        FaceCamera();
    }

    public void Hide()
    {
        if (_canvasGO) _canvasGO.SetActive(false);
    }

    void LateUpdate()
    {
        if (_canvasGO && _canvasGO.activeSelf) FaceCamera();
    }

    void FaceCamera()
    {
        var cam = Camera.main;
        if (!cam) return;
        _canvasGO.transform.rotation =
            Quaternion.LookRotation(_canvasGO.transform.position - cam.transform.position, Vector3.up);
    }
}
