const express = require('express');
const axios = require('axios');
const path = require('path');
const https = require('https');
const xlsx = require('xlsx');
const multer = require('multer');
const fs = require('fs');
const crypto = require('crypto');

const app = express();
const PORT = 3005;
const upload = multer({ dest: 'uploads/' });

// Ignore SSL certificates for industrial local IPs
const agent = new https.Agent({ rejectUnauthorized: false });

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

// Global Error Handling
process.on('uncaughtException', (err) => console.error('[FATAL]', err));
process.on('unhandledRejection', (reason) => console.error('[REJECTION]', reason));

class BotInstance {
    constructor(id, name) {
        this.id = id;
        this.name = name;
        this.config = {
            serverIp: 'dispatch-01-sf.smartflow.com.mx',
            token: '',
            user: 'root',
            pass: 'St4rtTheChange.',
            clientId: 'private.networking.app',
            clientSecret: 'UxwYJsELeTnSc2Zz642K',
            scope: 'smartflow IdentityServerApi offline_access',
            minWeight: 20,
            maxWeight: 40,
            minInterval: 3,
            maxInterval: 8,
            selectedVehicles: [],
            selectedEmployees: [],
            selectedRoutes: []
        };
        this.tokenExpiry = 0;
        this.isRunning = false;
        this.timeoutId = null;
        this.validCombinations = [];
        this.options = { vehicles: [], employees: [], routes: [] };
        this.logs = [];
        this.MAX_LOGS = 100;
        this.addLog('info', `Nodo "${this.name}" inicializado.`);
    }

    addLog(type, message) {
        const timestamp = new Date().toISOString();
        this.logs.unshift({ timestamp, type, message });
        if (this.logs.length > this.MAX_LOGS) this.logs.pop();
        console.log(`[${this.name}] [${type}] ${message}`);
    }

    async ensureValidToken() {
        if (this.config.user && this.config.pass && this.config.clientId) {
            const buffer = 60000; // 1 min buffer
            if (!this.config.token || Date.now() > this.tokenExpiry - buffer) {
                await this.getToken();
            }
        }
        return this.config.token;
    }

    async getToken() {
        try {
            const host = this.config.serverIp.startsWith('http') ? this.config.serverIp : `https://${this.config.serverIp}`;
            const authUrl = `${host}/api/openid/connect/token`;
            
            this.addLog('info', '🔄 Solicitando nuevo token de acceso...');
            
            const params = new URLSearchParams();
            params.append('grant_type', 'password');
            params.append('username', this.config.user);
            params.append('password', this.config.pass);
            params.append('scope', this.config.scope);
            params.append('client_id', this.config.clientId);
            params.append('client_secret', this.config.clientSecret);

            const response = await axios.post(authUrl, params, {
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                httpsAgent: agent,
                timeout: 10000
            });

            this.config.token = response.data.access_token;
            this.tokenExpiry = Date.now() + (response.data.expires_in * 1000);
            this.addLog('success', '✅ Token obtenido correctamente.');
            return true;
        } catch (e) {
            const errorMsg = e.response?.data?.error_description || e.message;
            this.addLog('error', `❌ Error de autenticación: ${errorMsg}`);
            return false;
        }
    }

    extractOptions() {
        const vSet = new Map();
        const eSet = new Map();
        const rSet = new Map();

        if (Array.isArray(this.validCombinations) && this.validCombinations.length > 0) {
            const first = this.validCombinations[0];
            const keys = Object.keys(first).slice(0, 15).join(', ');
            this.addLog('info', `DEBUG: Campos detectados: ${keys}`);

            this.validCombinations.forEach(c => {
                if (!c) return;
                const vid = c.vehicleId ?? c.VehicleId ?? c.vehicle ?? c.Vehicle ?? c.id_vehiculo ?? c.id;
                const eid = c.employeeId ?? c.EmployeeId ?? c.employee ?? c.Employee ?? c.id_empleado ?? c.id_operador;
                const rid = c.pathId ?? c.PathId ?? c.haulagePathName ?? c.pathDescription ?? c.id_ruta ?? c.PathName;

                if (vid !== null && vid !== undefined) {
                    const vdesc = c.vehicleDescription ?? c.VehicleDescription ?? c.vehicle ?? c.Vehicle ?? vid;
                    vSet.set(vid.toString(), vdesc);
                }
                if (eid !== null && eid !== undefined) {
                    const edesc = c.employeeName ?? c.EmployeeName ?? c.employee ?? c.Employee ?? eid;
                    eSet.set(eid.toString(), edesc);
                }
                if (rid !== null && rid !== undefined) {
                    const rdesc = c.pathDescription ?? c.PathDescription ?? c.haulagePathName ?? c.PathName ?? rid;
                    rSet.set(rid.toString(), rdesc);
                }
            });
        }

        this.options.vehicles = Array.from(vSet, ([id, label]) => ({ id, label }));
        this.options.employees = Array.from(eSet, ([id, label]) => ({ id, label }));
        this.options.routes = Array.from(rSet, ([id, label]) => ({ id, label }));
    }

