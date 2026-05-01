using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    [Index(nameof(Email), IsUnique = true)]
    public class User
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El usuario es obligatorio.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        public string PasswordHash { get; set; }

        public int FailedLoginAttempts { get; set; }

        public bool IsIpBlocked { get; set; } = false;

        public DateTime? LockoutEnd { get; set; }

        // 🔐 Rol 
        public string Role { get; set; } = "User";

        // 👤 Nombre completo
        public string? FullName { get; set; }

        // 📧 Email con validación real
   
        [EmailAddress(ErrorMessage = "Debe ingresar un correo válido.")]

       
        public string? Email { get; set; }

        public bool EmailConfirmed { get; set; } = false;


        // correo nuevo pendiente de confirmación
        public string? PendingEmail { get; set; }

        public string? ResetCode { get; set; }
        public DateTime? ResetCodeExpiry { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.Now;

        // ⚥ Género
        [Required(ErrorMessage = "Debe seleccionar un género.")]
        public string Gender { get; set; }
        public bool IsGoogleAccount { get; set; }
        public string? ProfileImage { get; set; }

        public string? ProfilePicture { get; set; }
        public string? Name { get; set; }

        public DateTime? LastOnline { get; set; }

        public bool IsBanned { get; set; } = false;

        public string? Bio { get; set; }
        public bool IsActive { get; set; } = true;

        public string? LastIp { get; set; }
        public string? Country { get; set; }

        public string? City { get; set; }

        public bool IsPremium { get; set; } = false;

        // 💳 pago Nequi
        public string? PaymentProofImage { get; set; } // imagen subida
        public bool PaymentApproved { get; set; } = false;

        public string? ISP { get; set; }
        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string? Municipality { get; set; }
        public string? Device { get; set; }
        public string? OperatingSystem { get; set; }
        public string? Browser { get; set; }

        // 🔕 silenciar notificaciones de chat
        public DateTime? MutedUntil { get; set; }
        // 🔐 Preguntas de seguridad
        public string? SecurityQuestion1 { get; set; }
        public string? SecurityAnswer1 { get; set; }
        public string? CoverImage { get; set; }
        public bool IsPrivate { get; set; } = false;
        public string? SecurityQuestion2 { get; set; }
        public string? SecurityAnswer2 { get; set; }
        public string? SecurityQuestion3 { get; set; }
        // En User.cs agregar:
        public bool HasWelcomePost { get; set; } = false;
        public int RiskLevel { get; set; } = 0; // 0=normal, 1=riesgo, 2=advertencia, 3=bloqueado
        public DateTime? LastWarningAt { get; set; }
        public string? WarningMessage { get; set; }

        // En Post.cs agregar:
        public bool CommentsDisabled { get; set; } = false;
        public bool IsOfficialAnnouncement { get; set; } = false;
        public bool IsWarningPost { get; set; } = false;

        // En Group.cs agregar:
        public bool ShowHealthWarning { get; set; } = false;
        public string? HealthWarningMessage { get; set; }
        public string? SecurityAnswer3 { get; set; }

        // 📌 chats anclados (IDs separados por coma)
        public string? PinnedChats { get; set; }


    }
}

