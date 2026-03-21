using System;

namespace HabitTrackerApp.Models
{
    public class Habit
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public List<HabitComment> Comments { get; set; }

        public bool Completed { get; set; }
        public int StreakDays { get; set; }
        public int MaxStreak { get; set; }
        public DateTime? LastCheckDate { get; set; }

        public DateTime CreatedDate { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

    }
}