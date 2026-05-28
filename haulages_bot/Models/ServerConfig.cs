using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models
{
    public class ServerConfig
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Name { get; set; } // Nombre descriptivo (ej. "Mina Principal")

        [Required]
        public string ApiUrl { get; set; } // https://demo-acarreos.smartflow.com.mx

        [Required]
        public string ClientId { get; set; }

        [Required]
        public string ClientSecret { get; set; }

        public bool IsActive { get; set; } = true;

        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? TokenExpiry { get; set; }
        public bool IsBotRunning { get; set; } = false;
        public bool IsSyncEnabledLocal { get; set; } = false;

        // Ajuste de zona horaria en horas. null = usar el valor global de appsettings.json ("TimezoneOffsetHours").
        // Ejemplo: -6 para UTC-6 (México CST). 0 = sin ajuste.
        public int? TimezoneOffsetHours { get; set; }
    }
}

