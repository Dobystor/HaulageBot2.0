using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un acarreo
    public class Haulage
    {
        // Propiedad que actúa como la clave primaria de la entidad Haulage
        [Key]
        public int? HaulageId { get; set; }

        // Identificador del vehículo, debe coincidir con el EconomicNumber del Vehicle
        public int? VehicleId { get; set; }

        // Identificador del empleado, debe coincidir con el FullName del Employee
        public int? EmployeeId { get; set; }

        // Identificador del recorrido del acarreo
        public int? PathId { get; set; }

        // Peso transportado en el acarreo
        public decimal? Weight { get; set; }

        // Comentarios adicionales sobre el acarreo
        public string? Comments { get; set; }

        // Fecha del acarreo en formato de cadena
        public string? Dateofcarries { get; set; }

        // Identificador opcional del tipo de material
        public int? materialTypeId { get; set; }

        // Identificador opcional del punto de carga
        public int? LoadPointId { get; set; }

        // Identificador opcional del punto de descarga
        public int? UnloadPointId { get; set; }

        // Identificador opcional del turno de trabajo
        public int? ShiftId { get; set; }

        // Nombre opcional del punto de carga
        public string? LoadPointName { get; set; }

        // Nombre opcional del punto de descarga
        public string? UnloadPointName { get; set; }

        // Tipo de ley aplicable al acarreo
        public string? LawType { get; set; }

        // Kilómetros recorridos durante el acarreo
        public decimal? Kilometers { get; set; }

        // Tipo de material transportado
        public string? MaterialType { get; set; }

        // Snapshots de nombres al momento de registro (para que el histórico sea inmutable)
        public string? VehicleEconomicNumber { get; set; }
        public string? EmployeeFullName { get; set; }
        public string? RouteDescription { get; set; }

        public int ServerConfigId { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Vehicle
        public virtual Vehicle? VehicleNavigation { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Employee
        public virtual Employee? Employee { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Route
        public virtual Route? HaulagePath { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Material
        public virtual Material? MaterialTypee { get; set; }
    }
}
