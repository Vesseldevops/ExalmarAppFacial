# Multi-Angle Enrollment And 1:N Identification Design

Date: 2026-08-31

## Goal

Improve the facial-recognition POC so enrollment captures several guided face angles and verification identifies the person automatically from the local database, without asking the user to select a profile first.

This remains a local POC. It does not add server sync, attendance, login, or liveness protection. Photos and videos can still spoof the system unless a future liveness layer is added.

## Current State

The app is a .NET MAUI project for Android and Windows. Registration currently captures one photo, extracts one 128-float SFace vector through YuNet face detection and alignment, encrypts that vector with AES-256-GCM, and stores it in SQLite as `Personas(Id, NombreUsuario, Vector)`.

Verification currently asks the user to select a profile, captures one new photo, extracts a vector, and compares it only against the selected profile with cosine similarity threshold `0.363`.

## Proposed UX

Enrollment starts from the existing `CAPTURAR ROSTRO` button. Instead of a single capture, the user enters a guided capture flow with five required steps:

1. Frente
2. Gira a la izquierda
3. Gira a la derecha
4. Mira arriba
5. Mira abajo

The first implementation uses a clear `Guardar captura` action per step. This keeps the POC reliable with the current camera abstractions. The UI copy and capture-service boundary will be shaped so a later automatic capture mode can replace the button once a stable frame/pose loop exists.

After all required captures succeed, the app saves a single person profile with a multi-vector biometric template. Each step must produce a valid YuNet/SFace vector. Failed captures show retry guidance and do not advance.

Identification starts from `VERIFICAR MI IDENTIDAD`. The page no longer shows a profile picker. The main button label becomes `IDENTIFICAR`. The app captures one new face, extracts its vector, compares it against every registered person, and shows:

- Green success: `Identificado: {NombreUsuario}`
- Red failure: no confident match
- `Siguiente` closes the screen after success

## Data Model

Keep the existing SQLite table with exactly three business columns:

`Personas(Id TEXT PRIMARY KEY, NombreUsuario TEXT NOT NULL, Vector BLOB NOT NULL)`

The `Vector` blob becomes a versioned encrypted payload:

- v1 existing format: one 128-float vector, 541 bytes
- v2 new format: multiple 128-float vectors in one encrypted payload

v2 plaintext layout:

- capture count
- per-capture angle id
- per-capture 128 normalized float32 vector

v2 encrypted blob layout:

- version byte
- nonce
- authentication tag
- encrypted plaintext

The AES-GCM associated data remains bound to model/version, ID, and name so moving a vector blob to a different profile fails authentication. Existing v1 rows remain readable and matchable as one-vector profiles.

## Matching

Introduce a person-level matcher:

- For each registered person, decrypt all stored vectors.
- Compare the candidate vector against every stored vector.
- Person score is the best cosine similarity across that person vectors.
- Winner must satisfy `score >= 0.363`.
- Winner must also beat the second-best person by a margin, initially `0.03`.

This avoids selecting a profile and reduces false positives when two people score similarly. The constants remain POC defaults and require calibration with real users, cameras, and lighting.

## Components

`FaceTemplate`

Represents one person's biometric template in memory:

- ID
- name
- one or more captures
- capture angle metadata

`CaptureAngle`

Defines guided enrollment steps and labels.

`VectorCodec`

Adds v2 encrypt/decrypt while preserving v1 decrypt.

`FaceRepository`

Adds methods to register a multi-vector template and list templates with vectors for identification. Android and Windows implementations keep platform-specific SQLite APIs but share blob format through `VectorCodec`.

`EnrollmentCapturePage`

New MAUI page or modal flow for guided five-angle enrollment. Uses existing platform camera capture path first. Later it can adopt automatic capture without changing storage or matching.

`VerificationPage`

Removes picker, changes button to `IDENTIFICAR`, performs 1:N identification, and shows result with close action.

## Error Handling

Enrollment:

- Invalid face, no face, multiple faces, small face, clipped face, or poor alignment stays on current step.
- Canceling discards unsaved captures.
- Saving occurs only after all required captures have valid vectors.

Identification:

- No registered profiles shows instruction to enroll first.
- No confident match shows retry message.
- Ambiguous match shows retry message instead of naming a person.
- Temporary photos are deleted after processing.

## Testing

Extend the existing verification console tests:

- v1 encrypted blob still decrypts.
- v2 encrypted blob round trips multiple vectors.
- v2 rejects tampering, wrong key, wrong ID, wrong name, invalid vector count, and invalid dimensions.
- Person matcher returns best matching profile.
- Person matcher rejects low score.
- Person matcher rejects ambiguous top-two scores.

Manual verification:

- Android enrollment wizard completes five steps.
- Windows enrollment wizard completes five steps.
- Identification works without selecting a profile.
- Canceling capture does not write SQLite.

## Out Of Scope

- True liveness detection
- Server sync
- Attendance/check-in
- Role-based access
- Biometric deduplication during enrollment
- Automatic pose-triggered capture in first pass

## Future Auto-Capture Path

Auto-capture can be added after this design by introducing a platform-specific live-frame provider:

- throttle analysis to 2-4 FPS
- run YuNet detection on background thread
- infer approximate pose from landmarks
- require stable acceptable pose for 700-1000 ms
- capture automatically and advance step

The storage, matching, and UI state model above are compatible with that future change.