    async fetchCombinations() {
        if (!this.config.serverIp) return false;
        await this.ensureValidToken();
        if (!this.config.token) return false;

        const host = this.config.serverIp.startsWith('http') ? this.config.serverIp : `https://${this.config.serverIp}`;
        const apiUrl = `${host}/service/haulages/api/v2/haulagepaths/semimanual/all`;

        try {
            this.addLog('info', `Consultando combinaciones en: ${apiUrl}`);
            const response = await axios.get(apiUrl, {
                headers: { 'Authorization': `Bearer ${this.config.token}` },
                httpsAgent: agent,
                timeout: 15000
            });
            
            const data = response.data.data || response.data;
            if (Array.isArray(data)) {
                this.validCombinations = data;
                this.extractOptions();
                this.addLog('success', `Se cargaron ${this.validCombinations.length} combinaciones.`);
                return true;
            }
            return false;
        } catch (error) {
            this.addLog('error', `Fallo al obtener combinaciones: ${error.message}`);
            return false;
        }
    }

    getFilteredCombinations() {
        return this.validCombinations.filter(c => {
            const vRaw = c.vehicleId ?? c.VehicleId ?? c.vehicle ?? c.Vehicle ?? c.id_vehiculo ?? c.id;
            const eRaw = c.employeeId ?? c.EmployeeId ?? c.employee ?? c.Employee ?? c.id_empleado ?? c.id_operador;
            const rRaw = c.pathId ?? c.PathId ?? c.haulagePathName ?? c.pathDescription ?? c.id_ruta ?? c.PathName;

            const vid = vRaw !== null && vRaw !== undefined ? vRaw.toString() : null;
            const eid = eRaw !== null && eRaw !== undefined ? eRaw.toString() : null;
            const rid = rRaw !== null && rRaw !== undefined ? rRaw.toString() : null;

            const vMatch = this.config.selectedVehicles.length === 0 || (vid && this.config.selectedVehicles.includes(vid));
            const eMatch = this.config.selectedEmployees.length === 0 || (eid && this.config.selectedEmployees.includes(eid));
            const rMatch = this.config.selectedRoutes.length === 0 || (rid && this.config.selectedRoutes.includes(rid));

            return vMatch && eMatch && rMatch;
        });
    }

    async doRegistration(specificPayload = null) {
        await this.ensureValidToken();
        if (!this.config.token) return;

        const host = this.config.serverIp.startsWith('http') ? this.config.serverIp : `https://${this.config.serverIp}`;
        const apiUrl = `${host}/service/haulages/api/v2/haulages/semimanual`;

        let payload = specificPayload;
        if (!payload) {
            const filtered = this.getFilteredCombinations();
            if (filtered.length === 0) {
                this.addLog('warning', 'Sin combinaciones para los filtros actuales.');
                return;
            }
            const combo = filtered[Math.floor(Math.random() * filtered.length)];
            const weight = (Math.random() * (this.config.maxWeight - this.config.minWeight) + parseFloat(this.config.minWeight)).toFixed(2);
            
            payload = {
                VehicleId: combo.vehicleId ?? combo.VehicleId ?? combo.id_vehiculo ?? combo.vehicle ?? combo.Vehicle ?? combo.id,
                EmployeeId: combo.employeeId ?? combo.EmployeeId ?? combo.employee ?? combo.Employee ?? combo.id_empleado ?? combo.id_operador,
                PathId: combo.pathId ?? combo.PathId ?? combo.haulagePathName ?? combo.pathDescription ?? combo.id_ruta ?? combo.PathName,
                MaterialTypeId: combo.materialTypeId ?? combo.MaterialTypeId ?? 1,
                Weight: parseFloat(weight),
                Comments: "Auto-Registro SF Bot",
                Date: new Date().toISOString()
            };
        }

        try {
            const response = await axios.post(apiUrl, payload, {
                headers: { 'Authorization': `Bearer ${this.config.token}` },
                httpsAgent: agent
            });
            this.addLog('success', `Registro exitoso: ${payload.Weight}t | ID: ${response.data.id || 'OK'}`);
        } catch (error) {
            this.addLog('error', `Fallo al registrar: ${error.message}`);
        }
    }

