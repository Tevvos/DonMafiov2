// LocalAudioListenerFusion.cs
using UnityEngine;
using Fusion;

public class LocalAudioListenerFusion : NetworkBehaviour
{
    public static Transform Listener;  // Transform du joueur local
    [SerializeField] private Transform anchor; // optionnel: un enfant "AudioAnchor"

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            Listener = anchor != null ? anchor : transform;
        }
    }

    private void OnDestroy()
    {
        var t = anchor != null ? anchor : transform;
        if (Listener == t) Listener = null;
    }
}
