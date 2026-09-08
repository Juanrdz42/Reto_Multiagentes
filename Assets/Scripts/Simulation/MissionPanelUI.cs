using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel en pantalla que muestra las misiones activas/recientes de la
/// simulación (id, pallet, estado, AGV asignado) leyendo SimPlayer.Instance
/// cada frame. Construye su propio Canvas/Panel/Text por código en Start(),
/// así que no depende de nada preexistente en la escena: solo agrégalo a
/// cualquier GameObject y funciona.
/// </summary>
public class MissionPanelUI : MonoBehaviour
{
    [Header("Apariencia")]
    public int fontSize = 16;
    public int maxRows = 14;
    public Vector2 panelSize = new Vector2(440, 340);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
    public Color textColor = Color.white;

    Text text;
    readonly StringBuilder sb = new StringBuilder();

    void Start()
    {
        BuildUI();
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("MissionCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = backgroundColor;
        var panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-20, -20);
        panelRect.sizeDelta = panelSize;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panelGO.transform, false);
        text = textGO.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.UpperLeft;
        text.color = textColor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 1.1f;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12, 12);
        textRect.offsetMax = new Vector2(-12, -12);
    }

    void Update()
    {
        if (text == null) return;

        var player = SimPlayer.Instance;
        if (player == null)
        {
            text.text = "Cargando simulación...";
            return;
        }

        sb.Clear();
        sb.Append("MISIONES  (completadas: ").Append(player.CurrentCompleted)
          .Append(" / ").Append(player.TotalMissions).Append(")\n\n");

        var missions = player.CurrentMissions;
        if (missions == null || missions.Count == 0)
        {
            sb.Append("(sin misiones activas)");
        }
        else
        {
            int shown = 0;
            foreach (var m in missions)
            {
                if (shown >= maxRows)
                {
                    sb.Append("... (+").Append(missions.Count - shown).Append(" más)");
                    break;
                }
                string assigned = string.IsNullOrEmpty(m.assigned) ? "-" : m.assigned;
                sb.Append(m.id).Append("  ").Append(m.pallet)
                  .Append("  [").Append(m.status).Append("]  ").Append(assigned).Append('\n');
                shown++;
            }
        }

        text.text = sb.ToString();
    }
}
