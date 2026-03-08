using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class EditEmailViewModel
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "Correo inválido.")]
        public string Email { get; set; }
    }
}