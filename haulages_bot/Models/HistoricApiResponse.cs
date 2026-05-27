using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace haulages_bot.Models
{
    public class HistoricApiResponse
    {
        public int HaulageId { get; set; }
        public int VehicleId { get; set; }
        public string Vehicle { get; set; } // Ejemplo: "CAA-0022"
        public int EmployeeId { get; set; }
        public string Employee { get; set; } // Ejemplo: "MARTIN TAMAYO PUEBLA"
        public int WorkshiftId { get; set; }
        public string WorkshiftName { get; set; } // Ejemplo: "First"
        public int VehicleCompanyId { get; set; }
        public string VehicleCompanyName { get; set; } // Ejemplo: "LASEC"
        public int EmployeeCompanyId { get; set; }
        public string EmployeCompanyName { get; set; } // Ejemplo: "LASEC"
        public double OperationTime { get; set; } // Ejemplo: 0.0793618473
        public double TonsTransported { get; set; } // Ejemplo: 22.5515565163
        public int MaterialTypeId { get; set; }
        public string MaterialTypeName { get; set; } // Ejemplo: "MINERAL"
        public int LoadPointId { get; set; }
        public string LoadPointName { get; set; } // Ejemplo: "R 67-385"
        public int UnloadPointId { get; set; }
        public string UnloadPointName { get; set; } // Ejemplo: "PLANTA 1"
        public int WeighingType { get; set; } // Ejemplo: 0 (numérico)
        public int WeightType { get; set; } // Ejemplo: 1 (numérico)
        public DateTime? LastTareUpdate { get; set; } // Mapea desde "lastTareUpdate"
        public string UserRegister { get; set; } // Ejemplo: "lalo"
        public DateTime? ModifiedDate { get; set; } // Ejemplo: "2024-11-05T08:47:05.5077085"
        public bool IsExtraction { get; set; }
        public DateTime? UnloadDate { get; set; } // Ejemplo: "2024-11-05T08:47:05.2662914"
        public string Comments { get; set; } // Ejemplo: "Comentario generado a partir de los datos."
    }

}
