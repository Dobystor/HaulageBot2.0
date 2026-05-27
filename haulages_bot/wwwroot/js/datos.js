$(document).ready(() => {
    
    document.getElementById('fileTest').addEventListener('change', function (e) {
        console.log("Archivo seleccionado:", e.target.files[0].name);
    });


    let grid = $('#gridContainer').dxDataGrid({
        dataSource: [],
        /*keyExpr: 'id',*/
        width: 1000, // Fija un ancho total que sea suficiente para el contenido, esto puede ajustarse según sea necesario
        height: 400,
        showBorders: true,
        columnAutoWidth: false,
        allowColumnResizing: false,
        scrolling: {
            mode: 'virtual', // Usa el modo virtual para agregar un scroll horizontal interno solo para la tabla
            useNative: true,
        },
        columns: [
            { dataField: 'vehicle', caption: 'Vehículo', width: 150 },
            { dataField: 'employee', caption: 'Empleado', width: 200 },
            { dataField: 'workshiftName', caption: 'Turno', width: 100 },
            { dataField: 'employeeCompanyName', caption: 'Compañía del empleado', width: 150 },
            { dataField: 'vehicleCompanyName', caption: 'Compañía del vehículo', width: 150 },
            { dataField: 'operationTime', caption: 'Duración', width: 100 },
            { dataField: 'materialTypeName', caption: 'Tipo de material', width: 130 },
            { dataField: 'tonsTransported', caption: 'Toneladas transportadas', width: 120 },
            { dataField: 'unloadDate', caption: 'Fecha de descarga', width: 150 },
            { dataField: 'tareDate', caption: 'Fecha de tara', width: 150 },
            { dataField: 'loadPointName', caption: 'Sitio de carga', width: 120 },
            { dataField: 'unloadPointName', caption: 'Sitio de descarga', width: 120 },
            { dataField: 'comments', caption: 'Comentarios', width: 200 },
            { dataField: 'userRegister', caption: 'Usuario que registró', width: 150 },
            { dataField: 'modifiedDate', caption: 'Fecha de modificación', width: 150 },
            { dataField: 'weighingType', caption: 'Tipo de pesaje', width: 130 },
            { dataField: 'weightType', caption: 'Tipo de peso', width: 130 },
        ],
        //export: {
        //    enabled: true,
        //},
        onExporting: function (e) {
            console.log(e);
        },
        onFileSaving: async e => {
            e.cancel = true;
            await exportToExcel(e, 'Acarreos', 'Acarreos', '', '');
            console.log('exportToExcel 444');
        },
        onExported: e => {
            console.log('exportToExcel 1111');
            if (globalExportPromiseResolved) {
                console.log('%c new global excel export promise', 'background: red; color: white');
                globalExportPromise = new Promise((resolve, reject) => {
                    globalExportPromiseResolved = false;
                    globalExportPromiseResolve = () => {
                        globalExportPromiseResolved = true;
                        console.log('%c ------------------- global excel promise resolved', 'background: red; color: white');
                        resolve('globalExportPromiseResolved');
                    };
                });
            }
        },
        onSelectionChanged(e) {
            console.log(e.selectedRowsData);
            datagridSelectedData();
        }
    }).dxDataGrid('instance');

    function datagridSelectedData() {
        $('#selectedData').html('');
        let arreglo = _dataGridInstance.getSelectedRowsData();
        arreglo.forEach(row => {
            $('#selectedData').append(`<span>${row.Address}</span>`)
        })
    }

    function importFile() {
        try {
            const input = document.getElementById('fileTest');

            // Verificar si se seleccionó un archivo
            if (input.files.length === 0) {
                alert("No se seleccionó ningún archivo. Por favor, elija un archivo para importar.");
                return;
            }

            const file = input.files[0];

            // Verificar que el archivo sea del tipo .xlsx
            if (!file.name.endsWith('.xlsx')) {
                alert("Tipo de archivo no válido. Por favor, seleccione un archivo .xlsx.");
                return;
            }

            const wb = new ExcelJS.Workbook();

            wb.xlsx.load(file).then(() => {
                const sheet = wb.worksheets[0];
                const dataFromExcel = [];

                // Procesar las filas desde la fila 6
                sheet.eachRow({ includeEmpty: false }, (row, rowNumber) => {
                    if (rowNumber >= 6) { // Saltar filas antes de la fila 6
                        // Construir el objeto con nombres PascalCase
                        const nuevoHaulage = {
                            vehicle: row.getCell(1)?.value || '', // Vehículo
                            employee: row.getCell(2)?.value || '', // Empleado
                            workshiftName: row.getCell(3)?.value || '', // Turno
                            employeeCompanyName: row.getCell(4)?.value || '', // Compañía del empleado
                            vehicleCompanyName: row.getCell(5)?.value || '', // Compañía del vehículo
                            operationTime: parseFloat(row.getCell(6)?.value) || 0, // Tiempo de operación
                            materialTypeName: row.getCell(7)?.value || '', // Tipo de material
                            tonsTransported: parseFloat(row.getCell(8)?.value) || 0, // Toneladas transportadas
                            unloadDate: parseExcelDate(row.getCell(9)?.value) || null, // Fecha de descarga
                            tareDate: parseExcelDate(row.getCell(10)?.value) || null, // Fecha de tara
                            loadPointName: row.getCell(11)?.value || '', // Sitio de carga
                            unloadPointName: row.getCell(12)?.value || '', // Sitio de descarga
                            comments: row.getCell(13)?.value || '', // Comentarios
                            userRegister: row.getCell(14)?.value || '', // Usuario que registró
                            modifiedDate: parseExcelDate(row.getCell(15)?.value) || null, // Fecha de modificación
                            weighingType: row.getCell(16)?.value || '', // Tipo de pesaje
                            weightType: row.getCell(17)?.value || '' // Tipo de peso
                        };

                        dataFromExcel.push(nuevoHaulage);
                        console.log("Datos enviados al backend:", JSON.stringify(dataFromExcel, null, 2));
                    }
                });

                // Validación de contenido del archivo
                if (dataFromExcel.length === 0) {
                    alert("El archivo no contiene datos válidos a partir de la fila 6. Por favor, revise el archivo e intente de nuevo.");
                    return;
                }

                // Mostrar confirmación antes de enviar los datos al backend
                const userConfirmed = confirm(`Se van a importar ${dataFromExcel.length} registros. ¿Desea continuar?`);
                if (!userConfirmed) {
                    alert("Importación cancelada por el usuario.");
                    return;
                }

                // Mostrar pantalla de carga
                showLoadingScreen();

                // Actualizar el DataGrid con los datos importados
                grid.option("dataSource", dataFromExcel);

                /* alert("File imported successfully.");*/
                console.log("Data imported successfully:", dataFromExcel);

                // Enviar los datos procesados al servidor
                saveHistoricData(dataFromExcel);
            }).catch(err => {
                console.error("Error reading the file:", err.message);
                alert("Ocurrió un error al procesar el archivo. Por favor, asegúrese de que el archivo esté en el formato correcto.");
            });
        } catch (error) {
            console.error("Unexpected error in the import function:", error);
            alert("Ocurrió un error inesperado. Por favor, revise el archivo e intente de nuevo.");

        }
    }

    // Función para convertir fechas de Excel a JavaScript
    function parseExcelDate(excelDate) {
        if (!excelDate) return null;
        if (typeof excelDate === 'object' && excelDate instanceof Date) return excelDate.toISOString();
        const timestamp = (excelDate - 25569) * 86400 * 1000; // Convertir formato Excel a UNIX timestamp
        return new Date(timestamp).toISOString();
    }


    document.getElementById("boton").addEventListener("click", function (e) {
        importFile();
    })

    // Función para enviar los datos al backend
    function saveHistoricData(data) {
        $.ajax({
            url: "/api/Historics/import-historic-data",
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(data),
            success: function (response) {
                alert(response.message || "¡Datos guardados exitosamente!");
                // Ocultar pantalla de carga
                hideLoadingScreen();
            },
            error: function (xhr) {
                console.error("Error saving data:", xhr.status, xhr.statusText, xhr.responseJSON);
                alert(`Error ${xhr.status}: ${xhr.responseText || "Ocurrió un error al guardar los datos."}`);
                // Ocultar pantalla de carga
                hideLoadingScreen();

            }
        });
    }

    // Función para mostrar la pantalla de carga
    function showLoadingScreen() {
        $("#loading-screen").removeClass("d-none");
    }

    // Función para ocultar la pantalla de carga
    function hideLoadingScreen() {
        $("#loading-screen").addClass("d-none");
    }

});


