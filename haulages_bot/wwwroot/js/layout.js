document.addEventListener("DOMContentLoaded", function () {
    let _tzOffsetHours = 0; // Se carga del API al inicializar

    // Cargar offset de zona horaria al inicializar
    async function loadTimezoneOffset() {
        let savedOffset = localStorage.getItem('globalTimezoneOffset');
        if (savedOffset !== null) {
            _tzOffsetHours = parseInt(savedOffset, 10);
            if (isNaN(_tzOffsetHours)) _tzOffsetHours = 0;
            
            // Sincronizar el input en la barra lateral
            const input = document.getElementById('globalTimezoneOffsetInput');
            if (input) input.value = _tzOffsetHours;
            return;
        }

        const activeServerId = localStorage.getItem('activeServerId');
        try {
            const url = activeServerId
                ? `/api/ServerConfig/timezone?serverId=${activeServerId}`
                : `/api/ServerConfig/timezone`;
            const res = await fetch(url);
            if (res.ok) {
                const data = await res.json();
                _tzOffsetHours = data.offsetHours || 0;
                localStorage.setItem('globalTimezoneOffset', _tzOffsetHours);
                
                // Sincronizar el input en la barra lateral
                const input = document.getElementById('globalTimezoneOffsetInput');
                if (input) input.value = _tzOffsetHours;
            }
        } catch(e) {
            console.warn('No se pudo cargar el offset de timezone:', e);
        }
    }

    // Helper: aplica el offset al objeto Date (devuelve nuevo Date)
    function applyTimezoneOffset(date) {
        if (!date || isNaN(date.getTime())) return date;
        return new Date(date.getTime() + _tzOffsetHours * 60 * 60 * 1000);
    }
    // Exponer globalmente para que gene_v2.js pueda usarlo
    window.applyTimezoneOffset = applyTimezoneOffset;
    window.getTimezoneOffsetHours = () => _tzOffsetHours;

    // Escuchar cambios en el input de timezone
    const tzInput = document.getElementById('globalTimezoneOffsetInput');
    if (tzInput) {
        tzInput.addEventListener('change', function () {
            let val = parseInt(this.value, 10);
            if (isNaN(val)) val = 0;
            this.value = val;
            _tzOffsetHours = val;
            localStorage.setItem('globalTimezoneOffset', val);
            
            // Refrescar el timer
            fetchNextRunTime();
            
            // Notificar a otras vistas
            const event = new CustomEvent("GlobalTimezoneChanged", { detail: { offsetHours: val } });
            window.dispatchEvent(event);
        });
    }

    // Iniciar cargando timezone y luego la próxima ejecución
    loadTimezoneOffset().then(() => fetchNextRunTime());

    const loadingScreen = document.getElementById("loading-screen-refresh");
    const content = document.querySelector(".content");

    if (loadingScreen && content) {
        // Mostrar la pantalla de carga
        loadingScreen.style.display = "flex";
        content.style.display = "none";

        // Cuando la ventana se ha cargado completamente
        window.addEventListener("load", function () {
            loadingScreen.style.display = "none";
            content.style.display = "block";
        });

        // Si el usuario recarga la página, muestra la pantalla de carga
        window.addEventListener("beforeunload", function () {
            loadingScreen.style.display = "flex";
            content.style.display = "none";
        });
    }

    if ($("#loading-screen-refresh").length) {
        $("#loading-screen-refresh").fadeOut(500, function () {
            $(this).remove();
        });
    }

    // Función para actualizar el reloj en tiempo real
    function updateCurrentTime() {
        const timeEl = document.getElementById("currentDateTime");
        if (timeEl) {
            const now = new Date();
            timeEl.innerText = `${now.toLocaleDateString('es-ES', {
                weekday: 'long',
                year: 'numeric',
                month: 'long',
                day: 'numeric',
            })} - ${now.toLocaleTimeString()}`;
        }
    }
    setInterval(updateCurrentTime, 1000);

    let countdownInterval = null;

    // Obtener y mostrar la próxima ejecución
    async function fetchNextRunTime() {
        const activeServerId = localStorage.getItem('activeServerId');
        if (!activeServerId) {
            updateTimerUI(null, "Desactivado");
            return;
        }

        // Re-cargar offset por si cambió el servidor activo
        await loadTimezoneOffset();

        try {
            const response = await fetch(`/api/sync/nextRunTime?serverId=${activeServerId}`);
            if (response.ok) {
                const data = await response.json();
                const rawDate = new Date(data.nextRunTime);
                const nextRunTime = applyTimezoneOffset(rawDate);
                updateNextRunTimer(nextRunTime);
            } else {
                updateTimerUI(null, "Desactivado");
            }
        } catch (error) {
            console.error("Error al obtener el próximo tiempo de ejecución:", error);
            updateTimerUI(null, "Error");
        }
    }

    function updateTimerUI(nextRunTime, countdownText) {
        const navNextRunText = document.getElementById("navNextRunText");
        const navCountdownText = document.getElementById("navCountdownText");
        const nextRunTimeFixed = document.getElementById("nextRunTimeFixed");
        const nextRunTimer = document.getElementById("nextRunTimer");

        if (nextRunTime) {
            const formattedTime = nextRunTime.toLocaleTimeString('es-ES');
            const formattedDate = nextRunTime.toLocaleDateString('es-ES', {
                weekday: 'long',
                year: 'numeric',
                month: 'long',
                day: 'numeric',
            });

            if (navNextRunText) navNextRunText.innerText = formattedTime;
            if (nextRunTimeFixed) nextRunTimeFixed.innerText = `${formattedDate} - ${formattedTime}`;
            
            if (navCountdownText) {
                navCountdownText.className = "px-2.5 py-0.5 bg-[#36b0c9]/15 text-[#36b0c9] border border-[#36b0c9]/30 font-bold rounded-full text-[10px] uppercase";
                navCountdownText.innerText = countdownText;
                navCountdownText.style.color = "";
            }
            if (nextRunTimer) nextRunTimer.innerText = countdownText;
        } else {
            if (navNextRunText) navNextRunText.innerText = "--:--:--";
            if (nextRunTimeFixed) nextRunTimeFixed.innerText = "Sin ejecución programada.";
            
            if (navCountdownText) {
                navCountdownText.className = "px-2.5 py-0.5 bg-white/10 text-slate-400 border border-white/10 font-bold rounded-full text-[10px] uppercase";
                navCountdownText.innerText = countdownText;
                navCountdownText.style.color = "";
            }
            if (nextRunTimer) nextRunTimer.innerText = countdownText;
        }
    }

    // Función para la cuenta regresiva
    function updateNextRunTimer(nextRunTime) {
        if (countdownInterval) {
            clearInterval(countdownInterval);
        }

        const runCountdown = () => {
            const now = new Date();
            const timeRemaining = nextRunTime - now;

            if (timeRemaining <= 0) {
                clearInterval(countdownInterval);
                updateTimerUI(nextRunTime, "Ejecutando...");
                // Dar un pequeño delay y volver a consultar
                setTimeout(fetchNextRunTime, 3000);
            } else {
                const totalSeconds = Math.floor(timeRemaining / 1000);
                const minutes = Math.floor(totalSeconds / 60);
                const seconds = totalSeconds % 60;
                const countdownStr = `En ${minutes}m y ${seconds}s`;
                updateTimerUI(nextRunTime, countdownStr);
            }
        };

        runCountdown();
        countdownInterval = setInterval(runCountdown, 1000);
    }

    // Registrar globalmente para que gene.js pueda dispararlo al cambiar de servidor
    window.refreshLayoutTimer = fetchNextRunTime;

    // Función para mostrar una notificación y reproducir sonido
    window.showNotification = function (type, message) {
        const container = document.getElementById("notification-container");
        if (!container) return;

        const alert = document.createElement("div");
        alert.className = `alert alert-${type} alert-dismissible fade show`;
        alert.role = "alert";
        alert.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>`;

        container.appendChild(alert);

        // Reproducir sonido
        const sound = document.getElementById("notification-sound");
        if (sound) {
            sound.play().catch((error) => console.error("Error al reproducir sonido:", error));
        }

        // Remover después de 5 segundos
        setTimeout(() => {
            alert.remove();
        }, 5000);
    };

    // Conectar con SignalR para recibir notificaciones
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/notificationHub")
        .build();

    connection.on("ReceiveNotification", (payload) => {
        let msg = "";
        let isError = false;
        if (typeof payload === 'string') {
            msg = payload;
            isError = payload.toLowerCase().includes("error");
        } else if (payload && typeof payload === 'object') {
            msg = payload.message || payload.Message || JSON.stringify(payload);
            isError = payload.error || payload.Error || msg.toLowerCase().includes("error");
        }
        const type = isError ? "danger" : "success";
        showNotification(type, msg);
        
        // Despachar evento para que gene.js lo capture
        const event = new CustomEvent("ServerNotification", { detail: payload });
        window.dispatchEvent(event);
    });

    connection.on("UpdateTimerInfo", (data) => {
        const activeServerId = localStorage.getItem('activeServerId');
        const sId = data.serverId || data.ServerId;
        if (sId == activeServerId) {
            const nextRun = data.nextRunTime || data.NextRunTime;
            // Parsear HH:mm:ss o una cadena ISO
            let nextRunDate;
            if (nextRun.includes("T") || nextRun.includes("-")) {
                nextRunDate = new Date(nextRun);
            } else {
                const [hh, mm, ss] = nextRun.split(':').map(Number);
                nextRunDate = new Date();
                nextRunDate.setHours(hh, mm, ss, 0);
                if (nextRunDate < new Date()) {
                    nextRunDate.setDate(nextRunDate.getDate() + 1);
                }
            }
            // Aplicar offset de zona horaria
            updateNextRunTimer(applyTimezoneOffset(nextRunDate));
        }
    });

    connection.start().catch((err) => console.error(err));

    // Registrar globalmente para que gene.js pueda dispararlo al cambiar de servidor
    window.refreshLayoutTimer = () => loadTimezoneOffset().then(() => fetchNextRunTime());
});

// Función para mostrar/ocultar el sidebar
function toggleSidebar() {
    $("#mySidebar").toggleClass("active");
    $(".main-content").toggleClass("shift");
}