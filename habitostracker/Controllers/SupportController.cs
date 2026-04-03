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

            var rnd = new Random();
            message = message.ToLower();

            // 🔹 SALUDOS
            if (message.Contains("hola") || message.Contains("buenas") || message.Contains("que mas") || message.Contains("hey"))
            {
                return "Que cuentas?";
            }

            // 🔹 TRISTEZA / BAJÓN
            if (message.Contains("triste") || message.Contains("solo") || message.Contains("deprimido") || message.Contains("cansado") || message.Contains("mierda") || message.Contains("mal") || message.Contains("vacío"))
            {
                return "Siento que te estés sintiendo así 💙. A veces todo pesa, pero hablarlo ya es un paso importante. ¿Qué te tiene así?";
            }

            // 🔹 ANSIEDAD / ESTRÉS
            if (message.Contains("ansiedad") || message.Contains("estres") || message.Contains("estresado"))
            {
                var respuestas = new List<string>
    {
        "La ansiedad puede ser muy pesada. Intenta respirar profundo unos minutos.",
        "Respira lento: inhala 4 segundos, sostén 4, exhala 6 🫁",
        "Tu mente va rápido, pero puedes bajarle el ritmo.",
        "Haz una pausa, aléjate un momento y vuelve poco a poco."
    };

                return respuestas[rnd.Next(respuestas.Count)];
            }

            // 🔹 MOTIVACIÓN
            if (message.Contains("motivacion") || message.Contains("motivado") || message.Contains("ganas") || message.Contains("pereza") || message.Contains("no quiero hacer nada"))
            {
                return "La motivación no siempre está, pero los hábitos sí. Empieza con algo pequeño hoy, aunque sea 5 minutos 🔥.";
            }

            // 🔹 SUEÑO
            if (message.Contains("dormir") || message.Contains("sueño") || message.Contains("insomnio") || message.Contains("no puedo dormir"))
            {
                return "Dormir bien es clave. Intenta acostarte a la misma hora y evita el celular antes de dormir. Tu mente necesita descanso 🧠.";
            }

            // 🔹 HÁBITOS
            if (message.Contains("habito") || message.Contains("hábito") || message.Contains("disciplina") || message.Contains("constancia"))
            {
                return "Los hábitos se construyen poco a poco. No busques perfección, busca constancia 💪.";
            }

            // 🔹 AMOR / RELACIONES
            if (message.Contains("novia") || message.Contains("novio") || message.Contains("pareja") || message.Contains("terminamos") || message.Contains("ruptura") || message.Contains("me dejaron"))
            {
                return "Las relaciones pueden doler mucho 💔. Date tiempo, no tienes que estar bien de inmediato. ¿Qué pasó exactamente?";
            }

            // 🔹 SOLEDAD
            if (message.Contains("nadie") || message.Contains("solo en la vida") || message.Contains("no tengo amigos"))
            {
                return "Sentirse solo es duro… pero no estás completamente solo. Aquí puedes hablar sin filtro 💙.";
            }

            // 🔹 FRUSTRACIÓN / FRACASO
            if (message.Contains("fracaso") || message.Contains("fallé") || message.Contains("no sirvo") || message.Contains("todo me sale mal"))
            {
                return "Fallar no te define. Literalmente todos fallamos. Lo importante es que lo intentaste, y puedes volver a hacerlo 🔥.";
            }

            // 🔹 UNIVERSIDAD / ESTUDIO
            if (message.Contains("estudio") || message.Contains("universidad") || message.Contains("parcial") || message.Contains("no entiendo nada"))
            {
                return "Es normal sentirse perdido a veces. Intenta dividir el problema en partes pequeñas y empezar por una sola cosa 📚.";
            }

            // 🔹 DINERO / TRABAJO
            if (message.Contains("dinero") || message.Contains("trabajo") || message.Contains("sin plata"))
            {
                return "Las preocupaciones económicas pesan mucho… pero paso a paso puedes mejorar tu situación. No te rindas 💪.";
            }

            // 🔹 BAJA AUTOESTIMA
            if (message.Contains("no valgo") || message.Contains("soy inútil") || message.Contains("no sirvo para nada"))
            {
                return "No hables así de ti 😤. Estás pasando un mal momento, pero eso no define quién eres.";
            }

            // 🔹 IRA / RABIA
            if (message.Contains("rabia") || message.Contains("odio") || message.Contains("me da ira"))
            {
                return "La rabia también es válida. Respira, aléjate un momento y evita reaccionar impulsivamente.";
            }

            // 🔹 APOYO GENERAL
            if (message.Contains("no sé qué hacer") || message.Contains("ayuda"))
            {
                return "Estoy aquí contigo, puedes hablar sin filtro 💙";
            }

            if (message.Contains("no puedo más") || message.Contains("me siento mal"))
            {
                return "No tienes que cargar todo solo, suéltalo aquí.";
            }

            // 🔹 TRISTEZA
            if (message.Contains("me siento vacío") || message.Contains("sin ganas"))
            {
                return "No tienes que estar bien hoy, solo no te rindas.";
            }

            // 🔹 ANSIEDAD
            if (message.Contains("me cuesta respirar") || message.Contains("ataque"))
            {
                return "Respira conmigo: inhala 4 segundos, sostén 4, exhala 6… repítelo 🫁";
            }

            // 🔹 DESMOTIVACIÓN
            if (message.Contains("no tengo energía") || message.Contains("sin motivación"))
            {
                return "Haz lo mínimo hoy, pero hazlo. Eso ya cuenta.";
            }

            // 🔹 PERDIDO
            if (message.Contains("estoy perdido") || message.Contains("no sé qué hacer con mi vida"))
            {
                return "No tener todo claro también es parte del proceso.";
            }

            // 🔹 RUPTURAS
            if (message.Contains("me rompieron") || message.Contains("me terminaron"))
            {
                return "Lo que dolió fue real, por eso duele tanto.";
            }

            // 🔹 SOLEDAD
            if (message.Contains("me siento solo") || message.Contains("nadie me habla"))
            {
                return "A veces uno se siente invisible… pero aquí te estoy leyendo 👀";
            }

            // 🔹 FRUSTRACIÓN
            if (message.Contains("no me sale") || message.Contains("no puedo"))
            {
                return "Intentarlo ya te pone por encima de muchos.";
            }

            // 🔹 AUTOESTIMA
            if (message.Contains("soy un fracaso") || message.Contains("no valgo"))
            {
                return "No eres lo que piensas en tus peores momentos.";
            }

            // 🔹 IRA
            if (message.Contains("quiero explotar") || message.Contains("me da mucha rabia"))
            {
                return "Antes de reaccionar, toma distancia un momento.";
            }

            // 🔹 PROCRASTINACIÓN
            if (message.Contains("lo dejo para después") || message.Contains("no hago nada"))
            {
                return "Empieza con 2 minutos, literal.";
            }

            // 🔹 ESTUDIO
            if (message.Contains("no entiendo") || message.Contains("me va mal en clase"))
            {
                return "No entender algo hoy no significa que no puedas mañana.";
            }

            // 🔹 CANSANCIO
            if (message.Contains("estoy agotado") || message.Contains("no doy más"))
            {
                return "Tu mente también necesita descanso.";
            }

            // 🔹 EXISTENCIAL
            if (message.Contains("no tiene sentido") || message.Contains("para qué vivir"))
            {
                return "A veces el sentido se construye, no se encuentra.";
            }


            // 🔹 PROCRASTINACIÓN
            if (message.Contains("procrastino") || message.Contains("dejo todo para después"))
            {
                return "Empieza con algo pequeño. No necesitas hacerlo perfecto, solo empezar 🔥.";
            }

            // 🔹 GYM / SALUD
            if (message.Contains("gym") || message.Contains("ejercicio") || message.Contains("entrenar"))
            {
                return "El ejercicio ayuda muchísimo a la mente y al cuerpo. Incluso una caminata ya suma 💪.";
            }

            // 🔹 COMIDA / SALUD
            if (message.Contains("comer") || message.Contains("comida") || message.Contains("no como bien"))
            {
                return "Tu alimentación influye en cómo te sientes. Intenta pequeños cambios poco a poco 🍎.";
            }

            // 🔹 EXISTENCIAL
            if (message.Contains("para que vivir") || message.Contains("sentido de la vida"))
            {
                return "Esa es una pregunta profunda… a veces no hay una sola respuesta. Pero tu historia aún no termina 💙.";
            }

            // 🔹 DEFAULT
            return "No lo entiendo parcero";
        }
    }
}