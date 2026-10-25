
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersistentPotsTracker;

public class TrackerData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tracker_id")]
    public string TrackerId { get; set; } = "";

    [JsonPropertyName("datacenter")]
    public int Datacenter { get; set; }

    [JsonPropertyName("server")]
    public int Server { get; set; }

    [JsonPropertyName("territory")]
    public int Territory { get; set; }

    [JsonPropertyName("last_update")]
    public long LastUpdate { get; set; }

    [JsonPropertyName("pot_history")]
    public string PotHistoryJson { get; set; } = "[]";

    [JsonPropertyName("fate_history")]
    public string FateHistoryJson { get; set; } = "[]";

    [JsonPropertyName("encounter_history")]
    public string EncounterHistoryJson { get; set; } = "[]";

    // Store parsed histories so we don't deserialize repeatedly.
    private List<PotData>? cachedPots;
    private List<PotData>? cachedFates;
    private List<PotData>? cachedEncounters;

    public List<PotData> GetPots()
    {
        return cachedPots ??= ParseHistory(PotHistoryJson);
    }

    public List<PotData> GetFates()
    {
        return cachedFates ??= ParseHistory(FateHistoryJson);
    }

    public List<PotData> GetEncounters()
    {
        return cachedEncounters ??= ParseHistory(EncounterHistoryJson);
    }

    private static List<PotData> ParseHistory(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            return JsonSerializer.Deserialize<List<PotData>>(json)
                   ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

public class PotData
{
    [JsonPropertyName("fate_id")]
    public int FateId { get; set; }

    [JsonPropertyName("spawn_time")]
    public long SpawnTime { get; set; }

    [JsonPropertyName("death_time")]
    public long DeathTime { get; set; }

    [JsonPropertyName("last_seen")]
    public long LastSeen { get; set; }

    [JsonPropertyName("respawn_times")]
    public List<long> RespawnTimes { get; set; } = new();

    [JsonPropertyName("killed_fates")]
    public int KilledFates { get; set; }

    [JsonPropertyName("killed_ces")]
    public int KilledCes { get; set; }

    [JsonPropertyName("state")]
    public int State { get; set; }
}
