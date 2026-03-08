using System;

namespace HabitTrackerApp.Models
{
    public class AdminLog
    {
        public int Id { get; set; }

        public int AdminId { get; set; }

        public string AdminName { get; set; }

        public int TargetUserId { get; set; }

        public string TargetUsername { get; set; }

        public string Action { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}