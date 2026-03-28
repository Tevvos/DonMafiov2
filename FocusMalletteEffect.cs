using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

public class FocusMalletteEffect : MonoBehaviour
{
    [Header("Global Volume (URP)")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("Players Highlight")]
    [SerializeField] private Color otherPlayersColor = Color.red;

    [Tooltip("Nom exact du GameObject qui porte le SpriteRenderer du joueur (dans ton prefab : VisualRoot).")]
    [SerializeField] private string visualRootName = "VisualRoot";

    private Coroutine _fadeCo;

    // Cache des couleurs d'origine (par SpriteRenderer)
    private readonly Dictionary<SpriteRenderer, Color> _originalColors = new();

    private void Awake()
    {
        if (globalVolume != null)
            globalVolume.weight = 0f; // OFF par défaut
    }

    public void EnableFocus(bool enable)
    {
        // 1) Map grayscale via volume weight
        float targetWeight = enable ? 1f : 0f;
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(FadeVolume(targetWeight));

        // 2) Autres joueurs en rouge
        SetOtherPlayersHighlight(enable);
    }

    private IEnumerator FadeVolume(float target)
    {
        if (globalVolume == null)
            yield break;

        float start = globalVolume.weight;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            globalVolume.weight = Mathf.Lerp(start, target, k);
            yield return null;
        }

        globalVolume.weight = target;
    }

    private void SetOtherPlayersHighlight(bool enable)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null) return;

        // Tous les NetworkObject présents (players inclus)
        var netObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);

        foreach (var no in netObjects)
        {
            if (no == null) continue;

            // Filtre joueurs: en général un player a InputAuthority != None
            if (no.InputAuthority == PlayerRef.None)
                continue;

            // Ne pas toucher au joueur local
            if (no.InputAuthority == runner.LocalPlayer)
                continue;

            // On vise spécifiquement le SpriteRenderer du "VisualRoot"
            var sr = GetVisualRootSpriteRenderer(no.transform);
            if (sr == null) continue;

            if (enable)
            {
                if (!_originalColors.ContainsKey(sr))
                    _originalColors[sr] = sr.color;

                sr.color = otherPlayersColor;
            }
            else
            {
                if (_originalColors.TryGetValue(sr, out var original))
                    sr.color = original;
                else
                    sr.color = Color.white;
            }
        }
    }

    private SpriteRenderer GetVisualRootSpriteRenderer(Transform playerRoot)
    {
        if (playerRoot == null) return null;

        // 1) Find direct child by name (fast + exact)
        var visual = playerRoot.Find(visualRootName);
        if (visual != null)
        {
            var sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null) return sr;
        }

        // 2) Fallback: chercher un enfant qui porte ce nom
        var children = playerRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in children)
        {
            if (t != null && t.name == visualRootName)
            {
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null) return sr;
            }
        }

        return null;
    }
}
