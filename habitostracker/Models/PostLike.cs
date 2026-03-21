namespace HabitTrackerApp.Models
{
    public class PostLike
    {
        public int Id { get; set; }

        public int PostId { get; set; }

        public int UserId { get; set; }
    }
}