using HabitTrackerApp.Models;
using System.ComponentModel.DataAnnotations.Schema;

public class HabitComment
{
    public int Id { get; set; }

    public int HabitId { get; set; }
    public Habit Habit { get; set; }

    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public User User { get; set; }

    public string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}