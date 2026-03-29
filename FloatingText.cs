using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    [Header("Refs (assign auto si vide)")]
    [SerializeField] private TMP_Text label3D;            // TextMeshPro (3D)
    [SerializeField] private TextMeshProUGUI labelUGUI;   // TextMeshProUGUI (UI)

    [Header("Anim")]
    [SerializeField] private float life = 0.8f;
    [SerializeField] private float riseSpeed = 1.4f;
    [SerializeField] private float startScale = 0.9f;
    [SerializeField] private float endScale   = 1.15f;

    private float _t;
    private Color _baseColor = Color.white;

    public bool IsUGUI => labelUGUI != null;

    void Awake()
    {
        if (!label3D)   label3D   = GetComponentInChildren<TMP_Text>(true);
        if (!labelUGUI) labelUGUI = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    public void Setup(string text, Color color)
    {
        _baseColor = new Color(color.r, color.g, color.b, 1f);

        if (labelUGUI != null)
        {
            labelUGUI.text  = text;
            labelUGUI.color = _baseColor;
        }
        if (label3D != null)
        {
            label3D.text  = text;
            label3D.color = _baseColor;
        }

        transform.localScale = Vector3.one * startScale;
        _t = 0f;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, life));

        // montée
        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        // scale
        float s = Mathf.Lerp(startScale, endScale, k);
        transform.localScale = new Vector3(s, s, 1f);

        // fade
        var c = _baseColor;
        c.a = 1f - k;
        if (labelUGUI) labelUGUI.color = c;
        if (label3D)   label3D.color   = c;

        if (_t >= life) Destroy(gameObject);
    }
}
