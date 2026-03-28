using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class ParallaxRawImage : MonoBehaviour
{
    public Vector2 speed = new Vector2(-0.1f, 0f); // UV par seconde
    [Range(0f,1f)] public float parallaxFactor = 1f;

    RawImage img;
    Rect uv;

    void Awake()
    {
        img = GetComponent<RawImage>();
        uv = img.uvRect;
        // Important pour la perf UI : déco hors interactions
        img.raycastTarget = false;
    }

    void Update()
    {
        uv.x += speed.x * parallaxFactor * Time.deltaTime;
        uv.y += speed.y * parallaxFactor * Time.deltaTime;
        img.uvRect = uv;
    }
}
