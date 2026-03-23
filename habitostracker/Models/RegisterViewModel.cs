using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApp.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "El usuario es obligatorio.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio.")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "Debes ingresar un correo válido.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Selecciona un género.")]
        public string Gender { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        public string Bio { get; set; }
    }
}