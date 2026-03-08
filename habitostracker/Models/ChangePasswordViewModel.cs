using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "La contraseña actual es obligatoria.")]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [MinLength(8, ErrorMessage = "La contraseña debe tener mínimo 8 caracteres.")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Debe confirmar la nueva contraseña.")]
        [Compare("NewPassword", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; }
    }
}