    startAuto() {
        if (this.isRunning) return;
        this.isRunning = true;
        this.addLog('info', 'MODO AUTOMÁTICO ACTIVADO');
        this.runIteration();
    }

    stopAuto() {
        this.isRunning = false;
        if (this.timeoutId) clearTimeout(this.timeoutId);
        this.addLog('info', 'MODO AUTOMÁTICO DESACTIVADO');
    }

    async runIteration() {
        if (!this.isRunning) return;
        await this.doRegistration();
        const nextMin = parseFloat(this.config.minInterval) * 60 * 1000;
        const nextMax = parseFloat(this.config.maxInterval) * 60 * 1000;
        const delay = Math.floor(Math.random() * (nextMax - nextMin + 1) + nextMin);
        this.addLog('info', `Siguiente ciclo en ${(delay/60000).toFixed(1)} min.`);
        this.timeoutId = setTimeout(() => this.runIteration(), delay);
    }
}

const bots = new Map();
const defaultBot = new BotInstance('main', 'Planta Principal');
bots.set('main', defaultBot);

app.get('/api/bots', (req, res) => {
    const list = Array.from(bots.values()).map(b => ({
        id: b.id,
        name: b.name,
        config: b.config,
        status: b.isRunning ? 'AUTO_ON' : 'OFFLINE',
        combinationsCount: b.validCombinations.length,
        options: b.options
    }));
    res.json(list);
});

app.post('/api/bots', (req, res) => {
    const id = crypto.randomUUID();
    const name = req.body.name || `Nuevo Nodo ${bots.size + 1}`;
    const bot = new BotInstance(id, name);
    bots.set(id, bot);
    res.json({ id, name });
});

app.delete('/api/bots/:id', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) {
        bot.stopAuto();
        bots.delete(req.params.id);
        res.json({ success: true });
    } else res.status(404).send();
});

app.post('/api/bots/:id/config', async (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) {
        const oldName = bot.name;
        if (req.body.name) bot.name = req.body.name;
        bot.config = { ...bot.config, ...req.body };
        bot.addLog('info', 'Parámetros actualizados.');
        await bot.fetchCombinations();
        res.json({ success: true });
    } else res.status(404).send();
});

app.get('/api/bots/:id/logs', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) res.json(bot.logs);
    else res.status(404).send();
});

app.post('/api/bots/:id/start', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) { bot.startAuto(); res.json({ success: true }); }
    else res.status(404).send();
});

app.post('/api/bots/:id/stop', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) { bot.stopAuto(); res.json({ success: true }); }
    else res.status(404).send();
});

app.get('/api/bots/:id/status', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) {
        res.json({
            id: bot.id,
            name: bot.name,
            config: bot.config,
            isRunning: bot.isRunning,
            combinationsCount: bot.validCombinations.length
        });
    } else res.status(404).send();
});

app.get('/api/bots/:id/options', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) res.json(bot.options);
    else res.status(404).send();
});

app.patch('/api/bots/:id/name', (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot && req.body.name) {
        bot.name = req.body.name;
        res.json({ success: true });
    } else res.status(404).send();
});

app.post('/api/bots/:id/trigger', async (req, res) => {
    const bot = bots.get(req.params.id);
    if (bot) {
        if (bot.validCombinations.length === 0) await bot.fetchCombinations();
        await bot.doRegistration();
        res.json({ success: true });
    } else res.status(404).send();
});

