using System;
using System.ComponentModel.DataAnnotations;
using haulages_bot.Models;

namespace haulages_bot.Models
{
    // Clase que representa un turno de trabajo
    public class Shift
    {
        // Propiedad que actúa como la clave primaria de la entidad Shift
        [Key]
        public int WorkShiftId { get; set; }

        // Identificador del turno (podría ser un código o número de turno)
        public int ShiftId { get; set; }

        // Descripción del turno
        public string Description { get; set; }

        // Estado de habilitación del turno, indicando si está activo o no
        public string Enabled { get; set; }

        // Hora de inicio del turno
        public TimeSpan StartTime { get; set; }

        // Hora de finalización del turno
        public TimeSpan EndTime { get; set; }

        // Tiempo total de operación del turno (duración del turno)
        public TimeSpan OperationTime { get; set; }

        public int ServerConfigId { get; set; }
    }
}
