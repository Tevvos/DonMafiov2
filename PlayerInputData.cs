using Fusion;
using UnityEngine;

/// <summary>
/// Struct d'input simple. Garde MoveX/MoveY et un angle d'aim (degrés).
/// Tu peux l'étendre si besoin (boutons tir/reload).
/// </summary>
public struct PlayerInputData : INetworkInput
{
    public float MoveX;
    public float MoveY;
    public float AimAngle; // en degrés

    // Exemple d'extension si tu utilises des boutons:
    // public NetworkButtons Buttons;
    // public const int BTN_FIRE = 0;
    // public const int BTN_RELOAD = 1;
}
