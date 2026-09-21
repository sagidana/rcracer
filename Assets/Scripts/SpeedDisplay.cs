using UnityEngine;

// Shows the car speed as simple text in the bottom-right corner.
[RequireComponent(typeof(Rigidbody))]
public class SpeedDisplay : MonoBehaviour
{
    public float fontSizeAt720p = 36f;

    Rigidbody rb;
    GUIStyle style;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void OnGUI()
    {
        if (rb == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.LowerRight;
            style.fontStyle = FontStyle.Bold;
        }

        float scale = Screen.height / 720f;
        style.fontSize = Mathf.RoundToInt(fontSizeAt720p * scale);

        Vector3 v = rb.linearVelocity;
        v.y = 0f;
        string text = Mathf.RoundToInt(v.magnitude * 3.6f) + " km/h";

        float w = 400f * scale, h = 80f * scale, margin = 20f * scale;
        Rect r = new Rect(Screen.width - w - margin, Screen.height - h - margin, w, h);

        style.normal.textColor = Color.black; // shadow
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        style.normal.textColor = Color.white;
        GUI.Label(r, text, style);
    }
}
