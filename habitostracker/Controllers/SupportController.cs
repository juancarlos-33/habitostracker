using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
                return Json(new { response = "No recibí ningún mensaje." });

            var userId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 contexto: últimos 6 mensajes del usuario
            var historial = _context.SupportMessages
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(6)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            string response = GenerateSmartResponse(message, historial);

            var msg = new SupportMessage
            {
                UserId = userId,
                Message = message,
                Response = response,
                CreatedAt = DateTime.Now
            };

            _context.SupportMessages.Add(msg);
            _context.SaveChanges();

            return Json(new { response });
        }

        [HttpPost]
        public IActionResult ClearHistory()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var msgs = _context.SupportMessages.Where(m => m.UserId == userId).ToList();
            _context.SupportMessages.RemoveRange(msgs);
            _context.SaveChanges();
            return Json(new { success = true });
        }

        private string GenerateSmartResponse(string message, List<SupportMessage> historial)
        {
            var rnd = new Random();
            var msg = message.ToLower().Trim();

            // 🔥 detectar intensidad emocional
            bool intenso = msg.Contains("no puedo más") || msg.Contains("ya no aguanto") ||
                           msg.Contains("quiero morir") || msg.Contains("para qué vivir") ||
                           msg.Contains("no tiene sentido vivir") || msg.Contains("quiero desaparecer");

            bool muyMal = msg.Contains("horrible") || msg.Contains("devastado") ||
                          msg.Contains("destrozado") || msg.Contains("odio mi vida") ||
                          msg.Contains("todo está mal");

            // 🔥 contexto previo
            string ultimoTema = "";
            if (historial.Any())
            {
                var ultimo = historial.Last().Message.ToLower();
                if (ultimo.Contains("triste") || ultimo.Contains("deprimido") || ultimo.Contains("solo"))
                    ultimoTema = "tristeza";
                else if (ultimo.Contains("ansiedad") || ultimo.Contains("estrés") || ultimo.Contains("nervioso"))
                    ultimoTema = "ansiedad";
                else if (ultimo.Contains("novia") || ultimo.Contains("pareja") || ultimo.Contains("amor"))
                    ultimoTema = "relacion";
            }

            // 🔥 CRISIS — máxima prioridad
            if (intenso)
            {
                var crisis = new[]
                {
                    "Lo que sientes ahora es muy pesado, y entiendo que estás en un momento muy difícil 💙. No estás solo/a. ¿Puedes contarme qué pasó exactamente?",
                    "Ese nivel de dolor que describes es real y merece atención. Aquí estoy contigo. ¿Tienes a alguien cercano con quien puedas estar ahora mismo?",
                    "Cuando todo se siente tan pesado, es importante no cargarlo solo/a. Cuéntame más, ¿qué está pasando?",
                    "Estoy aquí y te estoy escuchando de verdad. Ese dolor que sientes importa. ¿Qué es lo que más te está pesando en este momento?"
                };
                return crisis[rnd.Next(crisis.Length)];
            }

            // 🔥 SALUDOS
            if (msg.Contains("hola") || msg.Contains("buenas") || msg.Contains("que mas") ||
                msg.Contains("hey") || msg.Contains("buen día") || msg.Contains("buenos días"))
            {
                var saludos = new[]
                {
                    "¡Hola! 👋 ¿Cómo estás hoy? Aquí puedes hablar sin filtro.",
                    "¡Qué bueno que pasaste! ¿Cómo va todo?",
                    "Hey 👋 ¿Qué está pasando? Cuéntame.",
                    "Hola, ¿cómo te has sentido hoy?",
                    "¡Hola! Este es un espacio seguro. ¿Qué tienes en mente?"
                };
                return saludos[rnd.Next(saludos.Length)];
            }

            // 🔥 TRISTEZA / DEPRESIÓN
            if (msg.Contains("triste") || msg.Contains("tristeza") || msg.Contains("llorar") ||
                msg.Contains("lloro") || msg.Contains("deprimido") || msg.Contains("depresión") ||
                msg.Contains("bajón") || msg.Contains("vacío") || msg.Contains("sin ganas") ||
                msg.Contains("apagado") || muyMal)
            {
                if (ultimoTema == "tristeza")
                {
                    var followUp = new[]
                    {
                        "Sigue contándome... ¿hay algo específico que lo disparó?",
                        "Entiendo que sigue pesando. ¿Cuánto tiempo llevas sintiéndote así?",
                        "¿Has podido hablar de esto con alguien cercano a ti?",
                        "A veces ese peso tiene una raíz. ¿Sabes qué lo está causando?"
                    };
                    return followUp[rnd.Next(followUp.Length)];
                }
                var tristeza = new[]
                {
                    "Siento que estés pasando por eso 💙. La tristeza puede ser muy pesada. ¿Desde cuándo te sientes así?",
                    "Está bien no estar bien. ¿Qué es lo que más te tiene así?",
                    "Que lo cuentes ya es un paso. ¿Qué pasó?",
                    "No tienes que cargar eso solo/a. Cuéntame más.",
                    "Ese vacío es incómodo y real. ¿Hay algo puntual que lo causó?"
                };
                return tristeza[rnd.Next(tristeza.Length)];
            }

            // 🔥 ANSIEDAD / ESTRÉS / NERVIOS
            if (msg.Contains("ansioso") || msg.Contains("ansiedad") || msg.Contains("estrés") ||
                msg.Contains("estresado") || msg.Contains("nervioso") || msg.Contains("me cuesta respirar") ||
                msg.Contains("agitado") || msg.Contains("angustia") || msg.Contains("angustiado") ||
                msg.Contains("pánico") || msg.Contains("ataque de pánico"))
            {
                var ansiedad = new[]
                {
                    "La ansiedad puede ser muy intensa 😮‍💨. Prueba esto: inhala 4 segundos, sostén 4, exhala 6. Repítelo 3 veces. ¿Qué la está disparando?",
                    "Cuando el cuerpo se acelera así, hay que bajarle el ritmo. Respira profundo y dime: ¿qué está pasando?",
                    "El estrés acumulado tiene que salir de alguna forma. ¿Qué es lo que más te está pesando?",
                    "Antes de cualquier cosa, respira. Luego cuéntame qué está pasando 🫁",
                    "La ansiedad miente mucho — hace todo parecer peor. ¿Qué situación específica te tiene así?"
                };
                return ansiedad[rnd.Next(ansiedad.Length)];
            }

            // 🔥 SOLEDAD
            if (msg.Contains("solo") || msg.Contains("soledad") || msg.Contains("nadie me entiende") ||
                msg.Contains("no tengo amigos") || msg.Contains("nadie me habla") ||
                msg.Contains("me siento invisible") || msg.Contains("no le importo a nadie"))
            {
                var soledad = new[]
                {
                    "Sentirse solo es de lo más duro que existe 💙. Pero aquí te estoy escuchando. ¿Desde cuándo te sientes así?",
                    "Esa sensación de invisibilidad duele mucho. ¿Hay alguien en tu vida con quien puedas conectar?",
                    "No estás completamente solo/a — aquí puedes hablar sin filtro. ¿Qué pasó?",
                    "A veces uno puede estar rodeado de gente y aun así sentirse solo. ¿Es eso lo que pasa?",
                    "Que lo cuentes ya importa. Aquí te leo 👀 ¿Qué está pasando?"
                };
                return soledad[rnd.Next(soledad.Length)];
            }

            // 🔥 RELACIONES / AMOR
            if (msg.Contains("novia") || msg.Contains("novio") || msg.Contains("pareja") ||
                msg.Contains("terminamos") || msg.Contains("ruptura") || msg.Contains("me dejaron") ||
                msg.Contains("me dejó") || msg.Contains("amor") || msg.Contains("enamorado") ||
                msg.Contains("me rompieron el corazón") || msg.Contains("infiel") || msg.Contains("me traicionó"))
            {
                if (ultimoTema == "relacion")
                {
                    var followUp = new[]
                    {
                        "¿Cuánto tiempo estuvieron juntos?",
                        "¿Fue algo de repente o venía de antes?",
                        "¿Cómo estás manejando eso día a día?",
                        "¿Tienes personas cercanas que sepan lo que estás viviendo?"
                    };
                    return followUp[rnd.Next(followUp.Length)];
                }
                var relaciones = new[]
                {
                    "Eso duele mucho 💔. Las relaciones dejan huella. ¿Qué pasó?",
                    "El corazón tarda en procesar lo que la mente ya entendió. ¿Cuándo pasó?",
                    "Date tiempo — no tienes que estar bien de inmediato. ¿Qué fue lo que pasó?",
                    "Ese dolor es real y válido. ¿Quieres contarme más?",
                    "Las rupturas son un duelo. ¿Cómo te has sentido estos días?"
                };
                return relaciones[rnd.Next(relaciones.Length)];
            }

            // 🔥 AUTOESTIMA / FRACASO
            if (msg.Contains("no sirvo") || msg.Contains("soy un fracaso") || msg.Contains("no valgo") ||
                msg.Contains("soy inútil") || msg.Contains("todo me sale mal") || msg.Contains("fallé") ||
                msg.Contains("soy malo") || msg.Contains("no soy suficiente") || msg.Contains("me odio"))
            {
                var autoestima = new[]
                {
                    "No eres lo que piensas en tus peores momentos 💙. ¿Qué pasó que te hace sentir así?",
                    "Fallaste en algo, no eres un fracaso. Hay diferencia 🔥. ¿Qué ocurrió?",
                    "Ese diálogo interno tan duro no es la verdad. ¿De dónde viene ese pensamiento?",
                    "Lo que describes suena a que te estás siendo muy duro/a contigo mismo/a. ¿Qué pasó exactamente?",
                    "Todos fallamos. Lo que importa es lo que haces después. ¿Qué fue lo que salió mal?"
                };
                return autoestima[rnd.Next(autoestima.Length)];
            }

            // 🔥 IRA / RABIA
            if (msg.Contains("rabia") || msg.Contains("odio") || msg.Contains("me da ira") ||
                msg.Contains("quiero explotar") || msg.Contains("estoy furioso") || msg.Contains("me enoja") ||
                msg.Contains("me tiene harto") || msg.Contains("me tiene hasta la") || msg.Contains("me hartó"))
            {
                var ira = new[]
                {
                    "La rabia es válida, pero hay que manejarla bien. ¿Qué pasó?",
                    "Antes de reaccionar, toma distancia un momento. ¿Qué te tiene así?",
                    "Eso que sientes tiene una causa. Cuéntame qué pasó.",
                    "La ira no resuelta quema por dentro. ¿Qué o quién te tiene así?",
                    "Ok, respira primero 😤. Ahora cuéntame qué pasó."
                };
                return ira[rnd.Next(ira.Length)];
            }

            // 🔥 MOTIVACIÓN / PEREZA
            if (msg.Contains("sin motivación") || msg.Contains("pereza") || msg.Contains("no quiero hacer nada") ||
                msg.Contains("procrastino") || msg.Contains("no tengo energía") || msg.Contains("agotado") ||
                msg.Contains("cansado de todo") || msg.Contains("no doy más"))
            {
                var motivacion = new[]
                {
                    "La motivación viene y va — los hábitos se quedan. Empieza con algo pequeño hoy 🔥.",
                    "Haz lo mínimo hoy, pero hazlo. Eso ya cuenta.",
                    "El cuerpo y la mente también necesitan descanso real. ¿Llevas mucho tiempo así?",
                    "A veces la pereza esconde agotamiento real. ¿Estás durmiendo bien?",
                    "Empieza con 2 minutos, en serio. El arranque es lo más difícil."
                };
                return motivacion[rnd.Next(motivacion.Length)];
            }

            // 🔥 SUEÑO / INSOMNIO
            if (msg.Contains("dormir") || msg.Contains("insomnio") || msg.Contains("no puedo dormir") ||
                msg.Contains("no duermo") || msg.Contains("me desvelo") || msg.Contains("trasnoche"))
            {
                var sueno = new[]
                {
                    "El insomnio es agotador 😴. ¿Es algo reciente o llevas tiempo así?",
                    "No dormir bien afecta todo lo demás. ¿Qué crees que lo está causando?",
                    "Intenta acostarte a la misma hora y evitar el celular 30 min antes. ¿Tienes mucho en la cabeza?",
                    "La mente que no descansa no para. ¿Qué te está dando vueltas de noche?"
                };
                return sueno[rnd.Next(sueno.Length)];
            }

            // 🔥 ESTUDIO / TRABAJO
            if (msg.Contains("estudio") || msg.Contains("universidad") || msg.Contains("parcial") ||
                msg.Contains("no entiendo") || msg.Contains("me va mal") || msg.Contains("trabajo") ||
                msg.Contains("jefe") || msg.Contains("me van a echar") || msg.Contains("despedir"))
            {
                var trabajo = new[]
                {
                    "Esa presión académica/laboral puede ser muy pesada. ¿Qué está pasando exactamente?",
                    "No entender algo hoy no significa que no puedas mañana. ¿En qué estás atascado?",
                    "Divide el problema en partes pequeñas y empieza por una sola cosa 📚.",
                    "Las preocupaciones del trabajo o estudio pesan mucho. ¿Qué es lo que más te preocupa?"
                };
                return trabajo[rnd.Next(trabajo.Length)];
            }

            // 🔥 HÁBITOS / DISCIPLINA
            if (msg.Contains("habito") || msg.Contains("hábito") || msg.Contains("disciplina") ||
                msg.Contains("constancia") || msg.Contains("no soy constante"))
            {
                var habitos = new[]
                {
                    "Los hábitos se construyen poco a poco. No busques perfección, busca constancia 💪.",
                    "La disciplina no es motivación — es decisión. ¿Qué hábito quieres construir?",
                    "Empieza tan pequeño que sea imposible fallar. ¿Qué quieres cambiar?",
                    "La constancia supera al talento siempre. ¿Qué te está costando mantener?"
                };
                return habitos[rnd.Next(habitos.Length)];
            }

            // 🔥 FAMILIA
            if (msg.Contains("familia") || msg.Contains("papá") || msg.Contains("mamá") ||
                msg.Contains("hermano") || msg.Contains("hermana") || msg.Contains("padres") ||
                msg.Contains("mi casa") || msg.Contains("problemas en casa"))
            {
                var familia = new[]
                {
                    "Los problemas familiares pesan mucho porque vienen de donde más importa. ¿Qué está pasando?",
                    "La familia puede ser apoyo o fuente de dolor. ¿Qué ocurrió?",
                    "Eso en casa es difícil de cargar. ¿Desde cuándo está así la situación?",
                    "Cuéntame más sobre lo que está pasando en casa."
                };
                return familia[rnd.Next(familia.Length)];
            }

            // 🔥 DINERO
            if (msg.Contains("dinero") || msg.Contains("plata") || msg.Contains("deudas") ||
                msg.Contains("económico") || msg.Contains("sin trabajo") || msg.Contains("desempleo"))
            {
                var dinero = new[]
                {
                    "Las preocupaciones económicas quitan el sueño y el ánimo. ¿Qué está pasando?",
                    "Ese estrés financiero es muy real. ¿Qué es lo más urgente que tienes encima?",
                    "Paso a paso se puede mejorar. No te rindas 💪. ¿Cómo está la situación?",
                    "El dinero afecta todo lo demás. ¿Cómo estás manejando eso emocionalmente?"
                };
                return dinero[rnd.Next(dinero.Length)];
            }

            // 🔥 AGRADECIMIENTO / DESPEDIDA
            if (msg.Contains("gracias") || msg.Contains("me ayudó") || msg.Contains("me sirvió") ||
                msg.Contains("chao") || msg.Contains("adiós") || msg.Contains("hasta luego"))
            {
                var despedida = new[]
                {
                    "Para eso estoy 💙. Vuelve cuando quieras hablar.",
                    "Me alegra que haya servido. Cuídate mucho 🙌",
                    "Aquí estaré cuando lo necesites. ¡Ánimo!",
                    "Fue un placer escucharte. No olvides cuidarte 💙"
                };
                return despedida[rnd.Next(despedida.Length)];
            }

            // 🔥 BIEN / POSITIVO
            if (msg.Contains("estoy bien") || msg.Contains("me siento bien") || msg.Contains("feliz") ||
                msg.Contains("contento") || msg.Contains("genial") || msg.Contains("excelente") ||
                msg.Contains("lo logré") || msg.Contains("lo hice"))
            {
                var positivo = new[]
                {
                    "¡Qué bueno escuchar eso! 🙌 ¿Qué pasó?",
                    "Me alegra mucho 😊. ¿Qué fue lo que mejoró?",
                    "Eso se celebra 🔥. Cuéntame.",
                    "¡Bien! Esos momentos también importan. ¿Qué te tiene así de bien?"
                };
                return positivo[rnd.Next(positivo.Length)];
            }

            // 🔥 PREGUNTAS EXISTENCIALES
            if (msg.Contains("para qué") || msg.Contains("no tiene sentido") ||
                msg.Contains("propósito") || msg.Contains("qué hago con mi vida") ||
                msg.Contains("no sé qué quiero"))
            {
                var existencial = new[]
                {
                    "Esas preguntas son profundas y válidas. A veces el sentido se construye, no se encuentra.",
                    "No tener todo claro también es parte del proceso. ¿Qué te está generando esa duda?",
                    "Pocas personas tienen todo claro. Lo importante es seguir moviéndose. ¿Qué está pasando?",
                    "Esa búsqueda ya dice mucho de ti. ¿Qué es lo que más te inquieta?"
                };
                return existencial[rnd.Next(existencial.Length)];
            }

            // 🔥 CONTINUAR CONVERSACIÓN — si hay contexto previo
            if (historial.Count >= 2)
            {
                var continuacion = new[]
                {
                    "¿Cómo te has sentido después de lo que me contaste antes?",
                    "Siguiendo lo que me dijiste... ¿cómo vas?",
                    "¿Ha cambiado algo desde que hablamos?",
                    "¿Hubo algo que te ayudó o empeoró desde entonces?"
                };
                return continuacion[rnd.Next(continuacion.Length)];
            }

            // 🔥 DEFAULT MEJORADO
            var defaults = new[]
            {
                "Cuéntame más, te estoy escuchando 💙",
                "No te entendí del todo, ¿puedes contarme más?",
                "Aquí estoy. ¿Qué está pasando?",
                "Sigue, te estoy leyendo 👀",
                "¿Qué más está pasando? Cuéntame."
            };
            return defaults[rnd.Next(defaults.Length)];
        }
    }
}