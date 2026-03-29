using UnityEngine;
using Fusion;

[RequireComponent(typeof(Rigidbody2D))]
public class SmoothNetworkRigidbody2D : NetworkBehaviour
{
    private Rigidbody2D rb;

    [Networked] private Vector2 NetPos { get; set; }
    [Networked] private float NetRotZ  { get; set; }

    [SerializeField, Range(0f,1f)] private float posLerp = 0.2f;
    [SerializeField, Range(0f,1f)] private float rotLerp = 0.2f;

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();

        if (Object.HasStateAuthority)
        {
            rb.bodyType  = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            NetPos  = rb.position;
            NetRotZ = rb.rotation;
        }
        else
        {
            // Kinematic + simulated=true pour les proxies
            // La balle utilise useFullKinematicContacts pour les triggers
            rb.bodyType  = RigidbodyType2D.Kinematic;
            rb.simulated = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            NetPos  = rb.position;
            NetRotZ = rb.rotation;
        }
    }

    public override void Render()
    {
        if (Object.HasStateAuthority) return;

        Vector2 tPos = NetPos;
        float   tRot = NetRotZ;

        transform.position = Vector2.Lerp(transform.position, tPos, posLerp);
        float z = Mathf.LerpAngle(transform.eulerAngles.z, tRot, rotLerp);
        transform.rotation = Quaternion.Euler(0f, 0f, z);
    }
}
