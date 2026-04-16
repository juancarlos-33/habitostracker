using System;
using System.Collections.Generic;

namespace HabitTrackerApp.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int CreatorId { get; set; }
        public User Creator { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public ICollection<GroupMember> Members { get; set; }
        public ICollection<GroupMessage> Messages { get; set; }
    }
}