using UnityEngine;

/// <summary>Panel lateral y navegación fuera del área de la cámara.</summary>
public class MissionPanelUI : MonoBehaviour
{
    static MissionPanelUI instance;
    public static MissionPanelUI Instance
    {
        get { if (instance == null) instance = FindFirstObjectByType<MissionPanelUI>(); return instance; }
        private set => instance = value;
    }
    public static float SidebarWidth => Mathf.Min(300f, Screen.width * (Screen.width < 800 ? 0.29f : 0.24f));
    public static float FooterHeight => 120f;
    public static Rect Viewport => Instance != null && Instance.isActiveAndEnabled
        ? new Rect(0f, FooterHeight / Screen.height, Mathf.Max(0.1f, 1f - SidebarWidth / Screen.width), 1f - FooterHeight / Screen.height)
        : new Rect(0f, 0f, 1f, 1f);

    readonly Color ink = new Color32(25, 40, 56, 255);
    readonly Color muted = new Color32(106, 119, 130, 255);
    readonly Color accent = new Color32(12, 131, 125, 255);
    readonly Color background = new Color32(246, 248, 250, 255);
    Vector2 scroll;
    GUIStyle label;
    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    void Text(Rect rect, string value, int size, Color color, bool bold = false)
    {
        label.fontSize = size;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.normal.textColor = color;
        GUI.Label(rect, value, label);
    }

    void OnGUI()
    {
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
        }
        var player = SimPlayer.Instance;
        float width = SidebarWidth, x = Screen.width - width;
        float pad = width < 220 ? 12 : 20;
        float content = width - pad * 2;
        bool compact = Screen.height < 560 || width < 220;
        int body = compact ? 11 : 13;
        Fill(new Rect(x, 0, width, Screen.height), background);
        Fill(new Rect(x, 0, 1, Screen.height), new Color32(220, 226, 231, 255));
        Fill(new Rect(x + pad, 20, 4, 24), accent);
        Text(new Rect(x + pad + 14, 17, content - 14, 28), "Almacén", compact ? 19 : 23, ink, true);
        Text(new Rect(x + pad, 51, content, 20), player == null ? "Cargando…" : "SIMULACIÓN · PASO " + player.CurrentStep,
            compact ? 9 : 11, muted);

        float y = 78;
        Text(new Rect(x + pad, y, content, 18), "FLOTA · BATERÍA", 10, muted, true);
        y += 23;
        for (int i = 1; i <= 4; i++)
        {
            var agv = player == null || player.CurrentAgvs == null ? null : player.CurrentAgvs.Find(a => a.id == "AGV-" + i);
            float battery = agv == null ? 0 : Mathf.Clamp(agv.battery, 0, 100);
            Color chargeColor = battery <= 30 ? new Color32(192, 80, 48, 255) : accent;
            Text(new Rect(x + pad, y, content * 0.6f, 18), "AGV " + i + (agv != null && agv.state == "CHARGING" ? (agv.charge_phase == "CHARGING" ? " · Carga" : agv.charge_phase == "WAITING" ? " · Espera" : " · Ruta") : ""), body, ink, true);
            label.alignment = TextAnchor.UpperRight;
            Text(new Rect(x + pad + content * 0.6f, y, content * 0.4f, 18), agv == null ? "—" : battery.ToString("0") + "%", body, chargeColor, true);
            label.alignment = TextAnchor.UpperLeft;
            Fill(new Rect(x + pad, y + 20, content, 4), new Color32(220,228,233,255));
            if (agv != null) Fill(new Rect(x + pad, y + 20, content * battery / 100, 4), chargeColor);
            y += compact ? 32 : 36;
        }
        y += 10;
        float total = player == null ? 0 : player.TotalMissions;
        float completed = player == null ? 0 : player.CurrentCompleted;
        Text(new Rect(x + pad, y, content, 18), "MISIONES COMPLETADAS", 10, muted, true);
        y += 21;
        Text(new Rect(x + pad, y, content, 30), ((int)completed) + " / " + (int)total, 24, ink, true);
        y += 36;
        Fill(new Rect(x + pad, y, content, 4), new Color32(221,229,233,255));
        Fill(new Rect(x + pad, y, content * Mathf.Clamp01(total > 0 ? completed / total : 0), 4), accent);
        y += 14;

