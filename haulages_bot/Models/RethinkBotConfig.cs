using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models
{
    /// <summary>
    /// Configuración del bot de RethinkDB por servidor.
    /// Controla la simulación de datos en la tabla HaulageProcess.
    /// </summary>
    public class RethinkBotConfig
    {
        [Key]
        public int Id { get; set; }

        public int ServerConfigId { get; set; }

        /// <summary>Host de RethinkDB (IP o FQDN)</summary>
        public string RethinkHost { get; set; } = "";

        /// <summary>Puerto de RethinkDB (default 28015)</summary>
        public int RethinkPort { get; set; } = 28015;

        /// <summary>Contraseña de RethinkDB (default vacía para admin)</summary>
        public string RethinkPassword { get; set; } = "";

        /// <summary>Intervalo en segundos entre cada ciclo de actualización</summary>
        public int IntervalSeconds { get; set; } = 30;

        /// <summary>Cantidad máxima de vehículos simultáneos en la simulación</summary>
        public int MaxSimultaneousVehicles { get; set; } = 5;

        /// <summary>Cantidad de scooptrams adicionales en la simulación</summary>
        public int ScooptramCount { get; set; } = 3;

        /// <summary>Si el bot está activo</summary>
        public bool IsEnabled { get; set; } = false;
    }
}
