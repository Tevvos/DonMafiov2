using UnityEngine;

public class ParallaxMove : MonoBehaviour
{
    [SerializeField] private float scrollSpeed = 1f; // Vitesse du mouvement
    private Vector3 startPosition;
    private float spriteWidth;

    private GameObject background2; // Le second fond

    void Start()
    {
        // On récupère la taille du fond
        spriteWidth = GetComponent<SpriteRenderer>().bounds.size.x;

        // Créer et initialiser le deuxième fond
        background2 = new GameObject("Background2");
        background2.transform.position = new Vector3(transform.position.x + spriteWidth, transform.position.y, transform.position.z);
        background2.AddComponent<SpriteRenderer>().sprite = GetComponent<SpriteRenderer>().sprite; // Assurez-vous que le deuxième fond a la même image que le premier
    }

    void Update()
    {
        // Déplacer les deux arrière-plans
        transform.position = new Vector3(transform.position.x - scrollSpeed * Time.deltaTime, transform.position.y, transform.position.z);
        background2.transform.position = new Vector3(background2.transform.position.x - scrollSpeed * Time.deltaTime, background2.transform.position.y, background2.transform.position.z);

        // Lorsque le premier fond est complètement à gauche, le repositionner à droite
        if (transform.position.x <= -spriteWidth)
        {
            transform.position = new Vector3(background2.transform.position.x + spriteWidth, transform.position.y, transform.position.z);
        }

        // Lorsque le second fond est complètement à gauche, le repositionner à droite
        if (background2.transform.position.x <= -spriteWidth)
        {
            background2.transform.position = new Vector3(transform.position.x + spriteWidth, background2.transform.position.y, background2.transform.position.z);
        }
    }
}
