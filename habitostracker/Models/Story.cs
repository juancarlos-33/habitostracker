using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HabitTrackerApp.Models
{
    public class Story
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        // Tipo: "image", "video", "text"
        public string Type { get; set; } = "image";

        // URL de imagen o video (Cloudinary)
        public string? MediaUrl { get; set; }

        // Texto si es tipo texto
        public string? TextContent { get; set; }

        // Color de fondo si es texto
        public string? BgColor { get; set; } = "#6366f1";

        // Duración en segundos (10 para foto/texto, máx 30 para video)
        public int Duration { get; set; } = 7;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        [Column(TypeName = "text")]
        public string? Caption { get; set; }
        [Column(TypeName = "varchar(20)")]
        public string Visibility { get; set; } = "friends"; // "private", "friends", "public"

        // Expira 24h después
        public DateTime ExpiresAt { get; set; } = DateTime.Now.AddHours(24);

        // Destacada — visible para todos aunque perfil privado
        public bool IsHighlight { get; set; } = false;

        // Vistas
        public ICollection<StoryView> Views { get; set; } = new List<StoryView>();
    }

    public class StoryView
    {
        public int Id { get; set; }
        public int StoryId { get; set; }
        [ForeignKey("StoryId")]
        public Story Story { get; set; }
        public int ViewerId { get; set; }
        [ForeignKey("ViewerId")]
        public User Viewer { get; set; }
        public DateTime ViewedAt { get; set; } = DateTime.Now;
    }
}