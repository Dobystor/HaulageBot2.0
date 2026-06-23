$(document).ready(function () {
    let activeServerId = null;
    let serverList = [];
    let haulageLimit = 100;
    
    // Configuración inicial de UI
    const addServerModal = new bootstrap.Modal(document.getElementById('addServerModal'));
    
    // Inicializar Flatpickr en los campos de fecha del generador masivo
    const fpConfig = {
        enableTime: true,
        dateFormat: "Z",            // Valor real enviado: ISO 8601 con timezone
        altInput: true,             // Mostrar formato amigable al usuario
        altFormat: "d/m/Y H:i",    // Formato visual: dd/mm/yyyy HH:mm
        altInputClass: "w-full cyber-input px-3 py-2 pl-10 rounded", // Forzar clases completas con padding-left pl-10
        time_24hr: true,
        locale: "es",
        disableMobile: true,
        minuteIncrement: 1
    };
    flatpickr("#bulkStartDate", { ...fpConfig });
    flatpickr("#bulkEndDate", { ...fpConfig });
    
    // Cargar Servidores en el Arranque
    loadServers();

    // Cargar servidores desde la API
    function loadServers(selectId = null) {
        showLoadingScreen("Cargando servidores...");
        $.getJSON('/api/ServerConfig', function (servers) {
            serverList = servers;
            renderServerTabs(servers);
            
            if (servers.length === 0) {
                $('#noServersWarning').removeClass('d-none');
                $('#dashboardWorkspace').addClass('d-none');
                hideLoadingScreen();
            } else {
                $('#noServersWarning').addClass('d-none');
                $('#dashboardWorkspace').removeClass('d-none');
                
                // Determinar qué servidor seleccionar
                let targetId = selectId;
                if (!targetId) {
                    const savedId = localStorage.getItem('activeServerId');
                    if (savedId && servers.some(s => s.id == savedId)) {
                        targetId = parseInt(savedId);
                    } else {
                        targetId = servers[0].id;
                    }
                }
                
                selectServer(targetId);
            }
        }).fail(function (err) {
            console.error("Error al cargar los servidores:", err);
            hideLoadingScreen();
        });
    }

    // Renderizar las pestañas de servidores
    function renderServerTabs(servers) {
        const tabsContainer = $('#serverTabs');
        // Mantener el botón de agregar
        tabsContainer.find('.server-tab:not(.add-tab)').remove();
        
        servers.forEach(server => {
            const statusClass = server.isBotRunning ? 'running' : (server.isActive ? 'online' : 'offline');
            const tabHtml = `
                <button class="server-tab" data-id="${server.id}" id="tab-server-${server.id}">
                    <span class="status-dot ${statusClass}"></span>
                    ${server.name}
                </button>
            `;
            tabsContainer.prepend(tabHtml);
        });

        // Evento click para las pestañas
        $('.server-tab:not(.add-tab)').click(function () {
            const serverId = $(this).data('id');
            selectServer(serverId);
        });
    }

    // Seleccionar y cargar datos de un servidor específico
    function selectServer(serverId) {
        activeServerId = serverId;
        localStorage.setItem('activeServerId', serverId);
        
        $('.server-tab').removeClass('active');
        $(`#tab-server-${serverId}`).addClass('active');

        const server = serverList.find(s => s.id === serverId);
        if (!server) return;

        // Actualizar datos locales en gene.cshtml
        $('#activeServerName').text(server.name);
        $('#activeServerUrl').text(server.apiUrl);

        // Actualizar datos de la barra de navegación del layout
        localStorage.setItem('activeServerName', server.name);
        if (window.parent && window.parent.updateHeaderServerName) {
            window.parent.updateHeaderServerName();
        } else if (window.updateHeaderServerName) {
            window.updateHeaderServerName();
        }

        // Obtener estado real y config del bot
        fetchServerStatusAndConfig(serverId);

        // Actualizar el temporizador en la barra de navegación del layout
        if (window.parent && window.parent.refreshLayoutTimer) {
            window.parent.refreshLayoutTimer();
        } else if (window.refreshLayoutTimer) {
            window.refreshLayoutTimer();
        }
    }

    // Obtener estado y configuración del bot
    function fetchServerStatusAndConfig(serverId) {
        showLoadingScreen("Cargando configuración del nodo...");
        
        // Limpiar inputs de búsqueda al cambiar de nodo
        $('#searchRoutes').val('');
        $('#searchEmployees').val('');
        $('#searchVehicles').val('');

        // 1. Obtener estatus del servidor (isBotRunning, isSyncEnabledLocal, tokenExpiry)
        $.getJSON(`/api/sync/status?serverId=${serverId}`, function (status) {
            $('#botSwitch').prop('checked', status.isBotRunning);
            $('#syncSwitch').prop('checked', status.isSyncEnabledLocal);
            
            const badge = $('#connectionStatusBadge');
            badge.text(status.isBotRunning ? 'Autónomo Activo' : 'Listo / Pausado');
            
            if (status.isBotRunning) {
                $(`#tab-server-${serverId} .status-dot`).removeClass('online offline').addClass('running');
            } else {
                $(`#tab-server-${serverId} .status-dot`).removeClass('running offline').addClass('online');
            }
            
            $('#connectionTimeDesc').text(status.tokenExpiry);
        }).fail(function() {
            $('#connectionStatusBadge').text('Error de Conexión');
            $(`#tab-server-${serverId} .status-dot`).removeClass('running online').addClass('offline');
        });

        // 2. Cargar parámetros del Bot (tonelaje, tiempos, y catálogos seleccionados)
        $.getJSON(`/api/ConfBoot/loadDataFromDb?serverId=${serverId}`, function (config) {
            // Rellenar Min / Max
            if (config.tonnageVariation && config.tonnageVariation.length >= 2) {
                $('#tonnageMin').val(config.tonnageVariation[0]);
                $('#tonnageMax').val(config.tonnageVariation[1]);
            }
            if (config.time && config.time.length >= 2) {
                $('#timeMin').val(config.time[0]);
                $('#timeMax').val(config.time[1]);
            }

            // Almacenar temporalmente los arrays de IDs seleccionados
            const selRoutes = config.selectedRoutes || [];
            const selEmployees = config.selectedEmployees || [];
            const selVehicles = config.selectedVehicles || [];

            // Cargar y renderizar catálogos locales para este servidor
            loadCatalogs(serverId, selRoutes, selEmployees, selVehicles);
            
            // Cargar Historial de Acarreos en la tabla
            loadHaulageHistory(serverId);
            
        }).fail(function() {
            hideLoadingScreen();
        });

        // 3. Cargar tonelaje estimado diario
        loadDailyTonnageEstimate(serverId);

        // 4. Cargar config del bot RethinkDB
        loadRethinkBotConfig(serverId);
    }

    // Cargar catálogos desde el backend y renderizar checkboxes
    function loadCatalogs(serverId, selRoutes, selEmployees, selVehicles) {
        // Cargar Rutas
        $.getJSON(`/api/Routes/GetRoutes?serverId=${serverId}`, function (routes) {
            renderRoutesList(routes, selRoutes);
        }).fail(() => $('#routesList').html('<span class="text-danger">Error al cargar rutas</span>'));

        // Cargar Operadores (Employees)
        $.getJSON(`/api/Employees/GetEmployees?serverId=${serverId}`, function (employees) {
            renderCheckboxList('employeesList', employees, 'employeeId', 'nombreCompleto', selEmployees);
        }).fail(() => $('#employeesList').html('<span class="text-danger">Error al cargar operadores</span>'));

        // Cargar Vehículos
        $.getJSON(`/api/Vehicles/GetVehicles?serverId=${serverId}`, function (vehicles) {
            renderCheckboxList('vehiclesList', vehicles, 'vehicleId', 'economicNumber', selVehicles);
        }).fail(() => $('#vehiclesList').html('<span class="text-danger">Error al cargar vehículos</span>'));
        
        // Ocultar pantalla de carga
        setTimeout(hideLoadingScreen, 600);
    }

    // Cargar estimación de tonelaje diario para el servidor activo
    function loadDailyTonnageEstimate(serverId) {
        $.getJSON(`/api/ConfBoot/estimatedDailyTonnage?serverId=${serverId}`, function(data) {
            if (data.estimatedTonnage > 0) {
                $('#dailyTonnageValue').text(data.estimatedTonnage.toLocaleString('es-MX'));
                $('#dailyTonnageDetail').text(data.detail);
            } else {
                $('#dailyTonnageValue').text('--');
                $('#dailyTonnageDetail').text(data.detail || 'Configura el bot para ver estimación');
            }
        }).fail(function() {
            $('#dailyTonnageValue').text('--');
            $('#dailyTonnageDetail').text('Error al calcular');
        });
    }

    function updateCheckboxCounter(containerId) {
        const total = $(`#${containerId} input[type="checkbox"]`).length;
        const selected = $(`#${containerId} input[type="checkbox"]:checked`).length;
        
        let counterId = '';
        if (containerId === 'routesList') counterId = 'routesCounter';
        else if (containerId === 'employeesList') counterId = 'employeesCounter';
        else if (containerId === 'vehiclesList') counterId = 'vehiclesCounter';
        
        if (counterId) {
            $(`#${counterId}`).text(`${selected} de ${total} seleccionados`);
        }
    }

    function getFontSizeClass(text) {
        const len = (text || '').length;
        if (len > 35) return 'fs-small';
        if (len > 22) return 'fs-medium';
        return 'fs-normal';
    }

    // Renderizar lista de checkboxes genérica
    function renderCheckboxList(containerId, data, idField, nameField, selectedList) {
        const container = $(`#${containerId}`);
        container.empty();

        if (data.length === 0) {
            container.html('<span class="text-secondary small">Sin datos. Sincroniza el catálogo.</span>');
            updateCheckboxCounter(containerId);
            return;
        }

        data.forEach(item => {
            const itemId = item[idField];
            const itemName = String(item[nameField] || '');
            const isChecked = selectedList.includes(itemId) ? 'checked' : '';
            const sizeClass = getFontSizeClass(itemName);

            const itemHtml = `
                <div class="checkbox-item" data-name="${itemName.toLowerCase()}">
                    <input type="checkbox" id="chk-${containerId}-${itemId}" value="${itemId}" ${isChecked}>
                    <label for="chk-${containerId}-${itemId}" class="${sizeClass}">${itemName} <span class="item-id">#${itemId}</span></label>
                </div>
            `;
            container.append(itemHtml);
        });

        // Inicializar contador
        updateCheckboxCounter(containerId);

        // Escuchar cambios para actualizar el contador
        container.off('change', 'input[type="checkbox"]').on('change', 'input[type="checkbox"]', function () {
            updateCheckboxCounter(containerId);
        });
    }

    // Cache global de rutas para filtrado
    let _allRoutes = [];
    let _activeMatFilter = 'all';


    // Renderizar lista de rutas con badge de tipo de material (una sola vez)
    function renderRoutesList(routes, selectedList) {
        // Solo mostrar rutas activas/habilitadas
        _allRoutes = (routes || []).filter(r => r.isEnabled !== false);
        _activeMatFilter = 'all';
        // Resetear botones de filtro al cargar
        $('#routeMaterialFilter .route-mat-btn').removeClass('active');
        $('#routeMaterialFilter .route-mat-btn[data-mat="all"]').addClass('active');
        
        const container = $('#routesList');
        container.empty();

        if (_allRoutes.length === 0) {
            container.html('<span class="text-secondary small">Sin datos. Sincroniza el catálogo.</span>');
            updateCheckboxCounter('routesList');
            return;
        }

        _allRoutes.forEach(route => {
            const routeId = route.haulagePathId;
            const routeName = route.description;
            const badge = getRouteBadge(route);
            const isChecked = selectedList.includes(routeId) ? 'checked' : '';
            const matCat = getRouteMatCategory(route);
            const sizeClass = getFontSizeClass(routeName);

            const itemHtml = `
                <div class="checkbox-item" data-name="${routeName.toLowerCase()}" data-mat="${matCat}">
                    <input type="checkbox" id="chk-routesList-${routeId}" value="${routeId}" ${isChecked}>
                    <label for="chk-routesList-${routeId}" class="${sizeClass}" style="display: block;">
                        <span style="display: block;">${routeName} <span class="item-id">#${routeId}</span></span>
                        <span style="display: block; margin-top: 2px;">${badge}</span>
                    </label>
                </div>
            `;
            container.append(itemHtml);
        });

        updateCheckboxCounter('routesList');

        container.off('change', 'input[type="checkbox"]').on('change', 'input[type="checkbox"]', function () {
            updateCheckboxCounter('routesList');
        });

        // Aplicar filtros iniciales
        applyRoutesFilter();
    }

    // Obtener badge visual para una ruta según selectedMaterialType e isExtraction
    function getRouteBadge(route) {
        const mat = route.selectedMaterialType || 0;
        const matName = (route.materialType || 'ESTÉRIL').toUpperCase();
        if (mat === 0) return '<span style="font-size:8px;padding:0.5px 3.5px;border-radius:3px;background:rgba(255,215,0,0.12);color:#ffd700;border:1px solid rgba(255,215,0,0.25);font-weight:700;margin-left:4px;vertical-align:middle;display:inline-block;white-space:nowrap;">MINERAL</span>';
        if (mat === 1) return `<span style="font-size:8px;padding:0.5px 3.5px;border-radius:3px;background:rgba(255,100,50,0.12);color:#ff6432;border:1px solid rgba(255,100,50,0.25);font-weight:700;margin-left:4px;vertical-align:middle;display:inline-block;white-space:nowrap;">ESTÉRIL: ${matName}</span>`;
        if (mat === 2) return `<span style="font-size:8px;padding:0.5px 3.5px;border-radius:3px;background:rgba(0,200,180,0.12);color:#00c8b4;border:1px solid rgba(0,200,180,0.25);font-weight:700;margin-left:4px;vertical-align:middle;display:inline-block;white-space:nowrap;">AMBOS (MINERAL / ${matName})</span>`;
        return '';
    }

    // Obtener categoría de filtro efectiva para una ruta
    function getRouteMatCategory(route) {
        const mat = route.selectedMaterialType || 0;
        if (mat === 0) return 1; // Mineral
        if (mat === 1) return 2; // Estéril
        if (mat === 2) return 3; // Ambos
        return 1;
    }

    // Aplicar filtro de material + texto sobre la lista de rutas sin re-renderizar (para no perder selecciones)
    function applyRoutesFilter() {
        const textQuery = ($('#searchRoutes').val() || '').toLowerCase().trim();
        const matFilter = _activeMatFilter;

        $('#routesList .checkbox-item').each(function () {
            const name = String($(this).attr('data-name') || '').toLowerCase();
            const matCat = String($(this).attr('data-mat') || '');

            let matchesMaterial = true;
            if (matFilter !== 'all') {
                matchesMaterial = (matCat === matFilter);
            }

            let matchesText = true;
            if (textQuery) {
                matchesText = (name.indexOf(textQuery) > -1);
            }

            if (matchesMaterial && matchesText) {
                $(this).css('display', '');
            } else {
                $(this).css('display', 'none');
            }
        });
    }

    // Filtrar rutas por tipo de material al hacer clic en los botones
    window.filterRoutesByMaterial = function(matType) {
        _activeMatFilter = matType === 'all' ? 'all' : String(matType);
        // Actualizar estado activo de botones
        $('#routeMaterialFilter .route-mat-btn').removeClass('active');
        $(`#routeMaterialFilter .route-mat-btn[data-mat="${matType}"]`).addClass('active');
        applyRoutesFilter();
    };

    // Helper to clear a search input and trigger filter
    window.clearSearchInput = function(inputId, clearBtnId) {
        $(`#${inputId}`).val('').trigger('input');
        $(`#${clearBtnId}`).addClass('d-none');
    };

    // Toggle clear button visibility and run search
    function setupSearchFilterWithClear(inputId, listContainerId, clearBtnId) {
        $(`#${inputId}`).on('input', function () {
            const query = ($(this).val() || '').toLowerCase().trim();
            
            // Show/hide clear button
            if (query.length > 0) {
                $(`#${clearBtnId}`).removeClass('d-none');
            } else {
                $(`#${clearBtnId}`).addClass('d-none');
            }

            $(`#${listContainerId} .checkbox-item`).each(function () {
                const name = String($(this).attr('data-name') || '').toLowerCase();
                if (name.indexOf(query) > -1) {
                    $(this).css('display', '');
                } else {
                    $(this).css('display', 'none');
                }
            });
        });
    }

    // Buscador interactivo para los catálogos
    $('#searchRoutes').on('input', function () {
        const query = ($(this).val() || '').toLowerCase().trim();
        if (query.length > 0) {
            $('#btnClearSearchRoutes').removeClass('d-none');
        } else {
            $('#btnClearSearchRoutes').addClass('d-none');
        }
        applyRoutesFilter();
    });

    setupSearchFilterWithClear('searchEmployees', 'employeesList', 'btnClearSearchEmployees');
    setupSearchFilterWithClear('searchVehicles', 'vehiclesList', 'btnClearSearchVehicles');

    // Guardar Configuración del Bot
    $('#botConfigForm').submit(function (e) {
        e.preventDefault();
        
        const tonnageMin = parseInt($('#tonnageMin').val());
        const tonnageMax = parseInt($('#tonnageMax').val());
        const timeMin = parseInt($('#timeMin').val());
        const timeMax = parseInt($('#timeMax').val());

        if (tonnageMin > tonnageMax) {
            alert("El porcentaje mínimo no puede ser mayor que el máximo.");
            return;
        }
        if (timeMin > timeMax) {
            alert("El tiempo mínimo no puede ser mayor que el máximo.");
            return;
        }

        // Recolectar seleccionados
        const selectedRoutes = [];
        $('#routesList input[type="checkbox"]:checked').each(function () {
            selectedRoutes.push(parseInt($(this).val()));
        });

        const selectedEmployees = [];
        $('#employeesList input[type="checkbox"]:checked').each(function () {
            selectedEmployees.push(parseInt($(this).val()));
        });

        const selectedVehicles = [];
        $('#vehiclesList input[type="checkbox"]:checked').each(function () {
            selectedVehicles.push(parseInt($(this).val()));
        });

        const configPayload = {
            TonnageVariation: [tonnageMin, tonnageMax],
            Time: [timeMin, timeMax],
            SelectedRoutes: selectedRoutes,
            SelectedEmployees: selectedEmployees,
            SelectedVehicles: selectedVehicles
        };

        showLoadingScreen("Guardando configuración del Bot...");
        $.ajax({
            url: `/api/ConfBoot/dataconf?serverId=${activeServerId}`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(configPayload),
            success: function (res) {
                alert("Configuración guardada correctamente.");
            },
            error: function (xhr) {
                alert("Error al guardar la configuración: " + xhr.responseText);
            },
            complete: function() {
                hideLoadingScreen();
            }
        });
    });

    // Switches Toggle Handlers
    $('#botSwitch').change(function () {
        const isEnabled = $(this).is(':checked');
        showLoadingScreen(isEnabled ? "Iniciando Bot automático..." : "Deteniendo Bot automático...");
        
        $.ajax({
            url: `/api/sync/toggle?serverId=${activeServerId}`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(isEnabled),
            success: function (res) {
                const statusBadge = $('#connectionStatusBadge');
                statusBadge.text(isEnabled ? 'Autónomo Activo' : 'Listo / Pausado');
                
                if (isEnabled) {
                    $(`#tab-server-${activeServerId} .status-dot`).removeClass('online offline').addClass('running');
                } else {
                    $(`#tab-server-${activeServerId} .status-dot`).removeClass('running offline').addClass('online');
                }
            },
            error: function (xhr) {
                alert("Error al cambiar estado del bot: " + xhr.responseText);
                $('#botSwitch').prop('checked', !isEnabled);
            },
            complete: function() {
                hideLoadingScreen();
            }
        });
    });

    $('#syncSwitch').change(function () {
        const isEnabled = $(this).is(':checked');
        showLoadingScreen(isEnabled ? "Habilitando sincronización..." : "Deshabilitando sincronización...");
        
        $.ajax({
            url: `/api/sync/togglelocal?serverId=${activeServerId}`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(isEnabled),
            success: function (res) {
                // success
            },
            error: function (xhr) {
                alert("Error al cambiar sincronización: " + xhr.responseText);
                $('#syncSwitch').prop('checked', !isEnabled);
            },
            complete: function() {
                hideLoadingScreen();
            }
        });
    });

    // Manual Operations Buttons
    $('#btnBotManual').click(function () {
        if (!confirm("¿Deseas registrar un acarreo simulado en este momento?")) return;
        
        showLoadingScreen("Ejecutando acarreo aleatorio...");
        
        $.ajax({
            url: `/api/sync/botmanual?serverId=${activeServerId}`,
            type: 'POST',
            success: function (res) {
                alert("Acarreo registrado correctamente.");
                loadHaulageHistory(activeServerId);
            },
            error: function (xhr) {
                alert("Error en la ejecución: " + xhr.responseText);
            },
            complete: function () {
                hideLoadingScreen();
            }
        });
    });

    $('#btnSyncManual').click(function () {
        if (!confirm("¿Deseas descargar los catálogos del servidor SmartFlow ahora?")) return;
        
        showLoadingScreen("Sincronizando catálogos de SmartFlow...");
        
        $.ajax({
            url: `/api/sync/manuallocal?serverId=${activeServerId}`,
            type: 'POST',
            success: function (res) {
                alert("Catálogos descargados y actualizados correctamente.");
                fetchServerStatusAndConfig(activeServerId);
            },
            error: function (xhr) {
                alert("Error en la sincronización: " + xhr.responseText);
            },
            complete: function () {
                hideLoadingScreen();
            }
        });
    });

    // Eliminar Servidor
    $('#btnDeleteServer').click(function () {
        const server = serverList.find(s => s.id === activeServerId);
        if (!server) return;

        if (!confirm(`¿ESTÁS SEGURO de eliminar el servidor "${server.name}"? Se borrarán todos los acarreos y configuraciones locales.`)) {
            return;
        }

        showLoadingScreen(`Eliminando servidor ${server.name}...`);
        $.ajax({
            url: `/api/ServerConfig/${activeServerId}`,
            type: 'DELETE',
            success: function () {
                alert("Servidor eliminado exitosamente.");
                localStorage.removeItem('activeServerId');
                loadServers();
            },
            error: function (xhr) {
                alert("Error al eliminar el servidor: " + xhr.responseText);
                hideLoadingScreen();
            }
        });
    });

    // Agregar Nuevo Servidor - Formulario
    $('#addServerForm').submit(function (e) {
        e.preventDefault();
        
        const payload = {
            Name: $('#serverNameInput').val(),
            ApiUrl: $('#serverUrlInput').val(),
            ClientId: $('#clientIdInput').val(),
            ClientSecret: $('#clientSecretInput').val(),
            Username: $('#userInput').val(),
            Password: $('#passwordInput').val(),
            IsActive: true
        };

        $('#btnSubmitAddServer').prop('disabled', true).text("Verificando...");
        $('#connectionError').addClass('d-none').text("");

        $.ajax({
            url: '/api/ServerConfig/connect',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function (newServer) {
                addServerModal.hide();
                $('#addServerForm')[0].reset();
                loadServers(newServer.id);
            },
            error: function (xhr) {
                let errText = "Error al conectar con el servidor de SmartFlow.";
                try {
                    const resJson = JSON.parse(xhr.responseText);
                    if (resJson.message) errText = resJson.message;
                } catch(e) {}
                
                $('#connectionError').removeClass('d-none').text(errText);
            },
            complete: function() {
                $('#btnSubmitAddServer').prop('disabled', false).text("Conectar y Registrar");
            }
        });
    });

    // Cargar Historial de Acarreos en la tabla HTML
    function loadHaulageHistory(serverId) {
        $.getJSON(`/api/Haulages?serverId=${serverId}&limit=${haulageLimit}`, function (data) {
            const tbody = $('#latestHaulagesTableBody');
            tbody.empty();
            if (data.length === 0) {
                tbody.append('<tr><td colspan="8" class="px-6 py-8 text-center text-slate-500 font-sans">Sin registros de acarreos locales para este servidor.</td></tr>');
                return;
            }
            data.forEach(item => {
                // Generar badge o color premium según material
                let materialBadge = '';
                const matName = (item.materialName || '').toUpperCase();
                if (matName.includes('MINERAL')) {
                    materialBadge = `<span class="px-2 py-0.5 text-[10px] font-bold rounded bg-[#f59e0b]/10 text-[#f59e0b] border border-[#f59e0b]/20 uppercase">${item.materialName}</span>`;
                } else if (matName !== '') {
                    materialBadge = `<span class="px-2 py-0.5 text-[10px] font-bold rounded bg-[#ef4444]/10 text-[#ef4444] border border-[#ef4444]/20 uppercase">${item.materialName}</span>`;
                } else {
                    materialBadge = `<span class="text-slate-500">-</span>`;
                }

                // Aplicar offset de zona horaria a la fecha del acarreo
                let dateDisplay = item.dateofcarries || '';
                try {
                    const rawDate = new Date(item.dateofcarries);
                    if (!isNaN(rawDate.getTime())) {
                        const tzFn = window.applyTimezoneOffset;
                        const adjustedDate = tzFn ? tzFn(rawDate) : rawDate;
                        dateDisplay = adjustedDate.toLocaleString('es-MX', {
                            year: 'numeric', month: '2-digit', day: '2-digit',
                            hour: '2-digit', minute: '2-digit', second: '2-digit',
                            hour12: false
                        });
                    }
                } catch(e) { /* mantener valor original si falla */ }

                const tr = `
                    <tr class="hover:bg-white/5 transition-colors group">
                        <td class="px-6 py-4 font-data-mono text-[13px] text-white">${item.haulageId}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px] text-slate-300">${item.vehicleEconomicNumber || item.vehicleId}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px] text-slate-300">${item.employeeFullName || item.employeeId}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px] text-slate-300">${item.routeDescription || item.pathId}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px]">${materialBadge}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px] text-[#39ff14] font-bold">${item.weight.toFixed(2)}</td>
                        <td class="px-6 py-4 font-data-mono text-[13px] text-slate-400">${dateDisplay}</td>
                        <td class="px-6 py-4 text-[12px] text-slate-400">${item.comments || ''}</td>
                    </tr>
                `;
                tbody.append(tr);
            });

        }).fail(function () {
            console.error("Error al obtener histórico de acarreos.");
        });
    }

    // Drag and Drop excel upload zone
    const dropzone = $('#dropzone');
    
    dropzone.on('dragover dragenter', function (e) {
        e.preventDefault();
        e.stopPropagation();
        dropzone.addClass('dragover');
    });

    dropzone.on('dragleave dragend drop', function (e) {
        e.preventDefault();
        e.stopPropagation();
        dropzone.removeClass('dragover');
    });

    dropzone.on('drop', function (e) {
        const files = e.originalEvent.dataTransfer.files;
        if (files.length > 0) {
            handleExcelUpload(files[0]);
        }
    });

    dropzone.on('click', function () {
        $('#excelFile').click();
    });

    $('#excelFile').change(function () {
        const files = this.files;
        if (files.length > 0) {
            handleExcelUpload(files[0]);
        }
    });

    const correctionModal = new bootstrap.Modal(document.getElementById('correctionModal'));

    function handleExcelUpload(file) {
        if (!file.name.endsWith('.xlsx') && !file.name.endsWith('.xls')) {
            alert("Por favor sube solo archivos Excel (.xlsx o .xls)");
            return;
        }

        const formData = new FormData();
        formData.append('file', file);

        $('#uploadStatus').html('<span class="text-info"><i class="bi bi-hourglass-split"></i> Subiendo y procesando Excel...</span>');
        showLoadingScreen("Procesando archivo de acarreos...");

        $.ajax({
            url: `/api/Import/Upload?serverId=${activeServerId}`,
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            success: function (res) {
                $('#uploadStatus').html(`<span class="text-success"><i class="bi bi-check-circle-fill"></i> ${res.message}</span>`);
                loadHaulageHistory(activeServerId);

                if (res.failedRows && res.failedRows.length > 0) {
                    showFailedRowsModal(res.failedRows);
                } else {
                    alert("Importación de acarreos finalizada con éxito.");
                }
            },
            error: function (xhr) {
                let errText = "Error en la carga.";
                try {
                    const resJson = JSON.parse(xhr.responseText);
                    if (resJson.message) errText = resJson.message;
                } catch(e) {}
                
                $('#uploadStatus').html(`<span class="text-danger"><i class="bi bi-exclamation-triangle-fill"></i> ${errText}</span>`);
            },
            complete: function() {
                hideLoadingScreen();
                $('#excelFile').val('');
            }
        });
    }

    function showFailedRowsModal(failedRows) {
        const tbody = $('#correctionTableBody');
        tbody.empty();

        failedRows.forEach((row, index) => {
            const errMsg = row.errorMessage || 'Error desconocido';
            // Row with editable fields
            const tr = `
                <tr data-row-number="${row.rowNumber}" class="failed-import-row">
                    <td colspan="8" style="padding:0; border:none;">
                        <div style="margin: 4px 0; border-radius: 8px; overflow: hidden; border: 1px solid rgba(239,68,68,0.35); background: rgba(239,68,68,0.04);">
                            <div style="display:flex; align-items:center; gap:10px; padding: 6px 12px; background: rgba(239,68,68,0.10); border-bottom:1px solid rgba(239,68,68,0.2);">
                                <span style="font-size:13px;">⚠️</span>
                                <span style="color:#f87171; font-family: 'JetBrains Mono', monospace; font-size:12px; font-weight:700;">Fila ${row.rowNumber || (index + 1)}:</span>
                                <span style="color:#fca5a5; font-family: 'JetBrains Mono', monospace; font-size:12px; flex:1;">${errMsg}</span>
                            </div>
                            <div style="display:grid; grid-template-columns: 60px 1fr 1fr 1fr 90px 1fr 160px; gap:6px; padding:8px 12px; align-items:center;">
                                <div style="text-align:center; font-family:monospace; font-size:11px; color:#64748b;">${row.rowNumber || (index + 1)}</div>
                                <input type="text" class="form-control form-control-sm bg-dark text-light border-secondary val-vehicle" placeholder="Vehículo" value="${row.vehicleCode || ''}">
                                <input type="text" class="form-control form-control-sm bg-dark text-light border-secondary val-employee-name" placeholder="Empleado" value="${row.employeeName || ''}">
                                <input type="text" class="form-control form-control-sm bg-dark text-light border-secondary val-route" placeholder="Ruta" value="${row.routeDescription || ''}">
                                <input type="number" step="0.01" class="form-control form-control-sm bg-dark text-light border-secondary val-weight" placeholder="Peso" value="${row.weight || 0}">
                                <input type="text" class="form-control form-control-sm bg-dark text-light border-secondary val-material" placeholder="Material" value="${row.materialName || ''}">
                                <input type="text" class="form-control form-control-sm bg-dark text-light border-secondary val-date" placeholder="YYYY-MM-DD HH:MM:SS" value="${row.dateStr || ''}">
                            </div>
                        </div>
                    </td>
                </tr>
            `;
            tbody.append(tr);
        });

        correctionModal.show();
    }

    window.submitCorrectedRows = function() {
        const rows = [];
        $('#correctionTableBody tr.failed-import-row').each(function() {
            const row = {
                VehicleCode: $(this).find('.val-vehicle').val().trim(),
                EmployeeName: $(this).find('.val-employee-name').val().trim(),
                RouteDescription: $(this).find('.val-route').val().trim(),
                Weight: parseFloat($(this).find('.val-weight').val()) || 0,
                MaterialName: $(this).find('.val-material').val().trim(),
                DateStr: $(this).find('.val-date').val().trim()
            };
            rows.push(row);
        });

        if (rows.length === 0) return;

        showLoadingScreen("Re-importando corregidos...");
        $('#btnSubmitCorrected').prop('disabled', true).text("Importando...");

        $.ajax({
            url: `/api/Import/ImportRows?serverId=${activeServerId}`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(rows),
            success: function(res) {
                loadHaulageHistory(activeServerId);

                if (res.failedRows && res.failedRows.length > 0) {
                    showFailedRowsModal(res.failedRows);
                    alert("Algunas filas aún presentan errores. Por favor corrígelas.");
                } else {
                    correctionModal.hide();
                    alert("Todas las filas se han importado correctamente.");
                }
            },
            error: function(xhr) {
                alert("Error al importar corregidos: " + xhr.responseText);
            },
            complete: function() {
                $('#btnSubmitCorrected').prop('disabled', false).text("Importar Corregidos");
                hideLoadingScreen();
            }
        });
    };

    // Renombrar Servidor Activo
    window.editActiveServerName = function() {
        const server = serverList.find(s => s.id === activeServerId);
        if (!server) return;
        
        const newName = prompt("Ingresa el nuevo nombre para el servidor:", server.name);
        if (newName === null) return;
        
        const trimmedName = newName.trim();
        if (!trimmedName) {
            alert("El nombre del servidor no puede estar vacío.");
            return;
        }
        
        const payload = { ...server, name: trimmedName };
        
        showLoadingScreen("Renombrando servidor...");
        $.ajax({
            url: `/api/ServerConfig/${activeServerId}`,
            type: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function(updated) {
                server.name = updated.name;
                $('#activeServerName').text(updated.name);
                loadServers(activeServerId);
            },
            error: function(xhr) {
                alert("Error al renombrar el servidor: " + xhr.responseText);
                hideLoadingScreen();
            }
        });
    };

    // Formulario de generación masiva
    $('#bulkGenerateForm').submit(function (e) {
        e.preventDefault();
        
        const payload = {
            ServerId: activeServerId,
            StartDate: $('#bulkStartDate').val(),
            EndDate: $('#bulkEndDate').val(),
            TotalTonnage: parseFloat($('#bulkTotalTonnage').val())
        };

        if (isNaN(payload.TotalTonnage) || payload.TotalTonnage <= 0) {
            alert("El tonelaje total debe ser un número positivo.");
            return;
        }

        showLoadingScreen("Iniciando generación masiva...");
        
        $.ajax({
            url: '/api/sync/bulk-generate',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function(res) {
                alert(res.message);
                $('#bulkGenerateForm')[0].reset();
            },
            error: function(xhr) {
                alert("Error al iniciar generación masiva: " + xhr.responseText);
            },
            complete: function() {
                hideLoadingScreen();
            }
        });
    });

    // Capturar notificaciones en tiempo real desde SignalR despachadas por layout.js
    window.addEventListener("ServerNotification", function (e) {
        const payload = e.detail;
        if (!payload) return;

        if (typeof payload === 'object') {
            const serverId = payload.serverId || payload.ServerId;
            const message = payload.message || payload.Message;
            const isError = payload.error || payload.Error;

            if (serverId == activeServerId) {
                // Si es un registro exitoso, refrescar la lista de acarreos
                if (!isError && !message.includes("catálogo")) {
                    loadHaulageHistory(activeServerId);
                }
            }
        }
    });

    // Helper functions for loading screens
    function showLoadingScreen(text = "Procesando...") {
        $('#loadingText').text(text);
        $('#loading-screen').removeClass('d-none');
    }

    // Manejar selector de límite de acarreos
    $('#haulageLimitSelector button').click(function () {
        const selectedLimit = parseInt($(this).attr('data-limit'));
        if (selectedLimit && selectedLimit !== haulageLimit) {
            haulageLimit = selectedLimit;
            
            // Actualizar estilo visual de los botones
            $('#haulageLimitSelector button').removeClass('text-[#39ff14] bg-[#39ff14]/12')
                                             .addClass('text-slate-400');
            $(this).removeClass('text-slate-400')
                   .addClass('text-[#39ff14] bg-[#39ff14]/12');
            
            // Recargar datos
            if (activeServerId) {
                loadHaulageHistory(activeServerId);
            }
        }
    });

    // Escuchar el evento de cambio global de zona horaria
    window.addEventListener("GlobalTimezoneChanged", function () {
        if (activeServerId) {
            loadHaulageHistory(activeServerId);
        }
    });

    function hideLoadingScreen() {
        $('#loading-screen').addClass('d-none');
    }

    // ========== RETHINKDB BOT HANDLERS ==========
    
    // Cargar configuración del bot RethinkDB cuando se cambia de servidor
    function loadRethinkBotConfig(serverId) {
        // Obtener la IP del servidor activo como default para el host de RethinkDB
        var serverApiUrl = $('#activeServerUrl').text().trim();
        var defaultHost = serverApiUrl && serverApiUrl !== '--' ? serverApiUrl.replace(/^https?:\/\//, '').split(':')[0].split('/')[0] : '';

        $.getJSON(`/api/RethinkBot/${serverId}`, function(config) {
            $('#rethinkHost').val(config.rethinkHost || defaultHost);
            $('#rethinkPort').val(config.rethinkPort || 28015);
            $('#rethinkPassword').val(config.rethinkPassword || '');
            $('#rethinkInterval').val(config.intervalSeconds || 30);
            $('#rethinkMaxVehicles').val(config.maxSimultaneousVehicles || 5);
            $('#rethinkBotSwitch').prop('checked', config.isEnabled);
        }).fail(function() {
            // Defaults
            $('#rethinkHost').val(defaultHost);
            $('#rethinkPort').val(28015);
            $('#rethinkPassword').val('');
            $('#rethinkInterval').val(30);
            $('#rethinkMaxVehicles').val(5);
            $('#rethinkBotSwitch').prop('checked', false);
        });
    }

    // Guardar configuración
    $('#btnSaveRethinkConfig').click(function() {
        const payload = {
            rethinkHost: $('#rethinkHost').val().trim(),
            rethinkPort: parseInt($('#rethinkPort').val()) || 28015,
            rethinkPassword: $('#rethinkPassword').val() || '',
            intervalSeconds: parseInt($('#rethinkInterval').val()) || 30,
            maxSimultaneousVehicles: parseInt($('#rethinkMaxVehicles').val()) || 5,
            isEnabled: $('#rethinkBotSwitch').is(':checked')
        };

        if (payload.isEnabled && !payload.rethinkHost) {
            alert('Ingresa el host de RethinkDB antes de activar el bot.');
            return;
        }

        $.ajax({
            url: `/api/RethinkBot/${activeServerId}`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function() {
                alert('Configuración del bot RethinkDB guardada.');
            },
            error: function(xhr) {
                alert('Error al guardar: ' + (xhr.responseJSON?.message || xhr.responseText));
            }
        });
    });

    // Toggle rápido del switch
    $('#rethinkBotSwitch').change(function() {
        const isEnabled = $(this).is(':checked');
        const host = $('#rethinkHost').val().trim();

        if (isEnabled && !host) {
            alert('Configura el host de RethinkDB primero.');
            $(this).prop('checked', false);
            return;
        }

        $.ajax({
            url: `/api/RethinkBot/${activeServerId}/toggle`,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(isEnabled),
            success: function() {
                appendRethinkLog(isEnabled ? 'Bot activado' : 'Bot desactivado');
            },
            error: function(xhr) {
                alert('Error: ' + (xhr.responseJSON?.message || xhr.responseText));
                $('#rethinkBotSwitch').prop('checked', !isEnabled);
            }
        });
    });

    // Log helper para RethinkDB
    function appendRethinkLog(message, isError) {
        const container = $('#rethinkLogArea');
        const now = new Date().toLocaleTimeString();
        const color = isError ? 'color: var(--danger)' : 'color: var(--text-muted)';
        container.append(`<p style="${color}">[${now}] ${message}</p>`);
        container.scrollTop(container[0].scrollHeight);
        // Limitar a 100 líneas
        const lines = container.find('p');
        if (lines.length > 100) lines.first().remove();
    }

    // Escuchar notificaciones de SignalR que vengan del RethinkBot
    window.addEventListener("ServerNotification", function(e) {
        const payload = e.detail;
        if (!payload || typeof payload !== 'object') return;
        const sId = payload.serverId || payload.ServerId;
        if (sId != activeServerId) return;
        const message = payload.message || payload.Message || '';
        if (message.includes('[RethinkBot]')) {
            const isError = payload.error || payload.Error;
            appendRethinkLog(message.replace('[RethinkBot] ', ''), isError);
        }
    });

    // Polling de logs del bot RethinkDB (cada 10s si está activo)
    setInterval(function() {
        if (!$('#rethinkBotSwitch').is(':checked') || !activeServerId) return;
        $.getJSON(`/api/RethinkBot/${activeServerId}/status`, function(data) {
            if (data.lastLog) {
                appendRethinkLog(data.lastLog);
            }
        }).fail(function() {});
    }, 10000);
});

// Función de ayuda global expuesta para abrir el modal
window.openAddServerModal = function() {
    $('#connectionError').addClass('d-none').text("");
    $('#addServerForm')[0].reset();
    const addServerModal = bootstrap.Modal.getInstance(document.getElementById('addServerModal')) || new bootstrap.Modal(document.getElementById('addServerModal'));
    addServerModal.show();
};

// Función global para seleccionar/deseleccionar todos los elementos de un catálogo (solo visibles/filtrados)
window.toggleSelectAll = function(listId, checkBool) {
    $(`#${listId} .checkbox-item:visible input[type="checkbox"]`).prop('checked', checkBool).trigger('change');
};

function showNotificationInPage(type, msg) {
    if (window.showNotification) {
        window.showNotification(type, msg);
    } else {
        console.log(msg);
    }
}
