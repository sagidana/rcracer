using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Tools > Menu > Build Menu Scene : creates Assets/Scenes/Menu.unity from scratch:
// a camera looking at a podium (the chosen car turns on it), a light, and a uGUI canvas with
// the title, a car row, a track row and a Play button, all driven by MenuController.
// Running it again rebuilds the scene. Also puts Menu first in the build settings.
public static class MenuSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Menu/Build Menu Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 3D backdrop ----
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 35f;
        camGo.transform.position = new Vector3(3.2f, 1.6f, -4.2f);
        camGo.transform.LookAt(new Vector3(-0.9f, 0.55f, 0f));   // the car sits right of center, the UI panel left

        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(6f, 1f, 6f);
        floor.GetComponent<Renderer>().sharedMaterial = RaceSceneSetup.MakeMaterial(new Color(0.16f, 0.17f, 0.19f));

        GameObject podium = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        podium.name = "Podium";
        podium.transform.localScale = new Vector3(3.6f, 0.06f, 3.6f);
        podium.transform.position = new Vector3(0f, 0.06f, 0f);
        podium.GetComponent<Renderer>().sharedMaterial = RaceSceneSetup.MakeMaterial(new Color(0.85f, 0.35f, 0.12f));

        GameObject stage = new GameObject("CarStage");
        stage.transform.position = new Vector3(0f, 0.12f, 0f);

        // ---- UI ----
        GameObject canvasGo = new GameObject("Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        MenuController menu = canvasGo.AddComponent<MenuController>();
        menu.carStage = stage.transform;

        Text title = MakeText(canvasGo.transform, "Title", "RCRACE", font, 110, FontStyle.Bold, new Vector2(0f, 1f), new Vector2(90f, -40f), new Vector2(560f, 140f), new Color(1f, 0.93f, 0.8f));
        Shadow shadow = title.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(4f, -4f);

        // left column with the two choice rows and the play button
        GameObject panel = MakePanel(canvasGo.transform, "Panel", new Vector2(0f, 0.5f), new Vector2(90f, -40f), new Vector2(560f, 470f), new Color(0f, 0f, 0f, 0.45f));
        menu.carLabel = MakeRow(panel.transform, "Car", "CAR", font, 150f, menu.PrevCar, menu.NextCar);
        menu.trackLabel = MakeRow(panel.transform, "Track", "TRACK", font, 20f, menu.PrevTrack, menu.NextTrack);
        Button play = MakeButton(panel.transform, "PlayButton", "PLAY", font, 54, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 84f), new Color(0.85f, 0.35f, 0.12f));
        UnityEventTools.AddPersistentListener(play.onClick, new UnityAction(menu.Play));

        MakeText(canvasGo.transform, "Hint", "Arrows / WASD drive   Space handbrake   R reset   Esc menu", font, 26, FontStyle.Normal, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(1400f, 40f), new Color(1f, 1f, 1f, 0.7f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        TrackSceneBuilder.UpdateBuildSettings();
        Debug.Log("Menu scene saved: " + ScenePath + ". Build settings updated.");
    }

    // a row: caption on the left, "<" value ">" on the right; returns the value text
    static Text MakeRow(Transform parent, string name, string caption, Font font, float y, UnityAction prev, UnityAction next)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y); rt.sizeDelta = new Vector2(520f, 110f);

        MakeText(row.transform, "Caption", caption, font, 24, FontStyle.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(340f, 28f), new Color(1f, 0.93f, 0.8f, 0.75f));
        Button left = MakeButton(row.transform, "Prev", "<", font, 44, new Vector2(0f, 0f), new Vector2(40f, 8f), new Vector2(64f, 64f), new Color(1f, 1f, 1f, 0.15f));
        Button right = MakeButton(row.transform, "Next", ">", font, 44, new Vector2(1f, 0f), new Vector2(-40f, 8f), new Vector2(64f, 64f), new Color(1f, 1f, 1f, 0.15f));
        UnityEventTools.AddPersistentListener(left.onClick, prev);
        UnityEventTools.AddPersistentListener(right.onClick, next);
        return MakeText(row.transform, "Value", "-", font, 46, FontStyle.Bold, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(340f, 64f), Color.white);
    }

    static GameObject MakePanel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    static Text MakeText(Transform parent, string name, string text, Font font, int size, FontStyle style, Vector2 anchor, Vector2 pos, Vector2 box, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = box;
        Text t = go.AddComponent<Text>();
        t.text = text; t.font = font; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Button MakeButton(Transform parent, string name, string label, Font font, int size, Vector2 anchor, Vector2 pos, Vector2 box, Color color)
    {
        GameObject go = MakePanel(parent, name, anchor, pos, box, color);
        Button b = go.AddComponent<Button>();
        ColorBlock cb = b.colors;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        b.colors = cb;
        MakeText(go.transform, "Label", label, font, size, FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero, box, Color.white);
        return b;
    }
}
