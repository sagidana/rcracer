using UnityEngine;
using UnityEngine.InputSystem;

// Reads the keyboard (arrows + WASD + Space + R) and a gamepad (PS5 DualSense, Xbox, ...) and exposes simple values.
//   gamepad: R2 = throttle, L2 = brake / reverse, left stick or d-pad = steer, Square or L1 = handbrake, Triangle = reset.
// It knows nothing about physics. For multiplayer later, replace this script
// with one that fills the same values from the network - CarController stays the same.
public class CarInput : MonoBehaviour
{
    [Tooltip("אזור מת של הסטיק (0 עד 1): תזוזות קטנות מזה מתעלמים מהן.")]
    [Range(0f, 0.5f)] public float stickDeadzone = 0.12f;

    // -1..1 : forward = 1, brake/reverse = -1
    public float Throttle { get; private set; }
    // -1..1 : left = -1, right = 1
    public float Steer { get; private set; }
    // true while Space is held
    public bool Handbrake { get; private set; }

    // Set by NetServer for a car it owns: input then comes from the network instead of a local
    // keyboard/gamepad, so a dedicated server (no display, no local player) can still drive the car.
    [System.NonSerialized] public bool NetworkControlled;

    bool resetPressed;

    void Update()
    {
        if (NetworkControlled) return;   // SetNetworkInput() already wrote Throttle/Steer/Handbrake directly

        float t = 0f, s = 0f;
        bool handbrake = false;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) t += 1f;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) t -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) s += 1f;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) s -= 1f;
            handbrake = kb.spaceKey.isPressed;
            if (kb.rKey.wasPressedThisFrame) resetPressed = true;
        }

        // a gamepad that is being used wins over the keyboard (analog steering and throttle)
        Gamepad gp = Gamepad.current;
        if (gp != null)
        {
            float gt = gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
            if (Mathf.Abs(gt) > 0.02f) t = Mathf.Clamp(gt, -1f, 1f);

            float gs = Deadzone(gp.leftStick.x.ReadValue());
            if (gp.dpad.right.isPressed) gs = 1f;
            if (gp.dpad.left.isPressed) gs = -1f;
            if (gs != 0f) s = gs;

            if (gp.buttonWest.isPressed || gp.leftShoulder.isPressed) handbrake = true;
            if (gp.buttonNorth.wasPressedThisFrame) resetPressed = true;
        }

        Throttle = Mathf.Clamp(t, -1f, 1f);
        Steer = Mathf.Clamp(s, -1f, 1f);
        Handbrake = handbrake;
    }

    float Deadzone(float v)
    {
        float a = Mathf.Abs(v);
        if (a < stickDeadzone) return 0f;
        return Mathf.Sign(v) * (a - stickDeadzone) / (1f - stickDeadzone);
    }

    // NetServer calls this once per received Input packet for a network-controlled car, and
    // NetPredictor calls it once per buffered input during a replay - the latter runs many of these
    // back-to-back inside a single frame with no Update() in between, so this must take effect
    // immediately rather than waiting for Update() to copy it over (as an earlier, buffered version of
    // this method did - it worked for NetServer only because a real frame's Update() always runs
    // before that frame's FixedUpdate).
    public void SetNetworkInput(float throttle, float steer, bool handbrake, bool reset)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
        if (reset) resetPressed = true;
    }

    // NetClient reads this to forward an R press to the server without stealing it from
    // the local physics, which still consumes it itself via ConsumeReset().
    public bool PeekReset() { return resetPressed; }

    // Physics calls this once per press of R (put the car back on its wheels).
    public bool ConsumeReset()
    {
        bool r = resetPressed;
        resetPressed = false;
        return r;
    }
}
