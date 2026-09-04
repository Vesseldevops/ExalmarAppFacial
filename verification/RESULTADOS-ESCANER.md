# Cámara con guía visual — 30 de agosto de 2026

- Windows y Android compilados sin errores ni advertencias.
- Android API 34: vista previa integrada, óvalo verde, fondo atenuado y línea animada comprobados visualmente. Las dos capturas `scanner-camera.png` y `scanner-camera-second.png` muestran distintas posiciones de la línea.
- Se utilizó exclusivamente la cámara sintética del emulador; no se activó una cámara física.
- Capturar cerró la cámara y procesó la fotografía con el motor real. La escena sintética produjo correctamente «No se detectó un rostro», con Guardar deshabilitado y Capturar habilitado para reintentar (`scanner-after-capture.xml`).
- Se volvió a abrir la cámara y se canceló: regreso al formulario, mensaje de cancelación y Guardar deshabilitado (`scanner-after-cancel.xml`). No se guardaron personas durante esta prueba.
- Las 18 comprobaciones existentes del motor facial y cifrado se ejecutaron nuevamente y pasaron.
- Ejecutable Windows y APK en `artifacts` actualizados. La cámara física de Windows y la de un celular real quedan pendientes de prueba en los dispositivos del usuario.

El óvalo y la línea son ayudas visuales; no indican reconocimiento exitoso ni presencia física verificada. No se incorporan a la foto analizada.
