using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class VerifyCodeViewModel
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "El código es obligatorio.")]
        public string Code { get; set; }

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [MinLength(8, ErrorMessage = "La contraseña debe tener mínimo 8 caracteres.")]
        public string NewPassword { get; set; }
    }
}