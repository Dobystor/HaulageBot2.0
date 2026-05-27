using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un empleado
    public class Employee
    {
        // Propiedad que actúa como la clave primaria de la entidad Employee
        [Key]
        public int EmployeeId { get; set; }

        // Número de empleado, generalmente utilizado para identificar al empleado en sistemas internos
        public decimal NoEmployee { get; set; }

        // Nombre del empleado
        public string Name { get; set; }

        // Apellido paterno del empleado
        public string PaternalLastName { get; set; }

        // Apellido materno del empleado
        public string MaternalLastName { get; set; }

        // Nombre completo del empleado, que se puede construir a partir de las propiedades Name, PaternalLastName y MaternalLastName
        public string FullName { get; set; }

        // Identificador de la empresa a la que pertenece el empleado
        public int CompanyId { get; set; }

        public int ServerConfigId { get; set; }

        // Propiedad de navegación que representa la relación con la entidad Company
        public virtual Company Company { get; set; }
    }
}
