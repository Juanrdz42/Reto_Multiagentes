using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Persistent exports; incomplete downloads never appear in the library.</summary>
public static class SimulationLibrary
{
    public class Entry
    {
        public string path, date;
        public int seed, steps, seedCount;
        public long bytes;
    }
    [Serializable] class Header { public SimMeta meta; public SimResults results; }

    public static List<Entry> Read(string folder)
    {
        Directory.CreateDirectory(folder);
        var entries = new List<Entry>();
        foreach (string path in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var header = JsonUtility.FromJson<Header>(File.ReadAllText(path));
                if (header?.meta == null || header.results == null || header.meta.navigation_version < 4) continue;
                var file = new FileInfo(path);
                entries.Add(new Entry { path = path, seed = header.results.seed,
                    steps = header.meta.total_steps, seedCount = Math.Max(1, header.results.seed_count),
                    date = file.LastWriteTime.ToString("dd/MM/yyyy HH:mm"), bytes = file.Length });
            }
            catch (Exception ex) { if (!(ex is IOException || ex is ArgumentException)) throw; }
        }
        entries.Sort((a, b) => File.GetLastWriteTimeUtc(b.path).CompareTo(File.GetLastWriteTimeUtc(a.path)));
        return entries;
    }

    public static void Commit(string temporary, string target)
    {
        var data = JsonUtility.FromJson<SimRoot>(File.ReadAllText(temporary));
        if (data?.meta == null || data.results == null || data.frames == null || data.frames.Count == 0 ||
            data.meta.navigation_version < 4 || data.meta.n_agvs != 4)
            throw new ArgumentException("El JSON no contiene una simulación compatible.");
        for (int i = 0; i < data.frames.Count; i++)
            if (data.frames[i].agvs == null || data.frames[i].agvs.Count != 4 ||
                (i > 0 && data.frames[i].t <= data.frames[i - 1].t))
                throw new ArgumentException("Frames inválidos o desordenados.");
        // Older servers may have written this same run to the library already.
        if (File.Exists(target)) File.Delete(temporary);
        else File.Move(temporary, target);
    }
}
