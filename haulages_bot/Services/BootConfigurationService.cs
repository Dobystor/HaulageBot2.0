using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Models;
using haulages_bot.Data;

namespace haulages_bot.Services
{
    public class BootConfigurationService 
    {
        private readonly dbboot _context;

        // Campos de estado en segundo plano (para mantener el estado por instancia inyectada Scoped)
        private List<int> _tonnageVariation = new List<int>();
        private List<int> _time = new List<int>();
        private List<int> _selectedRoutes = new List<int>();
        private List<int> _selectedEmployees = new List<int>();
        private List<int> _selectedVehicles = new List<int>();

        public BootConfigurationService(dbboot context)
        {
            _context = context;
        }

        // Setters de configuración
        public void SetTonnageVariation(List<int> tonnageVariation) => _tonnageVariation = tonnageVariation;
        public void SetTime(List<int> time) => _time = time;
        public void SetSelectedRoutes(List<int> selectedRoutes) => _selectedRoutes = selectedRoutes;
        public void SetSelectedEmployees(List<int> selectedEmployees) => _selectedEmployees = selectedEmployees;
        public void SetSelectedVehicles(List<int> selectedVehicles) => _selectedVehicles = selectedVehicles;

        // Getters para elementos aleatorios
        public int GetRandomRoute() => GetRandomElement(_selectedRoutes);
        public int GetRandomEmployee() => GetRandomElement(_selectedEmployees);
        public int GetRandomVehicle() => GetRandomElement(_selectedVehicles);
        public int GetRandomTime() => GetRandomElement(_time);

        public decimal GetRandomTonnageWeight(decimal capacityVehicle)
        {
            return GetRandomTonnageWeight(_tonnageVariation, capacityVehicle);
        }

        // Método con sobrecarga/parámetro opcional para serverId para resolver errores de compilación
        public async Task<DataConfBoot?> GetDataConfiguration(int? serverId = null)
        {
            var query = _context.DataConfigurationLocal.AsQueryable();
            if (serverId.HasValue)
            {
                query = query.Where(dc => dc.ServerConfigId == serverId.Value);
            }

            var latestConfig = await query
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync();

            if (latestConfig != null)
            {
                return new DataConfBoot
                {
                    TonnageVariation = JsonConvert.DeserializeObject<List<int>>(latestConfig.TonnageVariation) ?? new List<int>(),
                    Time = JsonConvert.DeserializeObject<List<int>>(latestConfig.Time) ?? new List<int>(),
                    SelectedRoutes = JsonConvert.DeserializeObject<List<int>>(latestConfig.SelectedRoutes) ?? new List<int>(),
                    SelectedEmployees = JsonConvert.DeserializeObject<List<int>>(latestConfig.SelectedEmployees) ?? new List<int>(),
                    SelectedVehicles = JsonConvert.DeserializeObject<List<int>>(latestConfig.SelectedVehicles) ?? new List<int>()
                };
            }

            return null;
        }

        public async Task SaveDataConfiguration(DataConfBoot datos, int serverId = 0)
        {
            var existingConfig = await _context.DataConfigurationLocal
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            
            if (existingConfig != null)
            {
                _context.DataConfigurationLocal.Remove(existingConfig);
                await _context.SaveChangesAsync();
            }

            var newConfig = new DataConfigurationLocal
            {
                ServerConfigId = serverId,
                TonnageVariation = JsonConvert.SerializeObject(datos.TonnageVariation),
                Time = JsonConvert.SerializeObject(datos.Time),
                SelectedRoutes = JsonConvert.SerializeObject(datos.SelectedRoutes),
                SelectedEmployees = JsonConvert.SerializeObject(datos.SelectedEmployees),
                SelectedVehicles = JsonConvert.SerializeObject(datos.SelectedVehicles)
            };

            await _context.DataConfigurationLocal.AddAsync(newConfig);
            await _context.SaveChangesAsync();
        }

        public decimal GetRandomTonnageWeight(List<int> tonnageVariation, decimal capacityVehicle)
        {
            if (tonnageVariation == null || tonnageVariation.Count != 2)
                throw new InvalidOperationException("La variación de tonelaje no está configurada correctamente.");
                
            Random random = new Random();
            decimal minPercent = tonnageVariation[0];
            decimal maxPercent = tonnageVariation[1];
            
            decimal randomFactor = (decimal)random.NextDouble();
            decimal percentRange = maxPercent - minPercent;
            decimal randomPercent = minPercent + (randomFactor * percentRange);

            decimal weight = capacityVehicle * (randomPercent / 100m);
            return Math.Round(weight, 2);
        }

        public int GetRandomElement(List<int> list)
        {
            if (list == null || !list.Any())
                throw new InvalidOperationException("La lista está vacía.");
                
            Random random = new Random();
            return list[random.Next(list.Count)];
        }
    }
}
