using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa una empresa
    public class Company
    {
        // Propiedad que actúa como la clave primaria de la entidad Company
        [Key]
        public int CompanyId { get; set; }

        // Nombre de la empresa
        public string Name { get; set; }

        public int ServerConfigId { get; set; }
    }
}
