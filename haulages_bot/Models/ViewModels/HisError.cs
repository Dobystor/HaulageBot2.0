using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models.ViewModels
{
    public class HisError
    {
        [ Key]
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
        public string ErrorMessage { get; set; }
    }
}
