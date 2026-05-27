using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace haulages_bot.Models
{
    public class DataConfBoot 
    {
        //public string TonnageVariation { get; set; }
        //public string Time { get; set; }
        public List<int> TonnageVariation { get; set; } // Lista de dos enteros para la variación de tonelaje
        public List<int> Time { get; set; }             // Lista de dos enteros para el tiempo
        public List<int> SelectedRoutes { get; set; }    // Asumiendo que los valores son IDs
        public List<int> SelectedEmployees { get; set; } // Asumiendo que los valores son IDs
        public List<int> SelectedVehicles { get; set; }  // Asumiendo que los valores son IDs
    }
}
