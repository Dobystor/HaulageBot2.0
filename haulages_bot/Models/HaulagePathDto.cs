namespace haulages_bot.Models
{
    public class HaulagePathDto
    {
        public int HaulagePathId { get; set; }
        public string Description { get; set; }
        public int LoadPointId { get; set; }
        public string LoadPointName { get; set; }
        public int UnLoadPointId { get; set; }
        public string UnLoadPointName { get; set; }
        public decimal Distance { get; set; }
        public double TimeInHour { get; set; }
        public int SelectedMaterialType { get; set; }
        public int? MaterialTypeId { get; set; }
        public string? MaterialType { get; set; }
        public bool IsExtraction { get; set; }
        public bool IsEnabled { get; set; }
    }
}
