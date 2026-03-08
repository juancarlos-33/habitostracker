using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class ConfirmEmailViewModel
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Debes ingresar el código.")]
        public string Code { get; set; }
    }
}