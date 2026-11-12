# Sensor Panel

Panel de sensores para Windows en tiempo real, inspirado en el SensorPanel de AIDA64:
CPU, GPU, discos, temperaturas, uso, consumo, ventilador, FPS y reloj, sin necesitar AIDA64.

- **Sensores**: [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (`LibreHardwareMonitorLib`).
- **FPS**: se leen de la memoria compartida de RivaTuner Statistics Server (RTSS), igual que AIDA64. Si RTSS no está abierto, se muestra 0.
- **Interfaz**: WPF (.NET 8), diseñada a 1024×600 y escalada a cualquier tamaño de pantalla.

## Requisitos

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (o el SDK 8 para compilar)
- [PawnIO](https://pawnio.eu/): el driver que LibreHardwareMonitor usa para leer CPU y placa base. Sin él, la temperatura, el reloj y el consumo de la CPU y los ventiladores salen a 0.
- Permisos de administrador: la app se relanza elevada ella sola (aviso UAC). Si cancelas, funciona igualmente, pero solo con los sensores de GPU, RAM y carga de CPU.

## Compilar y ejecutar

```bash
dotnet build SensorPanel -c Release
```

El ejecutable queda en `SensorPanel/bin/Release/net8.0-windows/SensorPanel.exe`.

## Uso

Haz clic derecho en el panel para abrir el menú:

| Opción | Qué hace |
|---|---|
| Monitor | Pantalla completa en el monitor que elijas, o *Ventana normal* (se mueve arrastrando y se redimensiona desde la esquina) |
| Siempre visible | Mantiene el panel por encima de las demás ventanas |
| Iniciar con Windows | Crea una tarea programada que abre el panel al iniciar sesión, ya elevado y sin aviso UAC |
| Exportar lista de sensores | Genera `sensores.txt` con todos los sensores detectados y sus identificadores |
| Abrir config.json / Recargar | Edita la configuración y la aplica sin reiniciar |

## Configuración (`config.json`, junto al .exe)

Se crea la primera vez que se abre la app.

- `monitor`: índice de la pantalla (0, 1, …) o `-1` para usar una ventana normal.
- `cpuTitle`, `gpuTitle`: textos de cabecera; si están vacíos, se usa el nombre del hardware.
- `cpuImage`, `gpuImage`: ruta a un PNG que sustituye al icono genérico (por ejemplo, el logo de tu marca).
- `drives`: unidades que se muestran (hasta 3). La temperatura se toma automáticamente del disco físico que contiene cada unidad. Para usar otro sensor, añádelo en `driveTempSensors`.
- `gpuTdpWatts`: solo se usa si la gráfica no informa del porcentaje de límite de potencia.
- `sensors`: qué sensor alimenta cada recuadro. Cada selector admite:
  - `id`: identificador exacto (sale en `sensores.txt`, por ejemplo `/lpc/it8688e/0/temperature/1`). Si lo pones, tiene prioridad sobre lo demás.
  - `hardware`, `device`, `type`, `name`: patrones con comodines `*` y `?`. En `name`, separa alternativas con `|`: se usa la primera que encuentre algo.
  - `agg`: `first`, `max`, `min`, `avg` o `sum`, cuando varios sensores coinciden.

### Temperatura del líquido

LibreHardwareMonitor solo da una temperatura de líquido si la AIO la expone (algunos modelos de Corsair o NZXT) o si tienes una sonda conectada a la placa. Por defecto se usa `External #1` (la entrada de sonda de las placas Gigabyte) si no hay un sensor de líquido. Si tu sonda está en otra entrada, búscala en `sensores.txt` y ponla en `sensors.liquidTemp.id`.

## Estructura

| Archivo | Contenido |
|---|---|
| `SensorService.cs` | Abre LibreHardwareMonitor, resuelve los selectores y asocia unidades con discos físicos |
| `RtssReader.cs` | Lee los FPS de la aplicación en primer plano desde RTSS |
| `PanelConfig.cs` | Configuración y selectores por defecto |
| `MainWindow.xaml(.cs)` | Diseño del panel, fondo, bucle de actualización y menú |
| `Controls.cs` | Control `Tile` (recuadro con muesca) y `Sparkline` (gráfica de historial) |
| `Autostart.cs` | Tarea programada para iniciar con Windows |
