using UnityEngine;
using UnityEngine.InputSystem;

// Reads only the keyboard (arrows + WASD + Space + R) and exposes simple values.
// It knows nothing about physics. For multiplayer later, replace this script
// with one that fills the same values from the network - CarController stays the same.
public class CarInput : MonoBehaviour
{
    // -1..1 : forward = 1, brake/reverse = -1
    public float Throttle { get; private set; }
    // -1..1 : left = -1, right = 1
    public float Steer { get; private set; }
    // true while Space is held
    public bool Handbrake { get; private set; }

    bool resetPressed;

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            Throttle = 0f;
            Steer = 0f;
            Handbrake = false;
            return;
        }

        float t = 0f;
        if (kb.upArrowKey.isPressed || kb.wKey.isPressed) t += 1f;
        if (kb.downArrowKey.isPressed || kb.sKey.isPressed) t -= 1f;

        float s = 0f;
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) s += 1f;
        if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) s -= 1f;

        Throttle = t;
        Steer = s;
        Handbrake = kb.spaceKey.isPressed;
        if (kb.rKey.wasPressedThisFrame) resetPressed = true;
    }

    // Physics calls this once per press of R (put the car back on its wheels).
    public bool ConsumeReset()
    {
        bool r = resetPressed;
        resetPressed = false;
        return r;
    }
}
