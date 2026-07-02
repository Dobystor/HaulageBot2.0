using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models
{
    public class ProductionPlanBotConfig
    {
        [Key]
        public int Id { get; set; }
        public int ServerConfigId { get; set; }

        /// <summary>Tonelaje mínimo por ruta/mes</summary>
        public int TonnageMin { get; set; } = 3000;
        /// <summary>Tonelaje máximo por ruta/mes</summary>
        public int TonnageMax { get; set; } = 15000;

        /// <summary>Ley mínima para minerales en gr/ton (AG, AU)</summary>
        public decimal LawMinGrTon { get; set; } = 50;
        /// <summary>Ley máxima para minerales en gr/ton</summary>
        public decimal LawMaxGrTon { get; set; } = 150;

        /// <summary>Ley mínima para minerales en % (PB, FE, AS, CU, ZN, etc)</summary>
        public decimal LawMinPercent { get; set; } = 0.5m;
        /// <summary>Ley máxima para minerales en %</summary>
        public decimal LawMaxPercent { get; set; } = 5;

        /// <summary>Si el bot está activo (automático al inicio de mes)</summary>
        public bool IsEnabled { get; set; } = false;
    }
}
