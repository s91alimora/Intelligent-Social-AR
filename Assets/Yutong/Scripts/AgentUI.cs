using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class AgentUI : MonoBehaviour
{
    [Header("Placement")]
    public float height = 1.0f;
    public Vector2 panelSize = new Vector2(2f, 2f);

    [Header("Style")]
    public Sprite panelSprite;
    public Color fallbackColor = new Color(0.2f, 0.6f, 1f, 0.85f);

    GameObject _canvasGO;
    RectTransform _canvasRT, _panelRT;
    Image _panelImage;

    public void EnsureBuilt(Color? preferred = null)
    {
        if (_canvasGO == null)
        {
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
            _canvasRT = (RectTransform)_canvasGO.transform;
            _panelRT = (RectTransform)panelGO.transform;

            // start hidden
            _canvasGO.SetActive(false);
        }

        ApplyLayout(preferred);
    }

    void ApplyLayout(Color? preferred)
    {
        // sprite + type
        if (_panelImage != null)
        {
            _panelImage.sprite = panelSprite;
            _panelImage.type = panelSprite ? Image.Type.Sliced : Image.Type.Simple;

            var ca = GetComponent<ConversationalAgent>();
            _panelImage.color = preferred ?? (ca ? ca.speakingColor : fallbackColor);
        }

        // size + position (re-applied every time)
        if (_canvasRT != null) _canvasRT.sizeDelta = panelSize;
        if (_panelRT != null)
        {
            _panelRT.anchorMin = _panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRT.sizeDelta = panelSize;
        }

        _canvasGO.transform.localPosition = Vector3.up * height;
        _canvasGO.transform.localRotation = Quaternion.identity;
        _canvasGO.transform.localScale = Vector3.one;
    }

    public void Show()
    {
        EnsureBuilt(null);          // ensure & re-apply current values
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

#if UNITY_EDITOR
    // When you tweak values in the Inspector (Edit or Play), push them to the UI.
    void OnValidate()
    {
        if (_canvasGO != null) ApplyLayout(null);
    }
#endif
}
