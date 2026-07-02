using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models
{
    public class InventoryBotConfig
    {
        [Key]
        public int Id { get; set; }

        public int ServerConfigId { get; set; }

        /// <summary>Tonelaje mínimo por sitio</summary>
        public int TonnageMin { get; set; } = 200;

        /// <summary>Tonelaje máximo por sitio</summary>
        public int TonnageMax { get; set; } = 800;

        /// <summary>Cantidad mínima de sitios por turno</summary>
        public int SitesMin { get; set; } = 2;

        /// <summary>Cantidad máxima de sitios por turno</summary>
        public int SitesMax { get; set; } = 5;

        /// <summary>Si el bot está activo</summary>
        public bool IsEnabled { get; set; } = false;
    }
}
