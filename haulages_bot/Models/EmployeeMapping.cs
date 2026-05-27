using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    public class EmployeeMapping 
    {
        public int EmployeeId { get; set; }
        public string NoEmployee { get; set; }
        public string Name { get; set; }
        public string PaternalLastName { get; set; }
        public string MaternalLastName { get; set; }
        public int CompanyId { get; set; }
        public int EmployeeTypeId { get; set; } // Campo de tipo de empleado
    }
}
