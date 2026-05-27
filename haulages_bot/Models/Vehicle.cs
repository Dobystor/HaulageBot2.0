using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un vehículo en el sistema
    public class Vehicle
    {
        // Propiedad que actúa como la clave primaria de la entidad Vehicle
        [Key]
        public int VehicleId { get; set; }

        // Matrícula del vehículo
        public string Plates { get; set; }

        // Número económico del vehículo, usado para identificarlo
        public string EconomicNumber { get; set; }

        // Identificador de la compañía a la que pertenece el vehículo
        public int CompanyId { get; set; }

        // Modelo del vehículo
        public string Model { get; set; }

        // Peso vacío del vehículo en kilogramos
        public decimal EmptyWeight { get; set; }

        // Capacidad del tanque de combustible en litros
        public decimal FuelTankCapacity { get; set; }

        // Peso total del vehículo en kilogramos
        public decimal Weight { get; set; }

        // Identificador del tipo de vehículo (por ejemplo, camión, automóvil, etc.)
        public int VehicleTypeId { get; set; }

        // Descripción adicional del vehículo
        public string Description { get; set; }

        public decimal LoadingCapacity { get; set; }

        public int ServerConfigId { get; set; }

        // Relación de navegación con la entidad Company
        // Permite acceder a los detalles de la compañía a la que pertenece el vehículo
        public virtual Company Company { get; set; }

        // Relación de navegación con la entidad VehicleType
        // Permite acceder a los detalles del tipo de vehículo
        public virtual VehicleType VehicleType { get; set; }
    }
}
