using System;
using System.Collections.Generic;

namespace HabitTrackerApp.Models
{
    public class HabitProgress
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public int HabitId { get; set; }
        public Habit Habit { get; set; }
        public string Message { get; set; } = "";
        public int StreakDays { get; set; }
        public int CompletionRate { get; set; } // % últimos 7 días
        public DateTime SharedAt { get; set; } = DateTime.UtcNow;
        public ICollection<HabitProgressReaction> Reactions { get; set; } = new List<HabitProgressReaction>();
        public ICollection<HabitProgressComment> Comments { get; set; } = new List<HabitProgressComment>();
    }

    public class HabitProgressReaction
    {
        public int Id { get; set; }
        public int HabitProgressId { get; set; }
        public HabitProgress HabitProgress { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Emoji { get; set; } = "🔥"; // 🔥💪❤️👏🎯
    }

    public class HabitProgressComment
    {
        public int Id { get; set; }
        public int HabitProgressId { get; set; }
        public HabitProgress HabitProgress { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}