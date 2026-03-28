using UnityEngine;
using Fusion;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
public class NetFxOneShot : NetworkBehaviour
{
    [SerializeField] private float lifeTime = 2f;
    [SerializeField] private string stateLeft = "Blood_Left";
    [SerializeField] private string stateRight = "Blood_Right";

    public override void Spawned()
    {
        var anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.Rebind();
            anim.Update(0f);

            // Choix gauche/droite en fonction de la direction visuelle actuelle
            bool goRight = transform.right.x >= 0f;
            string state = goRight ? stateRight : stateLeft;

            if (!string.IsNullOrEmpty(state) && anim.HasState(0, Animator.StringToHash(state)))
            {
                anim.Play(state, 0, 0f);
                anim.Update(0f);
            }
            else
            {
                anim.Play(0, 0, 0f);
                anim.Update(0f);
            }
        }

        StartCoroutine(DespawnAfterDelay(lifeTime));
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Seule la StateAuthority a le droit de despawn le NetworkObject
        if (Object && Object.HasStateAuthority && Runner)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }
}
