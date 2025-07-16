using Fusion;
using UnityEngine;

/// <summary>
/// Network input data structure for Fusion 2
/// This replaces direct Unity Input System calls in networked gameplay
/// Enhanced with Advanced AAA-style mouse and movement controls
/// </summary>
public struct U3DNetworkInputData : INetworkInput
{
    public Vector2 MovementInput;
    public Vector2 LookInput;
    public NetworkButtons Buttons;
    public float PerspectiveScroll;

    // Advanced AAA-style mouse controls
    public bool LeftMouseHeld;
    public bool RightMouseHeld;
    public bool BothMouseHeld;

    // Enhanced movement controls
    public bool StrafeLeft;
    public bool StrafeRight;
    public bool TurnLeft;
    public bool TurnRight;
}

/// <summary>
/// Button definitions for network input
/// Enhanced with Advanced AAA-style controls
/// </summary>
public enum U3DInputButtons
{
    Jump = 0,
    Sprint = 1,
    Crouch = 2,
    Fly = 3,
    AutoRun = 4,
    Interact = 5,
    Teleport = 6,
    Zoom = 7,
    PerspectiveSwitch = 8,
    // Advanced AAA-style additions
    MouseLeft = 9,
    MouseRight = 10,
    StrafeLeft = 11,
    StrafeRight = 12,
    TurnLeft = 13,
    TurnRight = 14,
    AutoRunToggle = 15  // NumLock toggle
}