# Escritorio Windows — 30 de agosto de 2026

- Compilación completa Windows: 0 errores y 0 advertencias.
- Compilación del código Android después de habilitar Windows: 0 errores y 0 advertencias.
- Motor de vectores real ejecutado dentro de la aplicación Windows, con BitmapDecoder y corrección EXIF.
- SQLite: dos registros de prueba, tres columnas exactas, ID duplicado rechazado, lectura después de cerrar/reabrir la conexión.
- SecureStorage de Windows: recuperación de la clave y descifrado autenticado del vector guardado.
- Se reinició el proceso y se confirmó la persistencia de registros y clave.
- Resultados: `windows-first-run.txt` y `windows-restart.txt`.
- Los datos de prueba usan un directorio y una clave separados del registro normal. La compilación final excluye el ejecutor de pruebas y la imagen de muestra.
- La webcam física no se activó en las pruebas. La captura usa MediaCapture y una vista previa WinUI; requiere validación con el dispositivo del usuario.

Ejecutable normal: `../artifacts/Windows/ExaTareo.exe` (conservar archivos acompañantes).
