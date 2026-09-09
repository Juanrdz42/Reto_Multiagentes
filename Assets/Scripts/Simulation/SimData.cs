using System;
using System.Collections.Generic;

// Clases que reflejan la estructura de simulation_export.json generado por
// entregam3.py (export_simulation). Deben mantenerse en sync con ese script.

[Serializable]
public class SimMeta
{
    public int navigation_version;
    public bool authoritative_positions;
    public int width;
    public int height;
    public int cell_size;
    public float agv_diameter;
    public float pallet_diameter;
    public float pedestrian_radius = 12f;
    public float PalletDiameter => pallet_diameter > 0f ? pallet_diameter : cell_size * 0.7f;
    public float agv_clearance;
    public string[] navigation_obstacles;

    // Compatibilidad con exportaciones anteriores, que representaban AGVs como puntos.
    public float AgvDiameter => agv_diameter > 0f ? agv_diameter : cell_size * 0.8f;
    public float AgvClearance => Math.Max(agv_clearance, AgvDiameter / 2f);
    public bool IsObstacle(string name) => navigation_obstacles != null
        ? Array.IndexOf(navigation_obstacles, name) >= 0 : name.StartsWith("Rack");

    public int frame_sample;
    public int n_agvs;
    public int total_steps;
    public int total_completed;
    public int total_missions;
}

[Serializable]
public class SimZone
{
    public string name;
    public float x;
    public float y;
    public float width;
    public float height;
    public string color;
}

[Serializable]
public class SimNamedPos
{
    public string name;
    public float[] pos;
}

[Serializable]
public class SimNamedKindPos
{
    public string name;
    public string kind;
    public float[] pos;
}

[Serializable]
public class SimAgvFrame
{
    public string id;
    public float[] pos;
    public float battery;
    public string charge_phase;
    public string state;
}

[Serializable]
public class SimPalletFrame
{
    public string id;
    public float[] pos;
    public string state;
    public string storage_zone;
    public int storage_level;
    public bool removed;
}

[Serializable]
public class SimStationFrame
{
    public string id;
    public string state;
    public string pallet;
}

[Serializable]
public class SimMissionFrame
{
    public string id;
    public string pallet;
    public string status;
    public string assigned;
}

[Serializable]
public class SimPedestrianFrame
{
    public float[] pos;
}

[Serializable]
public class SimFrame
{
    public int t;
    public List<SimAgvFrame> agvs;
    public List<SimPalletFrame> pallets;
    public List<SimStationFrame> stations;
    public List<SimMissionFrame> missions;
    public int completed;
    public List<SimPedestrianFrame> pedestrians;
}

[Serializable]
public class SimRoot
{
    public SimResults results;
    public SimMeta meta;
    public List<SimZone> zones;
    public List<SimNamedPos> chargers;
    public List<SimNamedKindPos> stations;
    public List<SimFrame> frames;
    // "log" no se mapea: JsonUtility lo ignora sin problema y no se usa en runtime.
}

[Serializable]
public class SimMetric { public string name; public float baseline, proposed; }
[Serializable]
public class SimResults { public int seed_count, playback_seed; public int[] seeds; public int seed, steps; public string source; public List<SimMetric> metrics; }
