# Verificación — 30 de agosto de 2026

- Compilación Android: 0 errores, 0 advertencias.
- Motor compartido en Windows: 18 comprobaciones correctas; ver `desktop-results.txt`.
- Emulador Pixel 5 / Android API 34, x86_64: ejecución real de YuNet + SFace mediante ONNX Runtime, usando el decodificador Android y el tratamiento EXIF.
- SQLite Android: dos registros, exactamente tres columnas, rechazo de ID duplicado, lectura/descifrado con SecureStorage y eliminación de la copia temporal de procesamiento. Ver `android-first-run.txt`.
- Se detuvo y volvió a iniciar el proceso: registros y clave conservados, vectores descifrables. Ver `android-restart.txt`.
- Demo normal: arranque correcto y revisión visual (`android-screen.png`). Se comprobó permiso de cámara, apertura de la cámara Android y cancelación. Al cancelar, aparece el mensaje correspondiente y Guardar está deshabilitado (`android-camera-cancel.xml`).
- APK final reconstruida; no incluye assets de verificación. Incluye los modelos y las bibliotecas ONNX para ARM64, ARMv7, x86 y x86_64.

No se probó con una cámara de celular físico ni se evaluó precisión de identificación entre personas. Esa identificación, la asistencia y la validación de presencia física no forman parte de esta etapa. Las comprobaciones con dos ID validan almacenamiento de múltiples registros, no una comparación biométrica entre dos personas distintas.

Entregable: `../artifacts/Exalmar-RegistroFacial-demo.apk`, firma de depuración para pruebas.

SHA-256:
`7355a1c556d25108d12424bab5c62846522810f8d53b709dc5c7c224f431950e`




