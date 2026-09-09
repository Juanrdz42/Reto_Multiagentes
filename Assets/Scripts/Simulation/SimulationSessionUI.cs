using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Pull local: ejecutar Python, descargar JSON, reproducir y consultar resultados.</summary>
public class SimulationSessionUI : MonoBehaviour
{
    public string endpoint = "http://127.0.0.1:8765";
    public string pythonExecutable = "";
    public int seed = 42, steps = 800;
    public bool randomizeSeed = true;
    [Range(1, 20)] public int seedCount = 20;
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "";
    bool resultsOpen, libraryOpen;
    Vector2 libraryScroll;
    List<SimulationLibrary.Entry> saved = new List<SimulationLibrary.Entry>();
    Task<List<SimulationLibrary.Entry>> libraryRead;
    string libraryFolder;
    float progress;
    bool progressKnown;
    string activePath;

    void Start()
    {
        libraryFolder = Path.Combine(Application.persistentDataPath, "runs");
        string previous = Path.Combine(Application.persistentDataPath, "last_simulation.json");
        libraryRead = Task.Run(() =>
        {
            Directory.CreateDirectory(libraryFolder);
            string legacy = Path.Combine(libraryFolder, "previous_simulation.json");
            if (File.Exists(previous) && !File.Exists(legacy)) File.Copy(previous, legacy);
            return SimulationLibrary.Read(libraryFolder);
        });
    }

    void Update()
    {
        if (libraryRead == null || !libraryRead.IsCompleted) return;
        if (libraryRead.IsFaulted) Status = "No se pudo leer la biblioteca: " + libraryRead.Exception.GetBaseException().Message;
        else saved = libraryRead.Result;
        libraryRead = null;
    }
    public static bool ResultsVisible { get; private set; }
    bool showTable;
    Vector2 resultScroll;
    System.Diagnostics.Process server;
    readonly StringBuilder serverErrors = new StringBuilder();
    SimPlayer Player => GetComponent<SimPlayer>();
    [Serializable] class Job { public string id, status, error, phase; public float progress; }
    [Serializable] class Request { public int seed, steps, seed_count; }

