using UnityEngine;
using UnityEngine.UI;

public class StarrySkyScrollerUI : MonoBehaviour
{
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.01f, 0f);
    private RawImage rawImage;
    private Vector2 uvOffset;

    private void Awake()
    {
        rawImage = GetComponent<RawImage>();
    }

    private void Update()
    {
        uvOffset += scrollSpeed * Time.deltaTime;
        rawImage.uvRect = new Rect(uvOffset, rawImage.uvRect.size);
    }
}
