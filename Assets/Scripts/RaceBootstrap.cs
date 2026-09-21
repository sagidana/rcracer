using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Lives in every track scene. When the scene starts it spawns the chosen car prefab at the SpawnPoint
// and points the camera at it. Esc (or the gamepad's Options button) goes back to the menu.
// (Track scenes contain no car: the car comes from Assets/Resources/Cars/<name>.prefab.)
public class RaceBootstrap : MonoBehaviour
{
    [Tooltip("איפה הרכב מתחיל. ריק = המקום של האובייקט הזה.")]
    public Transform spawnPoint;

    [Tooltip("לבדיקות בעורך: שם רכב שיטען תמיד (Buggy / Roadster / Bike). ריק = מה שנבחר בתפריט.")]
    public string carOverride = "";

    public GameObject Car { get; private set; }

    void Start()
    {
        string carName = string.IsNullOrEmpty(carOverride) ? GameSelection.Car : carOverride;
        GameObject prefab = Resources.Load<GameObject>(GameSelection.CarResourceFolder + "/" + carName);
        if (prefab == null)
        {
            Debug.LogError("RaceBootstrap: no car prefab named '" + carName + "' in Resources/" + GameSelection.CarResourceFolder + ". Run Tools > Cars > Build Car Prefabs.");
            return;
        }

        Transform sp = spawnPoint != null ? spawnPoint : transform;
        Car = Instantiate(prefab, sp.position, sp.rotation);
        Car.name = "Car";

        Camera cam = Camera.main;
        if (cam == null) return;
        FollowCamera follow = cam.GetComponent<FollowCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<FollowCamera>();
        follow.target = Car.transform;
        cam.transform.position = sp.position - sp.forward * follow.distance + Vector3.up * follow.height;
        cam.transform.LookAt(sp.position + Vector3.up * follow.lookHeight);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Gamepad gp = Gamepad.current;
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);   // Esc or Options
        if (!back) return;
        if (Application.CanStreamedLevelBeLoaded(GameSelection.MenuScene))
            SceneManager.LoadScene(GameSelection.MenuScene);
    }
}
