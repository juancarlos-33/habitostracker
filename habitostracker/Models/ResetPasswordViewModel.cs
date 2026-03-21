using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class ResetPasswordViewModel
    {
        [Required]
        public string Email { get; set; }

        [Required]
        [MinLength(6)]
        public string Password { get; set; }

        [Required]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmPassword { get; set; }
    }
}