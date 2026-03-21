using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Chat;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class SupportController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IConfiguration _config;

        public SupportController(HabitDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        public IActionResult Chat()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var messages = _context.SupportMessages
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            return View(messages);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return Json(new { response = "No recibí ningún mensaje." });
            }

            var userId = int.Parse(User.FindFirst("UserId").Value);

            // 🔹 Preguntar a la IA real
            string response = GenerateSmartResponse(message);

            var msg = new SupportMessage
            {
                UserId = userId,
                Message = message,
                Response = response,
                CreatedAt = DateTime.Now
            };

            _context.SupportMessages.Add(msg);
            _context.SaveChanges();

            return Json(new { response = response });
        }

        private string GenerateSmartResponse(string message)
        {
            message = message.ToLower();

            // TRISTEZA
            if (message.Contains("triste") || message.Contains("solo") || message.Contains("deprimido") || message.Contains("cansado") || message.Contains("mierda"))
            {
                return "No me importa bro";
            }

            if (message.Contains("hola") || message.Contains("como estas") || message.Contains("que mas"))
            {
                return "Aja cuentame?";
            }

            // ANSIEDAD
            if (message.Contains("ansiedad") || message.Contains("estres") || message.Contains("estresado"))
            {
                return "La ansiedad puede ser muy pesada. Intenta respirar profundo por unos minutos. Un hábito útil es caminar 10 minutos o hacer una pausa lejos del celular.";
            }

            // MOTIVACION
            if (message.Contains("motivacion") || message.Contains("motivado") || message.Contains("ganas"))
            {
                return "La motivación va y viene, pero los hábitos construyen progreso. Empieza con algo pequeño hoy, incluso 5 minutos pueden marcar diferencia.";
            }

            // SUEÑO
            if (message.Contains("dormir") || message.Contains("sueño") || message.Contains("insomnio"))
            {
                return "Dormir bien es muy importante. Intenta crear el hábito de dormir a la misma hora todos los días y evitar el celular antes de acostarte.";
            }

            // HABITOS
            if (message.Contains("habito") || message.Contains("hábito"))
            {
                return "Los hábitos se construyen poco a poco. Empieza con algo muy pequeño que puedas repetir cada día.";
            }

            // DEFAULT
            return "no te entiendo bro";
        }
    }
}