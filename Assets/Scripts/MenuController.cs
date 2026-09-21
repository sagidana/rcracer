using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The start menu: pick a car and a track, press Play.
// The chosen car turns slowly on the podium behind the UI (a prefab instance with its physics switched off).
// Keyboard: left/right = car, up/down = track, Enter or Space = play. The buttons do the same with the mouse.
public class MenuController : MonoBehaviour
{
    public Text carLabel;
    public Text trackLabel;
    public Transform carStage;
    [Tooltip("מהירות הסיבוב של הרכב על הפודיום (מעלות בשנייה).")]
    public float turnSpeed = 25f;

    int carIndex;
    int trackIndex;
    GameObject shown;

    void Start()
    {
        carIndex = GameSelection.IndexOf(GameSelection.Cars, GameSelection.Car);
        trackIndex = GameSelection.IndexOf(GameSelection.Tracks, GameSelection.Track);
        Refresh();
    }

    public void PrevCar() { Step(ref carIndex, -1, GameSelection.Cars.Length); }
    public void NextCar() { Step(ref carIndex, 1, GameSelection.Cars.Length); }
    public void PrevTrack() { Step(ref trackIndex, -1, GameSelection.Tracks.Length); }
    public void NextTrack() { Step(ref trackIndex, 1, GameSelection.Tracks.Length); }

    public void Play()
    {
        GameSelection.Car = GameSelection.Cars[carIndex];
        GameSelection.Track = GameSelection.Tracks[trackIndex];
        PlayerPrefs.Save();
        SceneManager.LoadScene(GameSelection.Track);
    }

    void Step(ref int index, int dir, int count)
    {
        index = (index + dir + count) % count;
        Refresh();
    }

    void Refresh()
    {
        GameSelection.Car = GameSelection.Cars[carIndex];
        GameSelection.Track = GameSelection.Tracks[trackIndex];
        if (carLabel != null) carLabel.text = GameSelection.CarLabels[carIndex];
        if (trackLabel != null) trackLabel.text = GameSelection.TrackLabels[trackIndex];
        ShowCar();
    }

    void ShowCar()
    {
        if (shown != null) Destroy(shown);
        if (carStage == null) return;
        GameObject prefab = Resources.Load<GameObject>(GameSelection.CarResourceFolder + "/" + GameSelection.Cars[carIndex]);
        if (prefab == null) return;

        shown = Instantiate(prefab, carStage);
        shown.transform.localPosition = Vector3.zero;
        shown.transform.localRotation = Quaternion.identity;

        // display only: no physics, no input, no HUD
        foreach (Behaviour b in shown.GetComponentsInChildren<Behaviour>(true))
        {
            if (b is CarController || b is CarInput || b is SpeedDisplay || b is CarWheelVisuals) b.enabled = false;
        }
        foreach (Collider c in shown.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        Rigidbody rb = shown.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // stand the wheels on the podium
        Renderer[] rs = shown.GetComponentsInChildren<Renderer>();
        float lowest = float.MaxValue;
        foreach (Renderer r in rs) if (r.enabled && r.bounds.min.y < lowest) lowest = r.bounds.min.y;
        if (lowest < float.MaxValue) shown.transform.position += Vector3.up * (carStage.position.y - lowest);
    }

    void Update()
    {
        if (carStage != null) carStage.Rotate(Vector3.up, turnSpeed * Time.deltaTime, Space.World);

        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) PrevCar();
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) NextCar();
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) PrevTrack();
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) NextTrack();
        if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Play();
    }
}