        int alarms = 0;
        if (player != null && player.CurrentStations != null)
            foreach (var station in player.CurrentStations)
                if (station.state == "OUT_OF_SERVICE") alarms++;
        Text(new Rect(x + pad, y, content, 22), alarms == 0 ? "●  Estaciones disponibles" : "!  " + alarms + " estaciones en alerta",
            body, alarms == 0 ? accent : new Color32(155, 76, 17, 255));
        y += compact ? 30 : 38;
        Text(new Rect(x + pad, y, content, 20), "ACTIVIDAD", compact ? 9 : 11, muted, true);
        y += 26;

        var missions = player == null ? null : player.CurrentMissions;
        int count = missions == null ? 0 : missions.Count;
        float rowHeight = compact ? 64 : 74;
        float alertHeight = alarms * 52f;
        Rect list = new Rect(x + pad, y, width - pad - 6, Mathf.Max(1, Screen.height - y - 12));
        float rowWidth = Mathf.Max(30, list.width - 16);
        scroll = GUI.BeginScrollView(list, scroll,
            new Rect(0, 0, rowWidth, Mathf.Max(list.height, alertHeight + count * rowHeight)), false, false);
        float alertY = 0;
        if (alarms > 0)
            foreach (var station in player.CurrentStations)
                if (station.state == "OUT_OF_SERVICE")
                {
                    Fill(new Rect(0, alertY, rowWidth, 46), new Color32(255, 239, 222, 255));
                    Text(new Rect(8, alertY + 5, rowWidth - 16, 18), station.id, body, ink, true);
                    Text(new Rect(8, alertY + 24, rowWidth - 16, 18), "Fuera de servicio", body, muted);
                    alertY += 52;
                }
        if (count == 0) Text(new Rect(0, alertY + 6, rowWidth, 25), "Sin misiones activas", body, muted);
        for (int i = 0; i < count; i++)
        {
            var mission = missions[i];
            float row = alertHeight + i * rowHeight;
            Fill(new Rect(0, row, rowWidth, rowHeight - 8), Color.white);
            Fill(new Rect(0, row, 3, rowHeight - 8), mission.status == "PENDIENTE" ? new Color32(197, 206, 214, 255) : accent);
            string status = mission.status == "COMPLETADA" ? "Completada" : mission.status == "ASIGNADA" ? "En curso" : "En espera";
            Text(new Rect(10, row + 8, rowWidth - 18, 21), mission.id + " · Caja " + mission.pallet, body, ink, true);
            float half = (rowWidth - 20) / 2;
            Text(new Rect(10, row + 32, half, 20), string.IsNullOrEmpty(mission.assigned) ? "Sin asignar" : mission.assigned, compact ? 10 : 12, muted);
            Text(new Rect(10 + half, row + 32, half, 20), status, compact ? 10 : 12, accent);
        }
        GUI.EndScrollView();

        if (SimulationSessionUI.ResultsVisible) return;
        float footerY = Screen.height - FooterHeight;
        Fill(new Rect(0, footerY, x, FooterHeight), new Color32(237, 242, 245, 255));
        Fill(new Rect(0, footerY, x, 1), new Color32(213, 222, 228, 255));
        var cameras = player == null ? null : player.GetComponent<SimulationCameraController>();
        float gap = x < 500 ? 5 : 8;
        float cell = Mathf.Min(122, (x - 24 - gap * 4) / 5);
        float left = (x - cell * 5 - gap * 4) / 2;
        for (int i = 1; i <= 5; i++)
        {
            bool selected = cameras != null && cameras.SelectedView == i;
            var rect = new Rect(left + (i - 1) * (cell + gap), footerY + 9, cell, 30);
            Fill(rect, selected ? accent : Color.white);
            // Dibujo y zona clicable separados para que el texto siempre quepa.
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) cameras?.SelectView(i);
            Text(new Rect(rect.x + 6, rect.y + 3, 14, rect.height - 6), i.ToString(), 10,
                selected ? Color.white : muted, true);
            label.alignment = TextAnchor.MiddleCenter;
            Text(new Rect(rect.x + 19, rect.y, rect.width - 23, rect.height), i == 5 ? "Plano" : "AGV " + i,
                cell < 80 ? 10 : 12, selected ? Color.white : ink, true);
            label.alignment = TextAnchor.UpperLeft;
        }
    }
}
