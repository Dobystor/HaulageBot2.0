using System;
using System.ComponentModel.DataAnnotations;

namespace haulages_bot.Models
{
    // Clase que representa un tipo de material en la aplicación
    public class Material
    {
        // Propiedad que actúa como la clave primaria de la entidad Material
        [Key]
        public int materialTypeId { get; set; }

        // Nombre del tipo de material
        public string name { get; set; }

        public int ServerConfigId { get; set; }
    }
}
