using UnityEngine;

public static class WarehouseUI
{
    public static readonly Color Ink = new Color32(27, 43, 60, 255);
    public static readonly Color Muted = new Color32(95, 112, 128, 255);
    public static readonly Color Accent = new Color32(12, 131, 125, 255);
    static GUIStyle text;
    public static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
    public static void Text(Rect rect, string value, int size = 12, bool bold = false, Color? color = null,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        if (text == null) text = new GUIStyle(GUI.skin.label);
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.normal.textColor = color ?? Ink;
        text.alignment = alignment;
        text.wordWrap = true;
        text.padding = new RectOffset(0,0,0,0);
        GUI.Label(rect, value, text);
    }
    public static bool Button(Rect rect, string value, bool primary = false)
    {
        Fill(rect, !GUI.enabled ? new Color32(218,224,230,255) :
            primary ? Accent : new Color32(226,234,239,255));
        Text(rect, value, rect.width < 100 ? 10 : 12, true,
            GUI.enabled ? primary ? Color.white : Ink : Muted, TextAnchor.MiddleCenter);
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }
}
