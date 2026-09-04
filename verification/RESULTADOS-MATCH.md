# Verificación de perfil — 30 de agosto de 2026

- Compilación normal Windows y Android: cero errores y advertencias.
- 23 comprobaciones del ejecutable Verification correctas. Incluyen extracción real con SFace, cifrado y descifrado, comparación de una imagen con variación de iluminación, coincidencia de la misma imagen y rechazo de vectores inválidos.
- La prueba negativa de similitud utiliza vectores ortogonales sintéticos para comprobar la matemática; no mide rechazos entre personas distintas.
- SQLite conserva las mismas tres columnas. La nueva lectura selecciona por ID con parámetros y autentica el vector cifrado con el ID y nombre almacenados. No se agregan escrituras en el flujo de verificación.
- Ejecutable Windows y APK actualizados en artifacts.
- No había un emulador o dispositivo conectado al finalizar (`adb devices` vacío). Queda pendiente la prueba interactiva del nuevo apartado y la comparación con personas reales en Windows y Android.
- No hay detección de presencia física ni protección frente a fotografías o videos. Umbral de referencia SFace 0.363 sin calibración local.
