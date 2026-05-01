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
        public string Privacy { get; set; } = "Public"; // "Public", "Friends", "Private"


        public string? ImagePath { get; set; }
        public bool CommentsDisabled { get; set; } = false;
        public bool IsOfficialAnnouncement { get; set; } = false;
        public bool IsWarningPost { get; set; } = false;
        public bool IsWelcomePost { get; set; } = false; // para la publicación de bienvenida



        public string? Description { get; set; }
        public bool IsSensitive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

    }
}