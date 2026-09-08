using System;
using System.Collections.Generic;

// Clases que reflejan la estructura de simulation_export.json generado por
// entregam3.py (export_simulation). Deben mantenerse en sync con ese script.

[Serializable]
public class SimMeta
{
    public int width;
    public int height;
    public int cell_size;
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
    public string state;
}

[Serializable]
public class SimPalletFrame
{
    public string id;
    public float[] pos;
    public string state;
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
public class SimFrame
{
    public int t;
    public List<SimAgvFrame> agvs;
    public List<SimPalletFrame> pallets;
    public List<SimStationFrame> stations;
    public List<SimMissionFrame> missions;
    public int completed;
}

[Serializable]
public class SimRoot
{
    public SimMeta meta;
    public List<SimZone> zones;
    public List<SimNamedPos> chargers;
    public List<SimNamedKindPos> stations;
    public List<SimFrame> frames;
    // "log" no se mapea: JsonUtility lo ignora sin problema y no se usa en runtime.
}
