public class BlockedDevice
{
    public int Id { get; set; }

    public string Fingerprint { get; set; }

    public int? UserId { get; set; } // opcional (por si quieres asociarlo)

    public DateTime BlockedAt { get; set; } = DateTime.Now;
}