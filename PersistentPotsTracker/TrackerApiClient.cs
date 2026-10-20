using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PersistentPotsTracker;

public class TrackerApiClient : IDisposable
{
    private readonly HttpClient httpClient = new();

    public async Task<List<TrackerData>> GetTrackersAsync(
        List<int> datacenters)
    {
        if (datacenters.Count == 0)
            return new List<TrackerData>();

        long cutoff = DateTimeOffset.UtcNow
            .AddMinutes(-10)
            .ToUnixTimeSeconds();

        string dcFilter = string.Join(",", datacenters);

        string url =
            "https://infi.ovh/api/OccultTrackerV3" +
            $"?last_update=gte.{cutoff}" +
            $"&datacenter=in.({dcFilter})";

        return await httpClient.GetFromJsonAsync<List<TrackerData>>(url)
               ?? new List<TrackerData>();
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
