namespace HabitTrackerApp.Models
{
    public class Payment
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public User User { get; set; } // 🔥 ESTA ES LA CLAVE

        public string Screenshot { get; set; }

        public bool IsRejected { get; set; } = false;
        public string? AdminNote { get; set; }

        public bool IsApproved { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}