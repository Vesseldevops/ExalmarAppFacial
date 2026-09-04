# Ejecutar como programa de escritorio

**Sin abrir Visual Studio:** ejecuta `artifacts\Windows\ExaTareo.exe`. Conserva toda la carpeta Windows; el EXE necesita las DLL y los modelos que lo acompañan. Esta compilación x64 usa el runtime .NET 8 ya instalado en este PC.

En Visual Studio:

1. Abre `ExaTareo.sln` y acepta recargar el proyecto si ya estaba abierto.
2. En el Explorador de soluciones, clic derecho sobre **ExaTareo** → **Establecer como proyecto de inicio**.
3. En el desplegable junto al botón verde, selecciona **Windows Machine** / **Equipo Windows** (destino `net8.0-windows10.0.19041.0`).
4. Presiona **F5** o **Ctrl+F5**.

El proyecto incluye Android y Windows. El perfil Windows usa `commandName: Project` y `WindowsPackageType: None`, por lo que no necesita instalar un MSIX ni abrir un emulador.

También se puede compilar desde esta carpeta:

```powershell
dotnet build .\ExaTareo\ExaTareo.csproj -f net8.0-windows10.0.19041.0 -c Debug
```

Para capturar, conecta una webcam y permite que las aplicaciones de escritorio usen la cámara en **Configuración → Privacidad y seguridad → Cámara**. El botón **CAPTURAR ROSTRO** abre un enrollment guiado de cinco pasos: frente, izquierda, derecha, arriba y abajo. En cada paso, abre la vista previa, elige **Capturar** o **Cancelar**, y la app validará el rostro antes de avanzar. Si hay varias cámaras, se usa la frontal identificada por Windows o, en su defecto, la primera disponible.

La vista previa incluye el óvalo verde y el escáner animado del HTML. Son una guía de encuadre; la validación facial real ocurre después de pulsar Capturar. Esta versión no detecta automáticamente la inclinación del rostro todavía; la persona sigue las instrucciones de cada paso.

Los datos se guardan localmente en Windows, separados de la base del Android. La tabla sigue teniendo únicamente ID, nombre de usuario y plantilla facial cifrada. Cada perfil puede eliminarse desde su tarjeta con confirmación. **VERIFICAR MI IDENTIDAD** abre identificación 1:N: ya no se selecciona perfil, la app busca automáticamente entre los registros del dispositivo. No hay sincronización entre ambos dispositivos.

Verificado en este PC: arranque del proceso de escritorio, generación real del vector con una imagen de prueba, almacenamiento SQLite, rechazo de ID duplicado, descifrado mediante SecureStorage y persistencia después de reiniciar. La webcam física no se activó durante las pruebas automáticas; debes probar la captura con tu cámara.
