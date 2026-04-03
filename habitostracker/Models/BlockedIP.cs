using System.ComponentModel.DataAnnotations.Schema;

namespace HabitTrackerApp.Models
{
    [Table("BlockedIPs")] // 🔥 ESTA LÍNEA ES LA CLAVE
    public class BlockedIP
    {
        public int Id { get; set; }
        public string IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}