namespace HabitTrackerApp.Models
{
    public class GroupMessageReaction
    {
        public int Id { get; set; }
        public int GroupMessageId { get; set; }
        public GroupMessage Message { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Emoji { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}