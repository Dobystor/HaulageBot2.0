using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un histórico de acarreo
    public class Historic
    {
        // Propiedad que actúa como la clave primaria de la entidad Historic
        [Key]
        public int HistoricId { get; set; }

        // Identificador del registro del token asociado con el histórico
        public int TokenRegistryId { get; set; }

        // Identificador del vehículo asociado con el histórico, debe coincidir con el EconomicNumber del Vehicle
        public string vehicle { get; set; }

        // Nombre completo del empleado asociado con el histórico, debe coincidir con el FullName del Employee
        public string FullName { get; set; }

        // Nombre del punto de carga
        public string loadPointName { get; set; }

        // Nombre del punto de descarga
        public string unLoadPointName { get; set; }

        // Peso transportado durante el histórico
        public decimal Weight { get; set; }

        // Identificador del tipo de material asociado con el histórico
        public int materialTypeId { get; set; }

        // Fecha del acarreo representada como una cadena
        public string Dateofcarries { get; set; }

        // Identificador del turno de trabajo asociado con el histórico
        public int WorkShiftId { get; set; }

        // Propiedad de navegación que representa la relación con la entidad TokenRegistry
        public virtual TokenRegistry TokenRegistry { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Vehicle
        public virtual Vehicle VehicleNavigation { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Employee
        public virtual Employee Employee { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Route
        public virtual Route HaulagePath { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Material
        public virtual Material MaterialType { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Shift
        public virtual Shift WorkShift { get; set; }
    }
}
