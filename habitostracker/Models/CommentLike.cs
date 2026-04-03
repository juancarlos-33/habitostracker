using HabitTrackerApp.Models;

public class CommentLike
{
    public int Id { get; set; }

    public int CommentId { get; set; }
    public PostComment Comment { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}