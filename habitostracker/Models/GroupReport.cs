using System;

namespace HabitTrackerApp.Models
{
    public class GroupReport
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public int ReporterId { get; set; }
        public User Reporter { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}