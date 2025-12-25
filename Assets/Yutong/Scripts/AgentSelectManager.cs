using UnityEngine;
using Oculus.Interaction;

[DisallowMultipleComponent]
public class AgentSelectManager : MonoBehaviour
{
    [Header("Ray Input (Oculus Interaction)")]
    [SerializeField] private RayInteractor rayInteractor;
    [SerializeField] private float maxRayDistance = 10f;
    [SerializeField] private LayerMask agentLayers = ~0;   // optional filter

    [Header("UI Canvas Defaults")]
    public float uiHeight = 0.6f;
    public Vector2 uiPanelSize = new Vector2(0.35f, 0.16f);
    public Sprite uiPanelSprite;

    private AgentUI _currentUI;

    void Start()
    {
        if (!rayInteractor)
            Debug.LogError("AgentSelectManager: RayInteractor not assigned.");
    }

    void Update()
    {
        if (!rayInteractor) return;

        // NEW: Only allow UI interaction if we are in the AR_UI phase
        if (SessionController.Instance == null || 
            SessionController.Instance.CurrentPhase != SessionController.SessionPhase.AR_UI)
        {
            HideCurrent();
            return;
        }

        Ray ray = rayInteractor.Ray;
        if (Physics.Raycast(ray, out var hit, maxRayDistance, agentLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.CompareTag("Agent"))
        {
            var root = hit.collider.GetComponentInParent<Transform>();
            if (!root) { HideCurrent(); return; }

            var ui = root.GetComponent<AgentUI>();
            if (!ui)
            {
                ui = root.gameObject.AddComponent<AgentUI>();
                ui.height = uiHeight;
                ui.panelSize = uiPanelSize;
                ui.panelSprite = uiPanelSprite;
                ui.EnsureBuilt(null); // picks speakingColor if available
            }

            if (ui != _currentUI)
            {
                HideCurrent();
                ui.Show();
                _currentUI = ui;
            }
        }
        else
        {
            // no valid hit -> hide whatever was showing
            HideCurrent();
        }
    }

    void HideCurrent()
    {
        if (_currentUI != null)
        {
            _currentUI.Hide();
            _currentUI = null;
        }
    }
}
