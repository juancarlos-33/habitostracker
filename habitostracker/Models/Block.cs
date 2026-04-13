namespace HabitTrackerApp.Models
{
    public class Block
    {
        public int Id { get; set; }
        public int BlockerId { get; set; }
        public int BlockedId { get; set; }
        public DateTime CreatedAt { get; set; }
        public User Blocker { get; set; }
        public User Blocked { get; set; }
    }
}