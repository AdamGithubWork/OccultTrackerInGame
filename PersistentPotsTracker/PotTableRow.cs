namespace PersistentPotsTracker;

public class PotTableRow
{
    public string Datacenter { get; set; } = "";
    public string Zone { get; set; } = "";
    public string NextPot { get; set; } = "";
    public long NextSpawnTime { get; set; }
}
