using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "Debe ingresar un correo válido.")]
        public string Email { get; set; }
    }
}