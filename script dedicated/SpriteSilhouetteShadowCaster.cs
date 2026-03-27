// Assets/ShadowCasterSRMaker.cs
using UnityEngine;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// Crée/répare un enfant "_ShadowCasterSR" avec:
/// - SpriteRenderer (alpha 0, ENABLED)
/// - ShadowCaster2D (useRendererSilhouette = true)
/// Utilise overrideSprite s'il est fourni, sinon le sprite du parent.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class ShadowCasterSRMaker : MonoBehaviour
{
    [Tooltip("Met ici un sprite PLEIN (mask). Si vide, prend le sprite du parent.")]
    public Sprite overrideSprite;

    [Tooltip("Nom de l'enfant contenant le SR + ShadowCaster2D.")]
    public string childName = "_ShadowCasterSR";

    [Tooltip("Recréer de zéro à chaque Apply pour éviter tout état cassé.")]
    public bool alwaysRecreateOnApply = false;

    [ContextMenu("Apply / Create ShadowCaster Child")]
    public void Apply()
    {
        var parentSR = GetComponent<SpriteRenderer>();
        if (!parentSR)
        {
            Debug.LogError($"[{name}] Pas de SpriteRenderer parent.");
            return;
        }

        // 1) détruire l'ancien si on force la recréation
        Transform childT = transform.Find(childName);
        if (alwaysRecreateOnApply && childT)
        {
#if UNITY_EDITOR
            DestroyImmediate(childT.gameObject);
#else
            Destroy(childT.gameObject);
#endif
            childT = null;
        }

        // 2) créer si absent
        if (!childT)
        {
            var go = new GameObject(childName);
            childT = go.transform;
            childT.SetParent(transform, false);
            childT.localPosition = Vector3.zero;
            childT.localRotation = Quaternion.identity;
            childT.localScale    = Vector3.one;
        }

        // 3) s'assurer des composants
        var childGO = childT.gameObject;

        var childSR = childGO.GetComponent<SpriteRenderer>();
        if (!childSR) childSR = childGO.AddComponent<SpriteRenderer>();

        var caster = childGO.GetComponent<ShadowCaster2D>();
        if (!caster) caster = childGO.AddComponent<ShadowCaster2D>();

        // 4) config SpriteRenderer enfant
        childSR.enabled = true;                // IMPORTANT: doit être actif
        childSR.sprite  = overrideSprite ? overrideSprite : parentSR.sprite;
        childSR.sharedMaterial = parentSR.sharedMaterial; // Sprite-Lit-Default
        childSR.sortingLayerID = parentSR.sortingLayerID; // même layer
        childSR.sortingOrder   = -9999;                   // derrière tout
        var col = childSR.color; col.a = 0f; childSR.color = col; // invisible

        // 5) config ShadowCaster2D
        caster.useRendererSilhouette = true;  // clé: silhouette du SR enfant
        caster.selfShadows           = false;
        caster.castsShadows          = true;

        // 6) refresh “hard”
        childGO.SetActive(false);
        childGO.SetActive(true);
        caster.enabled = false; caster.enabled = true;

#if UNITY_EDITOR
        EditorUtility.SetDirty(childSR);
        EditorUtility.SetDirty(caster);
        EditorUtility.SetDirty(gameObject);
#endif

        // Petit log utile
        if (childSR.sprite == null)
            Debug.LogWarning($"[{name}] Enfant créé mais aucun sprite assigné (override vide et parent sprite null).");
        else
            Debug.Log($"[{name}] OK: enfant '{childName}' prêt (sprite silhouette: {childSR.sprite.name}).");
    }

#if UNITY_EDITOR
    // bouton visible directement dans l'Inspector
    [CustomEditor(typeof(ShadowCasterSRMaker))]
    public class ShadowCasterSRMakerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var maker = (ShadowCasterSRMaker)target;

            GUILayout.Space(6);
            if (GUILayout.Button("Apply / Create ShadowCaster Child", GUILayout.Height(32)))
            {
                maker.Apply();
            }
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Astuce: mets ici un sprite PLEIN en Override Sprite (mask). " +
                "La lumière du joueur doit avoir Shadows Enabled + Target Sorting Layers qui incluent le SOL/PERSONNAGES.",
                MessageType.Info);
        }
    }
#endif
}
