using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    public class DataConfigurationLocal
    {
        [Key]
        public int Id { get; set; }

        public int ServerConfigId { get; set; }

        // Almacena la lista de variaciones de tonelaje como un JSON
        public string TonnageVariation { get; set; } // Almacena en formato JSON

        // Almacena la lista de tiempos como un JSON
        public string Time { get; set; } // Almacena en formato JSON

        public string SelectedRoutes { get; set; } // Almacena en formato JSON
        public string SelectedEmployees { get; set; } // Almacena en formato JSON
        public string SelectedVehicles { get; set; } // Almacena en formato JSON
    }
}
    