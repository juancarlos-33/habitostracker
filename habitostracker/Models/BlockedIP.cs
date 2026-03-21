namespace HabitTrackerApp.Models
{
    public class BlockedIP
    {
        public int Id { get; set; }

        public string IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}