using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un registro de programación de acarreo
    public class ProgrammingRecord
    {
        // Propiedad que actúa como la clave primaria de la entidad ProgrammingRecord
        [Key]
        public int ProgrammingRecordId { get; set; }

        // Identificador del acarreo asociado con el registro de programación
        public int HaulageId { get; set; }

        // Identificador del empleado asociado con el registro de programación
        public int EmployeeId { get; set; }

        // Tiempo de acarreo representado como un TimeSpan
        public TimeSpan Dateofcarries { get; set; }

        public int ServerConfigId { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Haulage
        public virtual Haulage Haulage { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Employee
        public virtual Employee Employee { get; set; }
    }
}

