using UnityEngine;

public class CameraIdleMovement : MonoBehaviour
{
    [Header("⚙️ Mouvement")]
    public float amplitude = 0.1f; // Hauteur du mouvement
    public float frequency = 0.5f; // Vitesse du mouvement

    private Vector3 startPos;

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {
        float offsetY = Mathf.Sin(Time.time * frequency) * amplitude;
        float offsetX = Mathf.Cos(Time.time * frequency * 0.6f) * amplitude * 0.5f;

        transform.position = startPos + new Vector3(offsetX, offsetY, 0f);
    }
}
