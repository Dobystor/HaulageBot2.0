using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace haulages_bot.Models
{
    public class HistoricDataGridDto
    {
        [JsonPropertyName("vehicle")]
        public string Vehicle { get; set; }

        [JsonPropertyName("employee")]
        public string Employee { get; set; }

        [JsonPropertyName("workshiftName")]
        public string WorkshiftName { get; set; }

        [JsonPropertyName("vehicleCompanyName")]
        public string VehicleCompanyName { get; set; }

        [JsonPropertyName("employeeCompanyName")] // Corrige cualquier typo aquí
        public string EmployeeCompanyName { get; set; } // Ojo con diferencias

        [JsonPropertyName("operationTime")]
        public double OperationTime { get; set; }

        [JsonPropertyName("tonsTransported")]
        public double TonsTransported { get; set; }

        [JsonPropertyName("materialTypeName")]
        public string MaterialTypeName { get; set; }

        [JsonPropertyName("loadPointName")]
        public string LoadPointName { get; set; }

        [JsonPropertyName("unloadPointName")]
        public string UnloadPointName { get; set; }

        [JsonPropertyName("weighingType")]
        public string WeighingType { get; set; }

        [JsonPropertyName("weightType")]
        public string WeightType { get; set; }

        [JsonPropertyName("userRegister")]
        public string UserRegister { get; set; }

        [JsonPropertyName("modifiedDate")]
        public DateTime? ModifiedDate { get; set; }

        [JsonPropertyName("unloadDate")]
        public DateTime? UnloadDate { get; set; }

        [JsonPropertyName("tareDate")]
        public DateTime? TareDate { get; set; }

        [JsonPropertyName("comments")]
        public string Comments { get; set; }
    }

}
