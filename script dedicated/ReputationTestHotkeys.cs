using UnityEngine;

public class ReputationTestHotkeys : MonoBehaviour {
    void Update() {
        if (Input.GetKeyDown(KeyCode.F6)) ReputationManager.Instance?.AddReputation(10);   // +10
        if (Input.GetKeyDown(KeyCode.F7)) ReputationManager.Instance?.AddReputation(-10);  // -10 (min 0)
        if (Input.GetKeyDown(KeyCode.F8)) ReputationManager.Instance?.SetReputation(0);    // reset
    }
}
