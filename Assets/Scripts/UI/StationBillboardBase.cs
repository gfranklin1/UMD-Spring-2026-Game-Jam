using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base class for world-space billboard info panels above ship stations.
/// Creates a world-space canvas at runtime, faces the local camera each frame,
/// and shows only when a player is wearing the diving suit.
/// </summary>
public abstract class StationBillboardBase : MonoBehaviour
{
    [SerializeField] private Font  _font;
    [SerializeField] private float _heightOffset = 2.5f;
    [SerializeField] private int   _fontSize     = 28;
    [SerializeField] private Color _textColor    = Color.white;

    protected Text _text;
    private   GameObject      _canvasGO;
    private   PlayerController _cachedDiver;
    private   float            _refreshTimer;
    private   const float      RefreshInterval = 0.5f;

    protected virtual void Awake() => BuildCanvas();

    private void BuildCanvas()
    {
        _canvasGO = new GameObject("StationInfoCanvas");
        _canvasGO.transform.SetParent(transform, false);
        _canvasGO.transform.localPosition = new Vector3(0f, _heightOffset, 0f);
        _canvasGO.transform.localScale    = Vector3.one * 0.005f;

        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = _canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(240f, 100f);

        // Semi-transparent background panel
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(_canvasGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.sizeDelta        = new Vector2(240f, 100f);
        bgRT.anchoredPosition = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.55f);

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(_canvasGO.transform, false);
        var tRT = textGO.AddComponent<RectTransform>();
        tRT.sizeDelta        = new Vector2(228f, 90f);
        tRT.anchoredPosition = Vector2.zero;

        _text = textGO.AddComponent<Text>();
        _text.font        = _font != null ? _font : FindGameFont();
        _text.fontSize    = _fontSize;
        _text.alignment   = TextAnchor.MiddleCenter;
        _text.color       = _textColor;
        _text.lineSpacing = 1.3f;

        _canvasGO.SetActive(false);
    }

    private void LateUpdate()
    {
        // Periodically locate the active diver
        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = RefreshInterval;
            CacheActiveDiver();
        }

        bool active = _cachedDiver != null && _cachedDiver.IsWearingSuit;
        if (_canvasGO.activeSelf != active)
            _canvasGO.SetActive(active);

        if (!active) return;

        // Billboard: rotate to face the local player camera so it's always readable
        var cam = FindLocalCamera();
        if (cam != null)
            _canvasGO.transform.rotation = Quaternion.LookRotation(
                _canvasGO.transform.position - cam.transform.position);

        _text.text = GetDisplayText(_cachedDiver);
    }

    private void CacheActiveDiver()
    {
        _cachedDiver = null;
        foreach (var pc in FindObjectsByType<PlayerController>())
        {
            if (pc.IsWearingSuit) { _cachedDiver = pc; return; }
        }
    }

    private static Camera FindLocalCamera()
    {
        var main = Camera.main;
        if (main != null) return main;

        foreach (var pc in FindObjectsByType<PlayerController>())
        {
            if (pc == null || !pc.IsOwner) continue;

            var cameraRoot = pc.CameraRoot;
            if (cameraRoot == null) continue;

            var playerCamera = cameraRoot.GetComponent<Camera>();
            if (playerCamera != null && playerCamera.enabled) return playerCamera;
        }

        foreach (var cam in FindObjectsByType<Camera>())
        {
            if (cam != null && cam.enabled) return cam;
        }

        return null;
    }

    /// <summary>
    /// Borrow the font already in use by any Text in the scene so it matches the rest of the UI.
    /// Falls back to the built-in Unity font if nothing is found.
    /// </summary>
    private static Font FindGameFont()
    {
        foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Include))
        {
            if (t.font != null) return t.font;
        }
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    /// <summary>Return the text to display. Called every frame while a diver is suited up.</summary>
    protected abstract string GetDisplayText(PlayerController diver);
}
