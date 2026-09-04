# ============================================================
# Dockerfile multi-stage para HaulageBot2.0
# Compila y publica la app dentro del contenedor (autocontenido).
# Se conecta a SQL Server externo (configurado por variables de entorno).
# ============================================================

# --- Etapa 1: build ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar el csproj y restaurar dependencias (cache de capas)
COPY haulages_bot.csproj ./
RUN dotnet restore haulages_bot.csproj

# Copiar el resto del código y publicar
COPY . ./
RUN dotnet publish haulages_bot.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- Etapa 2: runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl es necesario para el healthcheck y para que el RethinkBot ejecute peticiones ReQL
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Directorio para las llaves de Data Protection (persistido por volumen)
RUN mkdir -p /var/keys

EXPOSE 80

# La app escucha en el puerto 80 dentro del contenedor
ENV ASPNETCORE_URLS=http://+:80

# Healthcheck: la app expone /health
HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=15s \
  CMD curl -f http://localhost:80/health || exit 1

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "haulages_bot.dll"]
