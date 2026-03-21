using System;

namespace HabitTrackerApp.Models
{
    public class Post
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        public string Username { get; set; }

        public string DisplayUsername => Username ?? "Cuenta eliminada";


        public string? ImagePath { get; set; }

       

        public string? Description { get; set; }
        public bool IsSensitive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

    }
}