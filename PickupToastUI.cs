using UnityEngine;

public class PickupToastUI : MonoBehaviour
{
    [Header("Prefab du texte flottant")]
    [SerializeField] private FloatingText floatingTextPrefab;

    [Header("Ancrage world (optionnel)")]
    [SerializeField] private Transform worldAnchor;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.1f, 0f);

    [Header("Couleurs par type")]
    [SerializeField] private Color pistolColor   = new Color(0.95f, 0.95f, 0.95f);
    [SerializeField] private Color thompsonColor = new Color(0.95f, 0.80f, 0.20f);
    [SerializeField] private Color shotgunColor  = new Color(0.95f, 0.40f, 0.20f);

    // Canvas world-space unique pour tous les toasts UGUI
    private static Canvas _worldCanvas;
    private static Camera _mainCam;

    private void Awake()
    {
        if (!worldAnchor) worldAnchor = transform;
        if (!_mainCam) _mainCam = Camera.main;
    }

    public void ShowAmmo(AmmoKind kind, int amount)
    {
        if (!floatingTextPrefab)
        {
            Debug.LogWarning("[PickupToastUI] floatingTextPrefab non assigné → aucun toast.");
            return;
        }

        var pos = (worldAnchor ? worldAnchor.position : transform.position) + worldOffset;

        // On instancie d'abord "libre", on regarde s'il est UGUI ou 3D
        var ft = Instantiate(floatingTextPrefab, pos, Quaternion.identity);
        bool isUGUI = ft.IsUGUI;

        if (isUGUI)
        {
            // S'assurer d'un Canvas World-Space unique
            EnsureWorldCanvas();

            // Parent sur le canvas world-space
            ft.transform.SetParent(_worldCanvas.transform, worldPositionStays: true);

            // Option : taille lisible en 2D (si besoin tu peux ajuster)
            ft.transform.localScale = Vector3.one * 0.015f; // petit scale pour UGUI en world
        }

        // Texte + couleur
        ft.Setup($"+{amount} ammo", GetColor(kind));

        // S'il n'est pas UGUI, laisser en world directement (TMP 3D)
        // Rien d'autre à faire.
    }

    private Color GetColor(AmmoKind kind)
    {
        switch (kind)
        {
            case AmmoKind.Pistol:   return pistolColor;
            case AmmoKind.Thompson: return thompsonColor;
            case AmmoKind.Shotgun:  return shotgunColor;
        }
        return Color.white;
    }

    private static void EnsureWorldCanvas()
    {
        if (_worldCanvas && _worldCanvas.renderMode == RenderMode.WorldSpace) return;

        var go = new GameObject("_ToastCanvas (WorldSpace)");
        _worldCanvas = go.AddComponent<Canvas>();
        _worldCanvas.renderMode = RenderMode.WorldSpace;
        _worldCanvas.worldCamera = _mainCam;

        var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var raycaster = go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Taille/position du Canvas world-space
        var rect = _worldCanvas.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(10f, 10f); // arbitraire, suffit pour toasts
        rect.position = Vector3.zero;
        rect.localScale = Vector3.one * 0.01f; // rend les UIs à l'échelle du monde

        // Option: couche de tri dédiée si tu utilises URP/Sorting
        _worldCanvas.sortingOrder = 32767;
    }
}
