namespace HabitTrackerApp.Models
{
    public class Repost
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int PostId { get; set; }
        public DateTime CreatedAt { get; set; }
        public User? User { get; set; }
        public Post? Post { get; set; }
    }
}