app.post('/api/bots/:id/import', upload.single('file'), async (req, res) => {
    const bot = bots.get(req.params.id);
    if (!bot || !req.file) {
        if (req.file) fs.unlinkSync(req.file.path);
        return res.status(400).json({ error: 'Nodo o archivo no encontrado' });
    }

    try {
        if (bot.validCombinations.length === 0) await bot.fetchCombinations();
        const workbook = xlsx.readFile(req.file.path);
        const rows = xlsx.utils.sheet_to_json(workbook.Sheets[workbook.SheetNames[0]], { raw: false });
        
        bot.addLog('info', `Importando ${rows.length} registros...`);
        res.json({ success: true, message: 'Procesando...' });

        const vMap = new Map(), eMap = new Map(), rMap = new Map();
        bot.validCombinations.forEach(c => {
            const v = (c.vehicleDescription ?? c.vehicle ?? "").toString().toLowerCase().trim();
            if (v) vMap.set(v, c.vehicleId ?? c.id_vehiculo ?? c.vehicle ?? c.id);
            const e = (c.employeeName ?? c.employee ?? "").toString().toLowerCase().trim();
            if (e) eMap.set(e, c.employeeId ?? c.id_empleado ?? c.employee);
            const r1 = (c.originDescription ?? "").toLowerCase().trim();
            const r2 = (c.destinationDescription ?? "").toLowerCase().trim();
            if (r1 && r2) rMap.set(`${r1}|${r2}`, c.pathId ?? c.id_ruta ?? c.haulagePathName);
            const rd = (c.pathDescription ?? c.haulagePathName ?? "").toLowerCase().trim();
            if (rd) rMap.set(rd, c.pathId ?? c.id_ruta ?? c.haulagePathName);
        });

        for (let i = 0; i < rows.length; i++) {
            const row = rows[i];
            const vS = (row.Vehiculo || "").toString().toLowerCase().trim();
            const eS = (row.Empleado || "").toString().toLowerCase().trim();
            const oS = (row.Sitio_Carga || "").toString().toLowerCase().trim();
            const dS = (row.Sitio_Descarga || "").toString().toLowerCase().trim();
            
            const vid = vMap.get(vS), eid = eMap.get(eS);
            let rid = rMap.get(`${oS}|${dS}`) || rMap.get((row.Ruta || "").toLowerCase().trim());

            if (vid === undefined || eid === undefined || rid === undefined) {
                bot.addLog('error', `Fila ${i+2} FALLÓ: Datos no encontrados en catálogo.`);
                continue;
            }

            await bot.doRegistration({
                VehicleId: vid, EmployeeId: eid, PathId: rid,
                MaterialTypeId: 1, Weight: parseFloat(row.Peso || 0),
                Date: new Date(row.Fecha || Date.now()).toISOString(),
                Comments: "Importación SF Bot"
            });
            await new Promise(r => setTimeout(r, 400));
        }
        bot.addLog('info', 'Importación finalizada.');
    } catch (e) { bot.addLog('error', `Error: ${e.message}`); }
    finally { fs.unlinkSync(req.file.path); }
});

app.get('/api/template', (req, res) => {
    const ws = xlsx.utils.json_to_sheet([{
        Vehiculo: "ECON-101", Empleado: "JUAN PEREZ", Sitio_Carga: "MINA", 
        Sitio_Descarga: "PLANTA", Peso: 22.5, Fecha: "2026-04-20 14:30:00"
    }]);
    const wb = xlsx.utils.book_new();
    xlsx.utils.book_append_sheet(wb, ws, "Acarreos");
    const tempPath = path.join(__dirname, 'uploads', `plantilla_${Date.now()}.xlsx`);
    if (!fs.existsSync('uploads')) fs.mkdirSync('uploads');
    xlsx.writeFile(wb, tempPath);
    res.setHeader('Cache-Control', 'no-cache');
    res.download(tempPath, 'Plantilla_V2.xlsx', () => fs.unlinkSync(tempPath));
});

app.listen(PORT, () => console.log(`Smartflow Bot running at http://localhost:${PORT}`));
