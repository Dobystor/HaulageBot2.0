# Guía: Subir HaulageBot2.0 al repo de GitLab en una nueva rama (con Docker)

Esta guía te lleva paso a paso para meter el proyecto **HaulageBot2.0** (el que desarrollamos)
en el repositorio de GitLab `https://developer.lasec.com.mx/SmartFlow/services/haulages_bot`,
en una **rama nueva**, manteniéndolo funcional con **Docker** (`docker-compose.yml` + `.env`).

No necesitas saber git avanzado: solo copia y pega los comandos en orden.

---

## Resumen de lo que vamos a hacer

1. Clonar el repo de GitLab en una carpeta nueva.
2. Crear una rama nueva dentro de ese repo.
3. Copiar los archivos del proyecto HaulageBot2.0 dentro del repo clonado.
4. Copiar los archivos de Docker (`Dockerfile`, `docker-compose.yml`, `.env.example`).
5. Hacer commit y push de la rama nueva a GitLab.
6. Levantar todo con Docker en el servidor.

> **Importante:** El archivo `.env` (con secretos) **no se sube** al repo. Solo subimos
> `.env.example` como plantilla. En el servidor se copia `.env.example` a `.env` y se ajusta.

---

## Paso 1 — Clonar el repo de GitLab

Abre una terminal (PowerShell) y colócate en una carpeta donde quieras trabajar, por ejemplo `C:\`:

```powershell
cd C:\
git clone https://developer.lasec.com.mx/SmartFlow/services/haulages_bot.git haulage_bot_gitlab
cd haulage_bot_gitlab
```

Si te pide usuario y contraseña, usa tus credenciales de GitLab de LASEC.

Verifica que estás en el repo correcto:

```powershell
git remote -v
```

Debe mostrar la URL de `developer.lasec.com.mx`.

---

## Paso 2 — Crear la rama nueva

Elige un nombre descriptivo, por ejemplo `feature/haulage-bot-2.0`:

```powershell
git checkout -b feature/haulage-bot-2.0
```

Con esto ya estás parado en tu rama nueva (aún local).

---

## Paso 3 — Copiar los archivos del proyecto HaulageBot2.0

El proyecto nuevo está en `C:\haulages_bot2.0\haulages_bot`.
Vamos a copiar **todo el código** dentro del repo clonado (en la subcarpeta `haulages_bot`).

```powershell
# Borra el contenido viejo de la carpeta haulages_bot del repo clonado (si existe)
Remove-Item -Recurse -Force C:\haulage_bot_gitlab\haulages_bot -ErrorAction SilentlyContinue

# Copia el proyecto nuevo completo
Copy-Item -Recurse C:\haulages_bot2.0\haulages_bot C:\haulage_bot_gitlab\haulages_bot

# Limpia carpetas de build que no deben subirse
Remove-Item -Recurse -Force C:\haulage_bot_gitlab\haulages_bot\bin -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force C:\haulage_bot_gitlab\haulages_bot\obj -ErrorAction SilentlyContinue
Remove-Item -Force C:\haulage_bot_gitlab\haulages_bot\*.db -ErrorAction SilentlyContinue
```

---

## Paso 4 — Copiar los archivos de Docker a la raíz del repo

```powershell
Copy-Item C:\haulages_bot2.0\docker-compose.yml   C:\haulage_bot_gitlab\docker-compose.yml
Copy-Item C:\haulages_bot2.0\.env.example         C:\haulage_bot_gitlab\.env.example
```

El `Dockerfile` ya va incluido dentro de la carpeta `haulages_bot` que copiaste en el Paso 3.

Asegúrate de que el `.gitignore` del repo NO ignore `docker-compose.yml`. Si lo hace, quita esa línea.

---

## Paso 5 — Commit y push a GitLab

```powershell
cd C:\haulage_bot_gitlab
git add .
git commit -m "Migra HaulageBot2.0 con soporte Docker (docker-compose + Dockerfile)"
git push -u origin feature/haulage-bot-2.0
```

Listo. La rama ya está en GitLab. Puedes abrirla en el navegador:
`https://developer.lasec.com.mx/SmartFlow/services/haulages_bot/-/branches`

---

## Paso 6 — Levantar con Docker en el servidor

En el servidor (Linux), dentro de la carpeta del repo:

```bash
# 1. Copiar la plantilla de variables y ajustarla
cp .env.example .env
nano .env        # ajusta DB_SERVER, DB_NAME, DB_USER, DB_PASSWORD, HOST_PORT, etc.

# 2. Construir y levantar el contenedor
docker compose up -d --build

# 3. Ver los logs
docker compose logs -f haulages_bot

# 4. Detener
docker compose down
```

El bot se conecta al **SQL Server** existente en el servidor (variables `DB_*` del `.env`)
y crea/usa su propia base de datos `haulages_bot`. Las llaves de cifrado (Data Protection)
se guardan en el volumen `haulage_bot_keys` para que no se regeneren en cada reinicio.

> El healthcheck de Docker consulta `http://localhost:80/health`. Puedes verificar el estado con
> `docker compose ps` (columna STATUS mostrará `healthy`).

---

## Comandos Docker útiles

| Acción | Comando |
|--------|---------|
| Reconstruir tras cambios de código | `docker compose up -d --build` |
| Ver logs en vivo | `docker compose logs -f haulages_bot` |
| Reiniciar | `docker compose restart haulages_bot` |
| Detener y borrar contenedor | `docker compose down` |
| Ver estado | `docker compose ps` |
| Entrar al contenedor | `docker compose exec haulages_bot bash` |

---

## Notas

- El puerto por defecto es **5101** (igual que el docker-compose del bot antiguo). Cámbialo en `.env` con `HOST_PORT`.
- La base de datos es **SQL Server** (la instancia que ya existe en el servidor). Se configura con `DB_SERVER`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` en el `.env`.
- La configuración de SmartFlow (`API_URL`, `CLIENT_ID`, `CLIENT_SECRET`) se ajusta en `.env`.
- Las migraciones de Entity Framework se aplican solas al arrancar (crean las tablas si no existen).
- El `Dockerfile` compila la app dentro del contenedor, así que no necesitas tener .NET instalado en el servidor, solo Docker.