    public void RunPython()
    {
        if (Busy) return;
        if (randomizeSeed) seed = (seed + UnityEngine.Random.Range(1, 1000000)) % 1000000;
        StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        Busy = true;
        progress = 0;
        progressKnown = false;
        Status = "Conectando con Python…";
        using (var health = UnityWebRequest.Get(endpoint + "/health"))
        {
            health.timeout = 3;
            yield return health.SendWebRequest();
            if (health.result != UnityWebRequest.Result.Success)
            {
                try { StartServer(); }
                catch (Exception ex) { Status = ex.Message; Busy = false; }
                if (!Busy) yield break;
                bool ready = false;
                for (int attempt = 0; attempt < 20 && !ready; attempt++)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (server != null && server.HasExited)
                    {
                        server.WaitForExit();
                        string details;
                        lock (serverErrors) details = serverErrors.ToString();
                        Fail("Python terminó al iniciar: " + details);
                        yield break;
                    }
                    using (var check = UnityWebRequest.Get(endpoint + "/health"))
                    {
                        check.timeout = 2;
                        yield return check.SendWebRequest();
                        ready = check.result == UnityWebRequest.Result.Success;
                    }
                }
                if (!ready) { Fail("Python no respondió. Revisa el intérprete y sus dependencias."); yield break; }
            }
        }
        int requestedSeed = seed;
        Status = "Calculando semilla " + requestedSeed + " · " + seedCount + " comparaciones…";
        string id;
        using (var request = new UnityWebRequest(endpoint + "/runs", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Request { seed = seed, steps = steps, seed_count = seedCount })));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { Fail("No se pudo iniciar: " + request.downloadHandler.text); yield break; }
            id = JsonUtility.FromJson<Job>(request.downloadHandler.text).id;
        }
        float deadline = Time.realtimeSinceStartup + 3600;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            using (var poll = UnityWebRequest.Get(endpoint + "/runs/" + id))
            {
                poll.timeout = 10;
                yield return poll.SendWebRequest();
                if (poll.result != UnityWebRequest.Result.Success) { Fail("Se perdió la conexión con Python."); yield break; }
                var job = JsonUtility.FromJson<Job>(poll.downloadHandler.text);
                if (job.status == "failed") { Fail(job.error); yield break; }
                progressKnown = !string.IsNullOrEmpty(job.phase);
                progress = Mathf.Clamp01(job.progress);
                Status = job.phase == "saving" ? "Preparando JSON…" :
                    "Calculando semilla " + requestedSeed + (progressKnown ? " · " + (progress * 100).ToString("0") + "%" : "…");
                if (job.status != "complete") continue;
            }
            string target = Path.Combine(libraryFolder, id + ".json");
            string temporary = target + ".download";
            using (var export = UnityWebRequest.Get(endpoint + "/runs/" + id + "/export"))
            {
                export.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                export.timeout = 120;
                var operation = export.SendWebRequest();
                while (!operation.isDone)
                {
                    progressKnown = ulong.TryParse(export.GetResponseHeader("Content-Length"), out ulong total) && total > 0;
                    progress = progressKnown ? Mathf.Clamp01((float)export.downloadedBytes / total) : 0;
                    Status = "Descargando · " + (export.downloadedBytes / 1048576f).ToString("0.0") + " MB" +
                        (progressKnown ? " · " + (progress * 100).ToString("0") + "%" : "");
                    yield return null;
                }
                if (export.result != UnityWebRequest.Result.Success)
                { Fail("No se pudo descargar el JSON: " + export.error); yield break; }
            }
            Status = "Guardando en la biblioteca…";
            progressKnown = false;
            var save = Task.Run(() =>
            {
                SimulationLibrary.Commit(temporary, target);
                return SimulationLibrary.Read(libraryFolder);
            });
            while (!save.IsCompleted) yield return null;
            if (save.IsFaulted) { Fail("No se pudo guardar: " + save.Exception.GetBaseException().Message); yield break; }
            saved = save.Result;
            progress = 1;
            progressKnown = true;
            Status = "Semilla " + requestedSeed + " guardada · ábrela en Simulaciones";
            Busy = false;
            yield break;
        }
        Fail("La corrida excedió una hora. El servidor puede seguir calculándola.");
    }

    void Fail(string message) { Status = message; Busy = false; Debug.LogError("[Python] " + message); }

    void StartServer()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string script = Path.Combine(root, "SimulationPython", "unity_server.py");
        if (!File.Exists(script)) throw new IOException("Inicia unity_server.py en Python y vuelve a solicitar la corrida.");
        string executable = pythonExecutable;
        if (string.IsNullOrWhiteSpace(executable))
        {
            string local = Path.Combine(root, ".venv", "bin", "python");
            string windows = Path.Combine(root, ".venv", "Scripts", "python.exe");
            executable = File.Exists(local) ? local : File.Exists(windows) ? windows :
                File.Exists("/tmp/unity-m4-venv/bin/python") ? "/tmp/unity-m4-venv/bin/python" : "python3";
        }
        var info = new System.Diagnostics.ProcessStartInfo(executable,
            Quote(script) + " --port " + new Uri(endpoint).Port + " --output " + Quote(Path.Combine(Application.persistentDataPath, "server-runs")))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
          RedirectStandardOutput = true, RedirectStandardError = true };
        lock (serverErrors) serverErrors.Clear();
        server = new System.Diagnostics.Process { StartInfo = info };
        server.OutputDataReceived += (_, e) => { };
        server.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (serverErrors)
            {
                serverErrors.AppendLine(e.Data);
                if (serverErrors.Length > 4000) serverErrors.Remove(0, serverErrors.Length - 4000);
            }
        };
        server.Start();
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
    }

    static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    void OnDestroy()
    {
        if (server != null)
        {
            try { if (!server.HasExited) server.Kill(); } catch (InvalidOperationException) { }
            server.Dispose();
        }
    }

    void OnGUI()
    {
        var player = Player;
        if (player == null) return;
        GUI.depth = -10;
        ResultsVisible = resultsOpen || libraryOpen;
        float width = Screen.width - MissionPanelUI.SidebarWidth;
        if (libraryOpen) { LibraryWindow(width); return; }
        if (resultsOpen) { ResultsWindow(0); return; }
        float y = Screen.height - 73;
        float cell = (width - 32) / 4;
        if (WarehouseUI.Button(new Rect(10, y, cell, 25), player.paused ? "Reproducir" : "Pausa", true)) player.paused = !player.paused;
        if (WarehouseUI.Button(new Rect(14 + cell, y, cell, 25), "Inicio")) { player.paused = true; player.Seek(0); }
        if (WarehouseUI.Button(new Rect(18 + cell * 2, y, cell, 25), "Simulaciones")) libraryOpen = true;
        GUI.enabled = true;
        if (WarehouseUI.Button(new Rect(22 + cell * 3, y, cell, 25), "Resultados")) resultsOpen = !resultsOpen;
        float step = GUI.HorizontalSlider(new Rect(12, y + 32, Mathf.Max(20, width - 110), 18), player.CurrentStep, 0, Mathf.Max(1, player.LastStep));
        if (Mathf.Abs(step - player.CurrentStep) >= 1) { player.paused = true; player.Seek(Mathf.RoundToInt(step)); }
        WarehouseUI.Text(new Rect(width - 93, y + 26, 90, 24), player.CurrentStep + " / " + player.LastStep);
        GUI.enabled = true;
        DrawProgress(new Rect(12, Screen.height - 23, width - 24, 22));

    }

    void DrawProgress(Rect rect)
    {
        WarehouseUI.Text(new Rect(rect.x, rect.y, rect.width, rect.height - 4), Status, 10);
        if (!Busy) return;
        WarehouseUI.Fill(new Rect(rect.x, rect.yMax - 3, rect.width, 3), new Color32(218,224,230,255));
        float amount = progressKnown ? progress : 0.15f;
        float left = progressKnown ? 0 : Mathf.PingPong(Time.realtimeSinceStartup * 0.35f, 0.85f);
        WarehouseUI.Fill(new Rect(rect.x + left * rect.width, rect.yMax - 3, rect.width * amount, 3), WarehouseUI.Accent);
    }

    void LibraryWindow(float width)
    {
        WarehouseUI.Fill(new Rect(0, 0, width, Screen.height), new Color32(248,250,252,255));
        WarehouseUI.Text(new Rect(22, 18, width - 125, 36), "Simulaciones", 24, true);
        if (WarehouseUI.Button(new Rect(width - 90, 22, 70, 28), "Volver")) libraryOpen = false;
        WarehouseUI.Text(new Rect(22, 60, width - 44, 40),
            "Crea una nueva semilla en segundo plano o abre una simulación guardada.", 12, false, WarehouseUI.Muted);
        GUI.enabled = !Busy;
        if (WarehouseUI.Button(new Rect(22, 108, 190, 32), "Nueva semilla", true)) RunPython();
        GUI.enabled = true;
        WarehouseUI.Text(new Rect(225, 108, Mathf.Max(30, width - 247), 32),
            "La simulación actual sigue disponible", 11, false, WarehouseUI.Muted);
        DrawProgress(new Rect(22, 151, width - 44, 38));
        WarehouseUI.Text(new Rect(22, 201, width - 44, 25), "GUARDADAS · " + saved.Count, 11, true);
        if (libraryRead != null && saved.Count == 0)
            WarehouseUI.Text(new Rect(22, 240, width - 44, 50), "Leyendo simulaciones guardadas…");
        else if (saved.Count == 0)
            WarehouseUI.Text(new Rect(22, 240, width - 44, 60), "Todavía no hay simulaciones guardadas. Crea tu primera semilla.");
        libraryScroll = GUI.BeginScrollView(new Rect(22, 236, width - 38, Mathf.Max(30, Screen.height - 256)),
            libraryScroll, new Rect(0, 0, width - 60, saved.Count * 86), false, false);
        for (int i = 0; i < saved.Count; i++)
        {
            var entry = saved[i];
            float y = i * 86, inner = width - 60;
            WarehouseUI.Fill(new Rect(0, y, inner, 78), Color.white);
            WarehouseUI.Text(new Rect(12, y + 5, inner - 110, 26), "Semilla " + entry.seed, 15, true);
            WarehouseUI.Text(new Rect(12, y + 33, inner - 110, 39), entry.date + " · " + entry.steps + " pasos · " +
                entry.seedCount + " semillas · " + (entry.bytes / 1048576f).ToString("0.0") + " MB", 10, false, WarehouseUI.Muted);
            if (WarehouseUI.Button(new Rect(inner - 88, y + 22, 76, 30), activePath == entry.path ? "Reiniciar" : "Abrir"))
            {
                try
                {
                    string json = File.ReadAllText(entry.path);
                    Player.LoadExport(json);
                    File.WriteAllText(Path.Combine(Application.persistentDataPath, "last_simulation.json"), json);
                    activePath = entry.path;
                    libraryOpen = false;
                    if (!Busy) Status = "Semilla " + entry.seed + " · pulsa Reproducir";
                }
                catch (Exception ex) { Status = "No se pudo abrir: " + ex.Message; }
            }
        }
        GUI.EndScrollView();
    }

    void OnDisable() { ResultsVisible = false; }

    public static bool TryImprovement(SimMetric metric, out float improvement)
    {
        improvement = 0;
        if (Mathf.Abs(metric.baseline) < 0.000001f) return false;
        bool lower = metric.name.StartsWith("Tiempo promedio") ||
            metric.name == "Distancia promedio recorrida" || metric.name == "Eventos de carga" ||
            metric.name.StartsWith("Balance de carga") || metric.name.StartsWith("Conflictos de ruta") ||
            metric.name == "Misiones pendientes";
        improvement = (metric.proposed - metric.baseline) / Mathf.Abs(metric.baseline) * 100 * (lower ? -1 : 1);
        return true;
    }

    void ResultsWindow(int id)
    {
        float width = Screen.width - MissionPanelUI.SidebarWidth, height = Screen.height;
        WarehouseUI.Fill(new Rect(0, 0, width, height), new Color32(248,250,252,255));
        WarehouseUI.Text(new Rect(22, 18, width - 115, 32), "Resultados", 24, true);
        if (WarehouseUI.Button(new Rect(width - 85, 22, 65, 28), "Volver"))
        { resultsOpen = false; ResultsVisible = false; }
        var results = Player.Results;
        if (results == null || results.metrics == null)
        {
            WarehouseUI.Text(new Rect(22, 70, width - 44, 70), "Ejecuta Python para generar resultados comparativos.");
            return;
        }
        WarehouseUI.Text(new Rect(22, 57, width - 44, 35),
            "Propuesta (heurística + A*) vs. baseline · Promedio de " + Mathf.Max(1, results.seed_count) + " semillas", 12, false, WarehouseUI.Muted);
        if (WarehouseUI.Button(new Rect(22, 100, 115, 28), "Mejora (%)", !showTable)) showTable = false;
        if (WarehouseUI.Button(new Rect(145, 100, 110, 28), "Datos", showTable)) showTable = true;
        WarehouseUI.Text(new Rect(22, 137, width - 44, 32),
            showTable ? "Valores finales · La simulación visible corresponde a la semilla " + results.seed :
            "Azul: mejora · Rojo: empeora · Menor tiempo, distancia y conflictos es mejor", 10, false, WarehouseUI.Muted);

        float rowHeight = 44, innerWidth = width - 60;
        float labelWidth = innerWidth * 0.43f;
        float plotLeft = labelWidth + 12, plotWidth = innerWidth - plotLeft;
        float max = 10;
        foreach (var metric in results.metrics)
            if (TryImprovement(metric, out float value)) max = Mathf.Max(max, Mathf.Abs(value));
        max = Mathf.Ceil(max / 10) * 10;
        if (showTable)
        {
            WarehouseUI.Text(new Rect(22 + labelWidth, 172, innerWidth * 0.24f, 22), "Baseline", 11, true);
            WarehouseUI.Text(new Rect(22 + labelWidth + innerWidth * 0.25f, 172, innerWidth * 0.25f, 22), "Propuesta", 11, true);
        }
        resultScroll = GUI.BeginScrollView(new Rect(22, 199, width - 38, Mathf.Max(30,height - 225)), resultScroll,
            new Rect(0,0,innerWidth,results.metrics.Count * rowHeight + 28), false, false);
        for (int i = 0; i < results.metrics.Count; i++)
        {
            var metric = results.metrics[i];
            float y = i * rowHeight;
            WarehouseUI.Fill(new Rect(0,y,innerWidth,rowHeight), i % 2 == 0 ? Color.white : new Color32(240,244,247,255));
            WarehouseUI.Text(new Rect(7,y+3,labelWidth-12,rowHeight-6), metric.name, width < 750 ? 10 : 12);
            if (showTable)
            {
                WarehouseUI.Text(new Rect(labelWidth,y,innerWidth*0.24f,rowHeight), metric.baseline.ToString("0.###"));
                WarehouseUI.Text(new Rect(labelWidth+innerWidth*0.25f,y,innerWidth*0.25f,rowHeight), metric.proposed.ToString("0.###"),12,true);
                continue;
            }
            float zero = plotLeft + plotWidth/2, half = Mathf.Max(1,plotWidth/2-45);
            WarehouseUI.Fill(new Rect(zero,y,1,rowHeight),new Color32(180,191,201,255));
            if (!TryImprovement(metric, out float change))
            {
                WarehouseUI.Text(new Rect(plotLeft,y,plotWidth,rowHeight),"N/D · baseline = 0",10,false,WarehouseUI.Muted);
                continue;
            }
            float length = Mathf.Abs(change)/max*half;
            Color bar = change >= 0 ? new Color32(44,120,210,255) : new Color32(215,75,75,255);
            WarehouseUI.Fill(new Rect(change >= 0 ? zero : zero-length,y+12,length,20),bar);
            WarehouseUI.Text(new Rect(change >= 0 ? zero+length+4 : zero-length-45,y,44,rowHeight),
                change.ToString("+0.0;-0.0;0.0")+"%",10,true,bar);
        }
        if (!showTable)
            WarehouseUI.Text(new Rect(plotLeft,results.metrics.Count*rowHeight,plotWidth,28),
                "−"+max.ToString("0")+"%                 0                 +"+max.ToString("0")+"%",10,false,WarehouseUI.Muted,TextAnchor.MiddleCenter);
        GUI.EndScrollView();
    }
}
