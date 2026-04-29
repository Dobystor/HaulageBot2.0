const VERSION = "2.1.0-FIX";
let currentBotId = null;
let isRunning = false;
let configLoadedForBot = null; 

const formatTime = (isoString) => {
    const d = new Date(isoString);
    return d.toTimeString().split(' ')[0];
};

document.addEventListener('DOMContentLoaded', () => {
    const logo = document.querySelector('.brand-logo span');
    if (logo) logo.innerText += ` [v${VERSION}]`;
});

const fetchBots = async () => {
    try {
        const res = await fetch('/api/bots');
        const bots = await res.json();
        
        const tabsContainer = document.getElementById('serverTabs');
        tabsContainer.innerHTML = '';
        
        if (bots.length === 0) return;

        if (!currentBotId || !bots.find(b => b.id === currentBotId)) {
            currentBotId = bots[0].id;
        }

        bots.forEach(b => {
            const btn = document.createElement('button');
            btn.className = `node-tab ${b.id === currentBotId ? 'active' : ''}`;
            btn.innerText = b.name.toUpperCase();
            btn.onclick = () => selectBot(b.id, b.name);
            tabsContainer.appendChild(btn);
        });

        const currentBot = bots.find(b => b.id === currentBotId);
        if (currentBot) {
            const titleInput = document.getElementById('currentServerName');
            // Only update title if not focused AND the text actually changed
            if (document.activeElement !== titleInput && titleInput.value !== currentBot.name.toUpperCase()) {
                titleInput.value = currentBot.name.toUpperCase();
            }
            document.getElementById('deleteBotBtn').style.display = (currentBot.id === 'default') ? 'none' : 'block';
        }

    } catch (e) { console.error('Error fetching bots', e); }
};

const selectBot = (id, name) => {
    currentBotId = id;
    configLoadedForBot = null; 
    document.getElementById('consoleLogs').innerHTML = '';
    fetchBots();
    fetchStatus();
    updateLogs();
};

const addServer = async () => {
    const name = prompt('Nombre del nuevo nodo:');
    if (!name) return;
    try {
        const res = await fetch('/api/bots', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name })
        });
        const data = await res.json();
        if (data.success) selectBot(data.id, name);
    } catch (e) { alert('Error creando servidor'); }
};

const renameCurrentBot = async (el) => {
    const newName = el.value.trim();
    if (!newName || !currentBotId) return;
    try {
        await fetch(`/api/bots/${currentBotId}/name`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: newName })
        });
        fetchBots();
    } catch (e) { console.error('Error renombrando', e); }
};

const deleteCurrentBot = async () => {
    if (!currentBotId || currentBotId === 'default') return;
    if (!confirm(`¿Eliminar nodo "${document.getElementById('currentServerName').value}"?`)) return;
    try {
        const res = await fetch(`/api/bots/${currentBotId}`, { method: 'DELETE' });
        const data = await res.json();
        if (data.success) {
            currentBotId = 'default';
            fetchBots();
            fetchStatus();
        }
    } catch (e) { alert('Error al borrar'); }
};

const selectAll = (id) => {
    const select = document.getElementById(id);
    const options = select.options;
    const allSelected = Array.from(options).every(o => o.selected);
    for (let i = 0; i < options.length; i++) {
        options[i].selected = !allSelected;
    }
};

const updateLogs = async () => {
    if (!currentBotId) return;
    try {
        const response = await fetch(`/api/bots/${currentBotId}/logs`);
        const logs = await response.json();
        const consoleEl = document.getElementById('consoleLogs');
        
        const isAtBottom = consoleEl.scrollHeight - consoleEl.scrollTop <= consoleEl.clientHeight + 50;

        consoleEl.innerHTML = '';
        logs.forEach(log => {
            const line = document.createElement('div');
            line.className = 'log-line';
            const prefix = log.type === 'success' ? 'TX' : (log.type === 'error' ? 'ERR' : 'SYS');
            line.innerHTML = `
                <span class="log-time">[${formatTime(log.timestamp)}]</span>
                <span class="log-tx">${prefix}</span>
                <span class="log-${log.type}">${log.message}</span>
            `;
            consoleEl.appendChild(line);
        });

        if (isAtBottom) {
            consoleEl.scrollTop = consoleEl.scrollHeight;
        }
    } catch (e) { console.error('Error fetching logs', e); }
};

const populateSelect = (selectId, optionsList, selectedValues) => {
    const select = document.getElementById(selectId);
    if (select.options.length !== optionsList.length) {
        select.innerHTML = '';
        optionsList.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.id;
            option.textContent = opt.label;
            if (selectedValues.includes(opt.id.toString())) option.selected = true;
            select.appendChild(option);
        });
    }
};

