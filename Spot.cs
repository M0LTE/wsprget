using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace wsprget;

public record class Spot
{
    public override string ToString() => ToString(this);

    public static string ToString(Spot spot) => $"{spot.Timestamp:yyyy-MM-dd HH:mm} {spot.Call} {spot.FrequencyHz / 1000000.0} {spot.SNR} {spot.Drift} {spot.Grid} {spot.PowerDbm} {spot.PowerW} {spot.Reporter} {spot.ReporterGrid} {spot.DistanceKm} {spot.DistanceMi} {spot.Mode} {spot.Version}";

    private string? hash;

    public string Hash
    {
        get
        {
            hash ??= SHA256.HashData(Encoding.UTF8.GetBytes(ToString(this))).Aggregate(new StringBuilder(), static (sb, b) => sb.Append(b.ToString("x2"))).ToString();
            return hash;
        }
    }

    public DateTime Timestamp { get; set; }
    public required string Call { get; set; }
    public long FrequencyHz { get; set; }
    public int SNR { get; set; }
    public int Drift { get; set; }
    public required string Grid { get; set; }
    public int PowerDbm { get; set; }
    [JsonIgnore]
    public double PowerW { get; set; }
    public required string Reporter { get; set; }
    public required string ReporterGrid { get; set; }
    public int DistanceKm { get; set; }
    [JsonIgnore]
    public int DistanceMi { get; set; }
    public required string Mode { get; set; }
    public string? Version { get; set; }
}
