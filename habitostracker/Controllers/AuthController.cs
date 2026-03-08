using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HabitTrackerApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly HabitDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(HabitDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // 🔐 REGISTER
        [HttpPost("register")]
        public IActionResult Register(User user)
        {
            if (_context.Users.Any(u => u.Username == user.Username))
                return BadRequest("El usuario ya existe.");

            // ⚠️ En producción usar Hash real (luego lo mejoramos)
            user.PasswordHash = user.PasswordHash;

            _context.Users.Add(user);
            _context.SaveChanges();

            return Ok("Usuario creado correctamente.");
        }

        // 🔑 LOGIN
        [HttpPost("login")]
        public IActionResult Login(User login)
        {
            var user = _context.Users
                .FirstOrDefault(u => u.Username == login.Username &&
                                     u.PasswordHash == login.PasswordHash);

            if (user == null)
                return Unauthorized("Credenciales incorrectas.");

            var token = GenerateJwtToken(user);

            return Ok(new { token });
        }

        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("UserId", user.Id.ToString())
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

            var creds = new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}