using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models.ViewModels.Historics
{
    public class CustomHistoric
    {
        public int HistoricId { get; set; }
        public int TokenRegistryId { get; set; }
        public string vehicle { get; set; } // Este debe ser el EconomicNumber del Vehicle
        public string FullName { get; set; } // Este debe ser el FullName del Employee
        public string loadPointName { get; set; }
        public string unLoadPointName { get; set; }
        public decimal Weight { get; set; }
        public int materialTypeId { get; set; }
        public string Dateofcarries { get; set; }
        public int WorkShiftId { get; set; }

        public virtual TokenRegistry TokenRegistry { get; set; }
        public virtual Vehicle VehicleNavigation { get; set; }
        public virtual Employee Employee { get; set; }
        public virtual Route HaulagePath { get; set; }
        public virtual Material MaterialType { get; set; }
        public virtual Shift WorkShift { get; set; }
    }
}
