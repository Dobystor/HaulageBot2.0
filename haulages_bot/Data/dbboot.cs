using Microsoft.EntityFrameworkCore;
using haulages_bot.Models;
using FluentValidation;
using FluentValidation.AspNetCore;

namespace haulages_bot.Data
{
    // DbContext para la aplicación
    public class dbboot : DbContext
    {
        // Constructor que recibe opciones de configuración para el DbContext
        public dbboot(DbContextOptions<dbboot> options) : base(options)
        {
        }

        // DbSet para cada entidad en el modelo de datos
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<ServerConfig> ServerConfigs { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<ProgrammingRecord> ProgrammingRecords { get; set; }
        public DbSet<TokenRegistry> TokenRegistries { get; set; }
        public DbSet<Haulage> Haulages { get; set; }
        public DbSet<haulages_bot.Models.Route> Routes { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Material> Materials { get; set; }
        public DbSet<DataConfigurationLocal> DataConfigurationLocal { get; set; }
        public DbSet<RethinkBotConfig> RethinkBotConfigs { get; set; }
        public DbSet<InventoryBotConfig> InventoryBotConfigs { get; set; }
        public DbSet<ProductionPlanBotConfig> ProductionPlanBotConfigs { get; set; }

        // Configuración del modelo de datos
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configura la eliminación en cascada para todas las claves foráneas como NoAction
            foreach (var forenkey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                forenkey.DeleteBehavior = DeleteBehavior.NoAction;
            }

            // Claves primarias compuestas para soporte multiserver
            modelBuilder.Entity<Vehicle>().HasKey(v => new { v.VehicleId, v.ServerConfigId });
            modelBuilder.Entity<Employee>().HasKey(e => new { e.EmployeeId, e.ServerConfigId });
            modelBuilder.Entity<haulages_bot.Models.Route>().HasKey(r => new { r.haulagePathId, r.ServerConfigId });
            modelBuilder.Entity<Company>().HasKey(c => new { c.CompanyId, c.ServerConfigId });
            modelBuilder.Entity<Material>().HasKey(m => new { m.materialTypeId, m.ServerConfigId });
            modelBuilder.Entity<Shift>().HasKey(s => new { s.WorkShiftId, s.ServerConfigId });
            modelBuilder.Entity<VehicleType>().HasKey(vt => new { vt.VehicleTypeId, vt.ServerConfigId });
            modelBuilder.Entity<ProgrammingRecord>().HasKey(pr => new { pr.ProgrammingRecordId, pr.ServerConfigId });

            // Configuración de la relación entre Vehicle y Company
            modelBuilder.Entity<Vehicle>()
                .HasOne(v => v.Company)
                .WithMany()
                .HasForeignKey(v => new { v.CompanyId, v.ServerConfigId });

            // Configuración de la relación entre Vehicle y VehicleType
            modelBuilder.Entity<Vehicle>()
                .HasOne(v => v.VehicleType)
                .WithMany()
                .HasForeignKey(v => new { v.VehicleTypeId, v.ServerConfigId });

            // Configuración de la relación entre Employee y Company
            modelBuilder.Entity<Employee>()
                .HasOne(e => e.Company)
                .WithMany()
                .HasForeignKey(e => new { e.CompanyId, e.ServerConfigId });

            // Precisión explícita para propiedades decimal (evita truncamiento silencioso en SQL Server)
            modelBuilder.Entity<Vehicle>().Property(v => v.EmptyWeight).HasPrecision(18, 4);
            modelBuilder.Entity<Vehicle>().Property(v => v.FuelTankCapacity).HasPrecision(18, 4);
            modelBuilder.Entity<Vehicle>().Property(v => v.Weight).HasPrecision(18, 4);
            modelBuilder.Entity<Vehicle>().Property(v => v.LoadingCapacity).HasPrecision(18, 4);
            modelBuilder.Entity<haulages_bot.Models.Route>().Property(r => r.distance).HasPrecision(18, 4);
            modelBuilder.Entity<haulages_bot.Models.Route>().Property(r => r.timeInHour).HasPrecision(18, 4);
            modelBuilder.Entity<Haulage>().Property(h => h.Weight).HasPrecision(18, 4);
            modelBuilder.Entity<Haulage>().Property(h => h.Kilometers).HasPrecision(18, 4);
            modelBuilder.Entity<Historic>().Property(h => h.Weight).HasPrecision(18, 4);
            modelBuilder.Entity<Employee>().Property(e => e.NoEmployee).HasPrecision(18, 0);
            modelBuilder.Entity<ProductionPlanBotConfig>().Property(p => p.LawMinGrTon).HasPrecision(18, 4);
            modelBuilder.Entity<ProductionPlanBotConfig>().Property(p => p.LawMaxGrTon).HasPrecision(18, 4);
            modelBuilder.Entity<ProductionPlanBotConfig>().Property(p => p.LawMinPercent).HasPrecision(18, 4);
            modelBuilder.Entity<ProductionPlanBotConfig>().Property(p => p.LawMaxPercent).HasPrecision(18, 4);
        }

        public class VehicleValidator : AbstractValidator<Vehicle>
        {
            public VehicleValidator()
            {
                RuleFor(vehicle => vehicle.VehicleId)
                    .NotNull()
                    .WithMessage("El ID del vehículo no puede ser nulo.");

                RuleFor(vehicle => vehicle.Plates)
                    .NotEmpty()
                    .WithMessage("Las placas no pueden estar vacías.");

                RuleFor(vehicle => vehicle.EconomicNumber)
                    .NotEmpty()
                    .WithMessage("El número económico no puede estar vacío.");

                RuleFor(vehicle => vehicle.CompanyId)
                    .NotNull()
                    .WithMessage("El ID de la compañía no puede ser nulo.");

                RuleFor(vehicle => vehicle.Model)
                    .NotEmpty()
                    .WithMessage("El modelo no puede estar vacío.");

                RuleFor(vehicle => vehicle.EmptyWeight)
                    .GreaterThan(0)
                    .WithMessage("El peso vacío debe ser mayor que 0.");

                RuleFor(vehicle => vehicle.FuelTankCapacity)
                    .GreaterThan(0)
                    .WithMessage("La capacidad del tanque de combustible debe ser mayor que 0.");

                RuleFor(vehicle => vehicle.Weight)
                    .GreaterThan(0)
                    .WithMessage("El peso debe ser mayor que 0.");

                RuleFor(vehicle => vehicle.VehicleTypeId)
                    .NotNull()
                    .WithMessage("El tipo de vehículo no puede ser nulo.");

                RuleFor(vehicle => vehicle.Description)
                    .NotEmpty()
                    .WithMessage("La descripción no puede estar vacía.");
            }

        }
        public class EmployeeValidator : AbstractValidator<Employee>
        {
            public EmployeeValidator()
            {
                RuleFor(employee => employee.EmployeeId)
                    .NotNull()
                    .WithMessage("El ID del empleado no puede ser nulo.");

                RuleFor(employee => employee.NoEmployee)
                    .GreaterThan(0)
                    .WithMessage("El número de empleado debe ser mayor que 0.");

                RuleFor(employee => employee.Name)
                    .NotEmpty()
                    .WithMessage("El nombre no puede estar vacío.");

                RuleFor(employee => employee.PaternalLastName)
                    .NotEmpty()
                    .WithMessage("El apellido paterno no puede estar vacío.");

                RuleFor(employee => employee.MaternalLastName)
                    .NotEmpty()
                    .WithMessage("El apellido materno no puede estar vacío.");

                RuleFor(employee => employee.FullName)
                    .NotEmpty()
                    .WithMessage("El nombre completo no puede estar vacío.");

                RuleFor(employee => employee.CompanyId)
                    .NotNull()
                    .WithMessage("El ID de la compañía no puede ser nulo.");
            }
        }
        public class HistoricValidator : AbstractValidator<Historic>
        {
            public HistoricValidator()
            {
                RuleFor(historic => historic.HistoricId)
                    .NotNull()
                    .WithMessage("El ID histórico no puede ser nulo.");

                RuleFor(historic => historic.TokenRegistryId)
                    .NotNull()
                    .WithMessage("El ID del registro de token no puede ser nulo.");

                //RuleFor(historic => historic.Vehicle)
                //    .NotEmpty()
                //    .WithMessage("El vehículo no puede estar vacío.");

                RuleFor(historic => historic.FullName)
                    .NotEmpty()
                    .WithMessage("El nombre completo no puede estar vacío.");

                //RuleFor(historic => historic.LoadPointName)
                //    .NotEmpty()
                //    .WithMessage("El nombre del punto de carga no puede estar vacío.");

                //RuleFor(historic => historic.UnloadPointName)
                //    .NotEmpty()
                //    .WithMessage("El nombre del punto de descarga no puede estar vacío.");

                RuleFor(historic => historic.Weight)
                    .GreaterThan(0)
                    .WithMessage("El peso debe ser mayor que 0.");

                //RuleFor(historic => historic.MaterialTypeId)
                //    .NotNull()
                //    .WithMessage("El ID del tipo de material no puede ser nulo.");

                //RuleFor(historic => historic.DateOfCarries)
                //    .NotEmpty()
                //    .WithMessage("La fecha de transporte no puede estar vacía.");

                RuleFor(historic => historic.WorkShiftId)
                    .NotNull()
                    .WithMessage("El ID del turno de trabajo no puede ser nulo.");

                //RuleFor(historic => historic.VehicleNavigationId)
                //    .NotNull()
                //    .WithMessage("El ID de navegación del vehículo no puede ser nulo.");

                //RuleFor(historic => historic.EmployeeId)
                //    .NotNull()
                //    .WithMessage("El ID del empleado no puede ser nulo.");

                //RuleFor(historic => historic.HaulagePathId)
                //    .NotNull()
                //    .WithMessage("El ID de la ruta de transporte no puede ser nulo.");
            }
        }
        public class ShiftValidator : AbstractValidator<Shift>
        {
            public ShiftValidator()
            {
                RuleFor(shift => shift.WorkShiftId)
                    .NotNull()
                    .WithMessage("El ID del turno de trabajo no puede ser nulo.");

                RuleFor(shift => shift.ShiftId)
                    .NotNull()
                    .WithMessage("El ID del turno no puede ser nulo.");

                RuleFor(shift => shift.Description)
                    .NotEmpty()
                    .WithMessage("La descripción no puede estar vacía.");

                RuleFor(shift => shift.Enabled)
                    .NotEmpty()
                    .WithMessage("El estado (habilitado) no puede estar vacío.");

                RuleFor(shift => shift.StartTime)
                    .NotNull()
                    .WithMessage("La hora de inicio no puede ser nula.");

                RuleFor(shift => shift.EndTime)
                    .NotNull()
                    .WithMessage("La hora de fin no puede ser nula.")
                    .GreaterThan(shift => shift.StartTime)
                    .WithMessage("La hora de fin debe ser mayor que la hora de inicio.");

                RuleFor(shift => shift.OperationTime)
                    .NotNull()
                    .WithMessage("El tiempo de operación no puede ser nulo.");

            }
        }

        public class ProgrammingRecordValidator : AbstractValidator<ProgrammingRecord>
        {
            public ProgrammingRecordValidator()
            {
                RuleFor(proRecord => proRecord.ProgrammingRecordId)
                    .NotNull()
                    .WithMessage("El ID del registro de programación no puede ser nulo.");

                RuleFor(proRecord => proRecord.HaulageId)
                    .NotNull()
                    .WithMessage("El ID de la carga no puede ser nulo.");

                RuleFor(proRecord => proRecord.EmployeeId)
                    .NotNull()
                    .WithMessage("El ID del empleado no puede ser nulo.");

                RuleFor(proRecord => proRecord.Dateofcarries)
                    .NotNull()
                    .WithMessage("La hora de transporte no puede ser nula.");
            }
        }

        public class HaulageValidator : AbstractValidator<Haulage>
        {
            public HaulageValidator()
            {
                RuleFor(haulage => haulage.HaulageId)
                    .NotNull()
                    .WithMessage("El ID de la carga no puede ser nulo.");

                RuleFor(haulage => haulage.VehicleId)
                    .NotNull()
                    .WithMessage("El ID del vehículo no puede ser nulo.");

                RuleFor(haulage => haulage.EmployeeId)
                    .NotNull()
                    .WithMessage("El ID del empleado no puede ser nulo.");

                RuleFor(haulage => haulage.PathId)
                    .NotNull()
                    .WithMessage("El ID de la ruta no puede ser nulo.");

                RuleFor(haulage => haulage.Weight)
                    .GreaterThan(0)
                    .WithMessage("El peso debe ser mayor que 0.");

                //RuleFor(haulage => haulage.DateOfCarries)
                //    .NotEmpty()
                //    .WithMessage("La fecha de transporte no puede estar vacía.");

                RuleFor(haulage => haulage.LoadPointId)
                    .NotNull()
                    .WithMessage("El ID del punto de carga no puede ser nulo.");

                RuleFor(haulage => haulage.UnloadPointId)
                    .NotNull()
                    .WithMessage("El ID del punto de descarga no puede ser nulo.");

                RuleFor(haulage => haulage.ShiftId)
                    .NotNull()
                    .WithMessage("El ID del turno no puede ser nulo.");

                RuleFor(haulage => haulage.LoadPointName)
                    .NotEmpty()
                    .WithMessage("El nombre del punto de carga no puede estar vacío.");

                RuleFor(haulage => haulage.UnloadPointName)
                    .NotEmpty()
                    .WithMessage("El nombre del punto de descarga no puede estar vacío.");

                RuleFor(haulage => haulage.LawType)
                    .NotEmpty()
                    .WithMessage("El tipo de ley no puede estar vacío.");

                RuleFor(haulage => haulage.Kilometers)
                    .GreaterThan(0)
                    .WithMessage("Los kilómetros deben ser mayores que 0.");

                RuleFor(haulage => haulage.MaterialType)
                    .NotEmpty()
                    .WithMessage("El tipo de material no puede estar vacío.");

                //RuleFor(haulage => haulage.HaulagePathId)
                //    .NotNull()
                //    .WithMessage("El ID de la ruta de carga no puede ser nulo.");

                //RuleFor(haulage => haulage.MaterialTypeId)
                //    .NotNull()
                //    .WithMessage("El ID del tipo de material no puede ser nulo.");
            }
        }
        public class RoutesValidator : AbstractValidator<haulages_bot.Models.Route>
        {
            public RoutesValidator()
            {
                RuleFor(route => route.haulagePathId)
                    .NotNull()
                    .WithMessage("El ID de la ruta de carga no puede ser nulo.");

                RuleFor(route => route.description)
                    .NotEmpty()
                    .WithMessage("La descripción no puede estar vacía.");

                RuleFor(route => route.distance)
                    .GreaterThan(0)
                    .WithMessage("La distancia debe ser mayor que 0.");

                RuleFor(route => route.isExtraction)
                    .NotNull()
                    .WithMessage("El estado de extracción no puede ser nulo.");

                RuleFor(route => route.isEnabled)
                    .NotNull()
                    .WithMessage("El estado de habilitación no puede ser nulo.");

                RuleFor(route => route.loadPointId)
                    .NotNull()
                    .WithMessage("El ID del punto de carga no puede ser nulo.");

                RuleFor(route => route.loadPointName)
                    .NotEmpty()
                    .WithMessage("El nombre del punto de carga no puede estar vacío.");

                RuleFor(route => route.timeInHour)
                    .NotNull()
                    .WithMessage("El tiempo en horas no puede ser nulo.");

                RuleFor(route => route.unLoadPointId)
                    .NotNull()
                    .WithMessage("El ID del punto de descarga no puede ser nulo.");

                RuleFor(route => route.unLoadPointName)
                    .NotEmpty()
                    .WithMessage("El nombre del punto de descarga no puede estar vacío.");
            }
        }

        public class CompanyValidator : AbstractValidator<Company>
        {
            public CompanyValidator()
            {
                RuleFor(company => company.CompanyId)
                    .NotNull()
                    .WithMessage("El ID de la compañía no puede ser nulo.");

                RuleFor(company => company.Name)
                    .NotEmpty()
                    .WithMessage("El nombre de la compañía no puede estar vacío.");
            }
        }
        public class MaterialValidator : AbstractValidator<Material>
        {
            public MaterialValidator()
            {
                RuleFor(material => material.materialTypeId)
            .NotNull()
            .WithMessage("El ID del tipo de material no puede ser nulo.");

                RuleFor(material => material.name)
                    .NotEmpty()
                    .WithMessage("El nombre del material no puede estar vacío.");
            }
        }
    }
}