const fetchStatus = async () => {
    if (!currentBotId) return;
    try {
        const response = await fetch(`/api/bots/${currentBotId}/status`);
        const status = await response.json();
        
        if (configLoadedForBot !== currentBotId) {
            if (document.getElementById('serverIp')) document.getElementById('serverIp').value = status.config.serverIp || '';
            if (document.getElementById('minWeight')) document.getElementById('minWeight').value = status.config.minWeight || 20;
            if (document.getElementById('maxWeight')) document.getElementById('maxWeight').value = status.config.maxWeight || 40;
            if (document.getElementById('minInterval')) document.getElementById('minInterval').value = status.config.minInterval || 3;
            if (document.getElementById('maxInterval')) document.getElementById('maxInterval').value = status.config.maxInterval || 8;
            
            const resOpt = await fetch(`/api/bots/${currentBotId}/options`);
            const opts = await resOpt.json();
            populateSelect('selectVehicles', opts.vehicles, status.config.selectedVehicles || []);
            populateSelect('selectEmployees', opts.employees, status.config.selectedEmployees || []);
            populateSelect('selectRoutes', opts.routes, status.config.selectedRoutes || []);
            
            configLoadedForBot = currentBotId;
        }

        updateStatusUI(status);
    } catch (e) { console.error('Error fetching status', e); }
};

const updateStatusUI = (status) => {
    isRunning = status.isRunning;
    const badge = document.getElementById('statusBadge');
    
    if (isRunning) {
        badge.textContent = 'AUTO_ON';
        badge.style.color = 'var(--success)';
    } else {
        badge.textContent = 'OFFLINE';
        badge.style.color = 'var(--text-secondary)';
    }

    const toggleBtn = document.getElementById('toggleBtn');
    if (isRunning) {
        toggleBtn.innerText = 'DETENER AUTO';
        toggleBtn.style.background = 'var(--success)';
        toggleBtn.style.color = 'black';
    } else {
        toggleBtn.innerText = 'INICIAR AUTO';
        toggleBtn.style.background = 'transparent';
        toggleBtn.style.color = 'var(--accent)';
    }

    document.getElementById('comboCount').innerText = status.combinationsCount || 0;
};

async function saveConfig() {
    if (!currentBotId) return;

    const config = {
        serverIp: document.getElementById('serverIp')?.value || '',
        minWeight: parseFloat(document.getElementById('minWeight')?.value || 20),
        maxWeight: parseFloat(document.getElementById('maxWeight')?.value || 40),
        minInterval: parseFloat(document.getElementById('minInterval')?.value || 3),
        maxInterval: parseFloat(document.getElementById('maxInterval')?.value || 8),
        selectedVehicles: Array.from(document.getElementById('selectVehicles')?.selectedOptions || []).map(o => o.value),
        selectedEmployees: Array.from(document.getElementById('selectEmployees')?.selectedOptions || []).map(o => o.value),
        selectedRoutes: Array.from(document.getElementById('selectRoutes')?.selectedOptions || []).map(o => o.value)
    };

    try {
        const res = await fetch(`/api/bots/${currentBotId}/config`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(config)
        });
        const data = await res.json();
        if (data.success) {
            // showToast replaced with alert for compatibility
            alert('Configuración guardada.');
            configLoadedForBot = null; 
            fetchStatus();
        }
    } catch (e) {
        alert('Error al guardar configuración');
    }
}

const toggleBot = async () => {
    if (!currentBotId) return;
    const endpoint = isRunning ? `/api/bots/${currentBotId}/stop` : `/api/bots/${currentBotId}/start`;
    try {
        const res = await fetch(endpoint, { method: 'POST' });
        const data = await res.json();
        if (!data.success) alert(data.message);
        fetchBots();
        fetchStatus();
    } catch (e) { console.error(e); }
};

const triggerManual = async () => {
    if (!currentBotId) return;
    try {
        const res = await fetch(`/api/bots/${currentBotId}/trigger`, { method: 'POST' });
        const data = await res.json();
        if (data.success) {
             updateLogs();
        } else {
            alert(data.message);
        }
    } catch (e) { console.error(e); }
};

const uploadFile = async () => {
    if (!currentBotId) return;
    const fileInput = document.getElementById('fileUpload');
    if (!fileInput.files || fileInput.files.length === 0) return;
    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    try {
        const res = await fetch(`/api/bots/${currentBotId}/import`, { method: 'POST', body: formData });
        const data = await res.json();
        if (!data.success) alert(data.message);
    } catch (e) { alert('Error subiendo archivo'); }
    fileInput.value = '';
};

// Initial calls
fetchBots().then(() => { fetchStatus(); updateLogs(); });
setInterval(() => { fetchBots(); fetchStatus(); }, 3000);
setInterval(updateLogs, 1500);
