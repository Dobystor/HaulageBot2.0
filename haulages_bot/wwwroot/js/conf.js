$(() => {

    loadConfigurationData();
    function loadConfigurationData() {
        $.ajax({
            url: '/api/ConfBoot/loadDataFromDb',
            type: 'GET',
            success(response) {
                // Formatear la variación de tonelaje como "5-25%"
                console.log(response);
                const tonnageFormatted = response.tonnageVariation.join('-') + '%';
                $('#text3').dxTextBox('instance').option('value', tonnageFormatted); // Actualizar el textbox con el formato deseado

                // Formatear el tiempo como "5-25 min."
                const timeFormatted = response.time.join('-') + ' min.';
                $('#text4').dxTextBox('instance').option('value', timeFormatted); // Actualizar el textbox con el formato deseado

                // Asignar las rutas, empleados y vehículos seleccionados
                $('#ruta').dxDropDownBox('instance').option('value', response.selectedRoutes);
                $('#employeeDropdown').dxDropDownBox('instance').option('value', response.selectedEmployees);
                $('#vehicleDropdown').dxDropDownBox('instance').option('value', response.selectedVehicles);
            },
            error(xhr, status, error) {
                console.error('Error al cargar los datos:', error);
                DevExpress.ui.notify('Error al cargar los datos', 'error', 3000);
            }
        });

    }


    $('#text3').dxTextBox({
        placeholder: 'Variación de tonelaje',
        //value: '5-25%', // Valor por defecto del 5-25%
        onValueChanged: function (e) {
            console.log(e.value); // Log de cambios de valor (opcional)
        },
        // mask: 'numeric',
        // maskRules: {
        //     'X': /[0-9\-]/,
        //     '9': /[0-9]/
        // },
        maskChar: '_'
    });

    $('#text4').dxTextBox({
        placeholder: 'Ingrese tiempo',
        // value: '00:00:00', // Valor por defecto del tiempo
        //value: '5-25 min.', // Valor por defecto del 5-25%
        onValueChanged: function (e) {
            console.log(e.value); // Log de cambios de valor (opcional)
        },
        // mask: '00:00:00'
    });

    let dataGrid; // Para el DataGrid de rutas

    // Función para crear un DataSource asíncrono
    const makeAsyncDataSource = function (jsonFile) {
        return new DevExpress.data.CustomStore({
            loadMode: 'raw',
            key: 'haulagePathId', // Cambia esto al ID de la ruta
            load() {
                return $.getJSON(`/api/Routes/GetRoutes`); // Llama a la API para obtener las rutas
            },
        });
    };

    // Configuración del DropDownBox para rutas
    $('#ruta').dxDropDownBox({
        value: [], // Mantiene un arreglo vacío para la selección inicial
        valueExpr: 'haulagePathId', // Valor que se enviará al backend
        displayExpr: 'description', // Campo que se mostrará en el DropDownBox
        placeholder: 'Seleccione una o varias rutas',
        showClearButton: true, // Botón para limpiar la selección
        searchEnabled: true, // Permite buscar en el DropDownBox
        dataSource: makeAsyncDataSource('routes.json'), // Usa tu archivo JSON aquí
        contentTemplate(e) {
            const v = e.component.option('value');
            const $dataGrid = $('<div>').dxDataGrid({
                dataSource: e.component.getDataSource(),
                columns: [
                    { dataField: 'haulagePathId', caption: 'ID', visible: false }, // Oculta el ID si no es necesario mostrarlo
                    { dataField: 'description', caption: 'Descripción' },
                ],
                hoverStateEnabled: true,
                paging: { enabled: true, pageSize: 10 },
                filterRow: { visible: true },
                scrolling: { mode: 'virtual' },
                height: 300, // Ajusta la altura del DataGrid
                selection: {
                    mode: 'multiple', // Permite selección múltiple
                    showCheckBoxesMode: 'always', // Muestra las casillas de verificación siempre
                },
                selectedRowKeys: v, // Sincroniza la selección inicial
                onSelectionChanged(selectedItems) {
                    const keys = selectedItems.selectedRowKeys;
                    e.component.option('value', keys); // Actualiza el valor del DropDownBox
                },
            });

            // Sincroniza la selección del DataGrid con el valor del DropDownBox
            e.component.on('valueChanged', (args) => {
                const { value } = args;
                $dataGrid.dxDataGrid('instance').selectRows(value, false); // Selecciona las filas en el DataGrid
            });

            return $dataGrid; // Devuelve el DataGrid como contenido del DropDownBox
        },
    });

    // Función para crear un DataSource asíncrono para empleados
    const makeAsyncEmployeeDataSource = function () {
        return new DevExpress.data.CustomStore({
            loadMode: 'raw',
            key: 'employeeId', // ID de empleado
            load() {
                return $.getJSON(`/api/Employees/GetEmployees`); // Llama a la API para obtener los empleados
            },
        });
    };

    // Función para crear un DataSource asíncrono para vehículos
    const makeAsyncVehicleDataSource = function () {
        return new DevExpress.data.CustomStore({
            loadMode: 'raw',
            key: 'vehicleId', // ID del vehículo
            load() {
                return $.getJSON(`/api/Vehicles/GetVehicles`); // Llama a la API para obtener los vehículos
            },
        });
    };

    // Configuración del DropDownBox para empleados
    $('#employeeDropdown').dxDropDownBox({
        value: [], // Mantiene un arreglo vacío para la selección inicial
        valueExpr: 'employeeId', // Valor que se enviará al backend
        displayExpr: 'nombreCompleto', // Campo que se mostrará en el DropDownBox
        placeholder: 'Seleccione uno o varios empleados',
        showClearButton: true,
        searchEnabled: true,
        dataSource: makeAsyncEmployeeDataSource(), // Usa el DataSource creado
        contentTemplate(e) {
            const v = e.component.option('value');
            const $dataGrid = $('<div>').dxDataGrid({
                dataSource: e.component.getDataSource(),
                columns: [
                    { dataField: 'employeeId', caption: 'ID', visible: false },
                    { dataField: 'nombreCompleto', caption: 'Nombre Completo' },
                ],
                hoverStateEnabled: true,
                paging: { enabled: true, pageSize: 10 },
                filterRow: { visible: true },
                scrolling: { mode: 'virtual' },
                height: 300,
                selection: {
                    mode: 'multiple',
                    showCheckBoxesMode: 'always',
                },
                selectedRowKeys: v,
                onSelectionChanged(selectedItems) {
                    const keys = selectedItems.selectedRowKeys;
                    e.component.option('value', keys);
                },
            });

            e.component.on('valueChanged', (args) => {
                const { value } = args;
                $dataGrid.dxDataGrid('instance').selectRows(value, false);
            });

            return $dataGrid;
        },
    });

    // Configuración del DropDownBox para vehículos
    $('#vehicleDropdown').dxDropDownBox({
        value: [], // Mantiene un arreglo vacío para la selección inicial
        valueExpr: 'vehicleId', // Valor que se enviará al backend
        displayExpr: 'economicNumber', // Campo que se mostrará en el DropDownBox
        placeholder: 'Seleccione uno o varios vehículos',
        showClearButton: true,
        searchEnabled: true,
        dataSource: makeAsyncVehicleDataSource(), // Usa el DataSource creado
        contentTemplate(e) {
            const v = e.component.option('value');
            const $dataGrid = $('<div>').dxDataGrid({
                dataSource: e.component.getDataSource(),
                columns: [
                    { dataField: 'vehicleId', caption: 'ID', visible: false },
                    { dataField: 'economicNumber', caption: 'Vehiculo' },
                ],
                hoverStateEnabled: true,
                paging: { enabled: true, pageSize: 10 },
                filterRow: { visible: true },
                scrolling: { mode: 'virtual' },
                height: 300,
                selection: {
                    mode: 'multiple',
                    showCheckBoxesMode: 'always',
                },
                selectedRowKeys: v,
                onSelectionChanged(selectedItems) {
                    const keys = selectedItems.selectedRowKeys;
                    e.component.option('value', keys);
                },
            });

            e.component.on('valueChanged', (args) => {
                const { value } = args;
                $dataGrid.dxDataGrid('instance').selectRows(value, false);
            });

            return $dataGrid;
        },
    });

    // Botón Enviar
    $('#button2').dxButton({
        text: 'Guardar Configuración',
        onClick() {
            // Deshabilitar el botón al hacer clic
            const buttonInstance = $('#button2').dxButton('instance');
            buttonInstance.option('disabled', true);

            // Recoger los valores de los controles DevExtreme
            const tonnageVariation = $('#text3').dxTextBox('instance').option('value');
            const time = $('#text4').dxTextBox('instance').option('value');
            const selectedRoutes = $('#ruta').dxDropDownBox('instance').option('value');
            const selectedEmployees = $('#employeeDropdown').dxDropDownBox('instance').option('value');
            const selectedVehicles = $('#vehicleDropdown').dxDropDownBox('instance').option('value');

            const tonnageList = extractRangeWeight(tonnageVariation);
            const timeList = extractRange(time);

            if (!tonnageList || !timeList || !selectedRoutes || selectedRoutes.length === 0 || !selectedEmployees || selectedEmployees.length === 0 || !selectedVehicles || selectedVehicles.length === 0) {
                DevExpress.ui.notify('Por favor, complete todos los campos antes de enviar.', 'error', 3000);
                // Rehabilitar el botón si hay error en la validación
                buttonInstance.option('disabled', false);
                return;
            }

            const datos = {
                tonnageVariation: tonnageList,
                time: timeList,
                selectedRoutes: selectedRoutes,
                selectedEmployees: selectedEmployees,
                selectedVehicles: selectedVehicles
            };

            $.ajax({
                url: '/api/ConfBoot/dataconf',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(datos),
                success(response) {
                    DevExpress.ui.notify('Datos enviados exitosamente', 'success', 3000);

                    // Esperar 5 segundos y luego refrescar la página
                    setTimeout(() => {
                        location.reload();
                    }, 5000); // Tiempo en ms
                },
                error(xhr, status, error) {
                    console.error('Error al enviar los datos:', error);
                    DevExpress.ui.notify('Error al enviar los datos', 'error', 3000);

                    // Rehabilitar el botón en caso de error
                    buttonInstance.option('disabled', false);
                }
            });
        }
    });


    // Función genérica para extraer los valores numéricos de un rango (e.g., '5-25')
    // y verificar que los valores estén en el rango permitido (5 <= valor <= 60)
    function extractRange(rangeString) {
        const match = rangeString.match(/(\d+)-(\d+)/); // Extrae los dos números separados por "-"
        if (match) {
            const min = parseInt(match[1], 10);
            const max = parseInt(match[2], 10);

            // Validar que ambos valores estén entre 5 y 60
            if (min >= 1 && max <= 60 && min <= max) {
                return [min, max]; // Retorna como una lista de dos números si es válido
            } else {
                DevExpress.ui.notify('El rango debe estar entre 1 y 60, y el valor mínimo debe ser menor o igual al máximo.', 'error', 3000);
                return null;
            }
        }
        DevExpress.ui.notify('Formato de entrada incorrecto. Use el formato num1-num2.', 'error', 3000);
        return null; // Devuelve null si no encuentra el patrón
    }

    function extractRangeWeight(rangeString) {
        // Extraer números del rango con soporte para valores negativos
        const match = rangeString.match(/(-?\d+)-(-?\d+)/); // Incluye el signo "-" opcional para números negativos
        if (match) {
            const min = parseInt(match[1], 10);
            const max = parseInt(match[2], 10);

            // Validar que ambos valores estén dentro del rango permitido y que min <= max
            if (min >= -30 && max <= 10 && min <= max) {
                return [min, max]; // Retorna el rango como una lista si es válido
            } else {
                DevExpress.ui.notify(
                    'El rango debe estar entre -30 y 10, y el valor mínimo debe ser menor o igual al máximo.',
                    'error',
                    3000
                );
                return null;
            }
        }

        // Si no coincide con el formato esperado
        DevExpress.ui.notify('Formato de entrada incorrecto. Use el formato num1-num2.', 'error', 3000);
        return null; // Retorna null si no encuentra un patrón válido
    }


    // Inicializar tooltips de Bootstrap
    //const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    //const tooltipList = [...tooltipTriggerList].map(tooltipTriggerEl => new bootstrap.Tooltip(tooltipTriggerEl));

    // Inicializar tooltips de Bootstrap
    const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    tooltipTriggerList.forEach(tooltipTriggerEl => {
        new bootstrap.Tooltip(tooltipTriggerEl);
    });

});