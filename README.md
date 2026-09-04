# Exalmar · Registro facial local (primera etapa)

App .NET MAUI para **Android 8.0 o superior y Windows 10/11 x64**. Incluye registro facial local con cinco capturas guiadas e identificación contra todos los perfiles guardados en el dispositivo. No se implementaron asistencia, login, sincronización ni liveness. El HTML de `../demo` permanece intacto como referencia visual. Para escritorio, ver [INICIAR-WINDOWS.md](INICIAR-WINDOWS.md).

## Verificar mi identidad

Pulsa **VERIFICAR MI IDENTIDAD** y luego **IDENTIFICAR**. Ya no se selecciona un perfil manualmente: la app captura un rostro nuevo, genera su vector SFace y lo compara contra todos los perfiles registrados en SQLite. Si hay una coincidencia clara, muestra **Identificado: Nombre** en verde y permite cerrar con **Siguiente**. La captura de identificación no modifica el perfil ni se guarda en SQLite.

Se usa similitud coseno y el umbral inicial **0.363** del [ejemplo oficial de SFace/OpenCV](https://github.com/opencv/opencv_zoo/blob/main/models/face_recognition_sface/sface.py). Para identificación 1:N también se exige que el primer lugar supere al segundo por un margen inicial de **0.03**. La puntuación no es un porcentaje de certeza. Umbral y margen necesitan evaluación con personas y cámaras reales antes de uso operativo. La demo no verifica presencia física y puede aceptar fotos o videos; no debe usarse como control de acceso seguro.

## Uso

1. Instalar la APK de demostración en Android.
2. Ingresar el nombre de usuario; confirmar la autorización de la persona. El ID se genera automáticamente al guardar.
3. Pulsar **CAPTURAR ROSTRO**. Se abre un enrollment guiado de cinco pasos: frente, izquierda, derecha, arriba y abajo.
4. En cada paso, fotografiar una única persona con buena iluminación y pulsar **GUARDAR CAPTURA**. La cámara conserva el óvalo y el escáner visual. Android utiliza la cámara integrada y prefiere la frontal; Windows utiliza la webcam.
5. Después de completar las cinco capturas, pulsar **GUARDAR EN ESTE DISPOSITIVO**.

El botón Guardar solo se habilita tras procesar realmente las cinco imágenes. Cambiar el nombre invalida las capturas pendientes. Se admiten varios registros; cada registro nuevo recibe un UUID de 32 caracteres generado automáticamente, visible tras guardar y en la lista. Cada tarjeta de persona permite **Eliminar** el perfil con confirmación; esto borra su fila local de SQLite. No se modifican los ID ni los vectores existentes. Cancelar o fallar una captura no escribe en SQLite. La lista muestra únicamente personas ya guardadas.

El óvalo y la animación son guías visuales de encuadre: no indican que el rostro haya sido reconocido ni producen una validación automática por tiempo. Hay que pulsar Capturar; después se genera el vector con el modelo real. Cancelar cierra la vista previa y detiene el escáner y la cámara.

## Qué se guarda

Archivo `rostros.db3` dentro de `FileSystem.AppDataDirectory`, privado de la app. Una tabla de negocio, exactamente tres columnas:

| Columna | SQLite | Contenido |
| --- | --- | --- |
| Id | TEXT PRIMARY KEY | UUID automático de 32 caracteres para nuevos registros; conserva los ID anteriores |
| NombreUsuario | TEXT NOT NULL | Nombre ingresado (1–100 caracteres) |
| Vector | BLOB NOT NULL | Plantilla facial versionada, cifrada con AES-256-GCM |

El formato v1 anterior mide 541 bytes: versión (1), nonce aleatorio (12), etiqueta (16), vector cifrado (512). El formato v2 guarda varias capturas en un único BLOB cifrado: versión, nonce, etiqueta y plaintext cifrado con conteo, ángulo y vectores de 128 float32 normalizados. Los perfiles v1 siguen siendo legibles como una plantilla de una captura frontal. ID y nombre están autenticados como datos asociados: mover una plantilla a otro registro invalida su autenticación. **ID y nombre no están cifrados**; la protección adicional se aplica a los vectores. La clave se guarda aparte mediante MAUI SecureStorage, respaldado por Android Keystore. Las copias de seguridad de la app están deshabilitadas. Desinstalar o borrar los datos de la app elimina los registros y la clave.

El guardado usa una transacción y verifica que el vector leído de SQLite se pueda descifrar antes de confirmar. No se registran vectores ni fotografías en logs. Las capturas se escriben temporalmente en la caché privada y se eliminan tras procesarlas; no se guardan en la galería. La copia temporal adicional de procesamiento en Android también se elimina al terminar.

## Generación real del vector

- **YuNet 2023mar** detecta el rostro y cinco puntos faciales, con ONNX Runtime en CPU.
- Se rechazan cero/múltiples rostros, rostros demasiado pequeños o recortados, y alineaciones deficientes.
- Los rechazos normales de captura devuelven un aviso para repetir, sin lanzar excepciones: rostro pequeño → acercarse; rostro recortado → alejarse y centrarse. No se genera un vector ni se habilita Guardar mientras la captura sea inválida.
- Se alinea la imagen a 112 × 112 usando una transformación de similitud de cinco puntos.
- **SFace 2021dec** genera 128 valores; se aplica normalización L2 antes del cifrado.
- Los dos modelos se incluyen en la APK. No se requieren API, servidor, claves de proveedor ni conexión para registrarse.

No hay vectores aleatorios, simulación de identificación ni sustitutos basados en píxeles. Se conserva el mismo modelo/preprocesamiento para futuras comparaciones. Cambiar de modelo exige migración o volver a registrar los rostros.

Esta etapa usa cinco capturas guiadas por persona. **No valida presencia física ni evita registrar fotos o videos de alguien.** Tampoco impide registrar el mismo rostro con otro ID; la deduplicación biométrica, la captura automática por pose y la calibración de umbrales quedan fuera de esta etapa. Antes de uso real se requiere probar precisión, consentimiento y controles de acceso en los equipos previstos. .NET 8 se mantuvo por compatibilidad con el entorno instalado; antes de producción debe planificarse la actualización a una versión soportada.

## Compilación

Abrir `ExaTareo.sln` en Visual Studio con MAUI y Android, o ejecutar:

```powershell
dotnet restore .\ExaTareo\ExaTareo.csproj
dotnet build .\ExaTareo\ExaTareo.csproj -f net8.0-android -c Debug -t:Rebuild
```

La APK incluye los assemblies (`EmbedAssembliesIntoApk=true`) y se puede instalar sin Visual Studio. La firma de depuración es solo para pruebas, no para publicar en una tienda.

Windows usa WinUI/MAUI sin empaquetado MSIX, una vista previa de webcam con MediaCapture y SQLite mediante Microsoft.Data.Sqlite. Mantiene el mismo esquema y cifrado AES-GCM; la clave usa SecureStorage de Windows. La base del PC es independiente de la del Android. La compilación de escritorio incluye el runtime de Windows App SDK; en este PC usa el runtime .NET 8 instalado. Conserva los archivos acompañantes del ejecutable.

## Verificación

`verification/Verification.csproj` ejecuta el mismo `FaceEngine` y `VectorCodec` en Windows: inferencia real, repetibilidad, cambio leve de iluminación, rechazo de cero/dos rostros, dimensión y normalización, cifrado/descifrado, nonce aleatorio, ID/nombre incorrectos, clave incorrecta, manipulación y vectores inválidos.

```powershell
dotnet run --project .\verification\Verification.csproj -- (Get-Location).Path
```

La imagen pública de prueba se encuentra en `verification/fixtures/face.jpg` (muestra `lena.jpg` de OpenCV). No se empaqueta en la demo normal. La variante `-p:FaceVerification=true` usa otro identificador de aplicación (`pe.com.exalmar.exatareo.verification`), ejecuta pruebas reales de inferencia Android y persistencia SQLite y escribe `verification.txt` en su directorio privado. No habilitar esa propiedad al generar la demo entregable.

Usar `-t:Rebuild` al cambiar entre la variante de verificación y la demo: el empaquetado incremental de MAUI puede conservar assets antiguos. La APK entregada se reconstruyó y se inspeccionó para confirmar que no contiene la imagen de prueba.

## Modelos y licencias

Fuentes: [OpenCV Zoo YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet), [OpenCV Zoo SFace](https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface), [ONNX Runtime móvil](https://onnxruntime.ai/docs/tutorials/mobile/). Licencias de los modelos en `licenses/`. La decodificación y preprocesamiento siguen los formatos documentados por OpenCV.

SHA-256 de los archivos incluidos:

```text
YuNet: 8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4
SFace: 0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79
```
