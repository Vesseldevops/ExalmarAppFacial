# Multi-Angle Identification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add guided multi-angle enrollment and identify a person from all locally registered profiles.

**Architecture:** Keep the current .NET MAUI app, YuNet detector, SFace recognizer, SQLite table, and AES-GCM storage boundary. Add versioned multi-vector templates inside the existing `Vector` blob, a person-level matcher, a guided enrollment page, and a simplified identification page.

**Tech Stack:** .NET MAUI 8, C#, ONNX Runtime, YuNet, SFace, SQLite, SecureStorage, AES-256-GCM.

## Global Constraints

- Do not require internet, server APIs, provider keys, login, attendance, or sync.
- Keep SQLite business table as `Personas(Id, NombreUsuario, Vector)`.
- Preserve v1 single-vector compatibility.
- New profiles use v2 multi-vector encrypted blob.
- Enrollment requires five guided captures: Frente, Gira a la izquierda, Gira a la derecha, Mira arriba, Mira abajo.
- First pass uses a `Guardar captura` action per step.
- Verification must not require profile selection.
- Identification winner must satisfy cosine threshold `0.363` and margin `0.03` over second-best person.
- Do not store photos; temporary capture files must be deleted after processing.
- No commits are required; this is local work.
- Current machine has .NET Runtime but no .NET SDK; build/test commands require SDK installation or a machine with SDK.

---

## File Structure

- Create `ExaTareo/CaptureAngle.cs`: defines guided capture steps.
- Create `ExaTareo/FaceTemplate.cs`: in-memory person template and capture vectors.
- Modify `ExaTareo/VectorCodec.cs`: add v2 multi-vector encrypt/decrypt and preserve v1 decrypt.
- Modify `ExaTareo/FaceMatcher.cs`: add 1:N identification logic with threshold and margin.
- Modify `ExaTareo/FaceRepository.AutomaticId.cs`: add multi-vector registration entry point.
- Modify `ExaTareo/Platforms/Android/FaceRepository.cs`: read/write `FaceTemplate`.
- Modify `ExaTareo/Platforms/Windows/FaceRepository.cs`: read/write `FaceTemplate`.
- Create `ExaTareo/EnrollmentCapturePage.cs`: guided five-step capture flow.
- Modify `ExaTareo/MainPage.cs`: use enrollment page and save multi-vector template.
- Modify `ExaTareo/VerificationPage.cs`: remove picker and identify against all templates.
- Modify `verification/Program.cs`: test v2 codec and person matcher.

---

### Task 1: Capture Template Types

**Files:**
- Create: `ExaTareo/CaptureAngle.cs`
- Create: `ExaTareo/FaceTemplate.cs`

**Interfaces:**
- Produces: `CaptureAngle`, `FaceCaptureVector`, `FaceTemplate`, `FaceTemplate.Create(id, name, captures)`.

- [ ] **Step 1: Create angle definitions**

Create `ExaTareo/CaptureAngle.cs`:

```csharp
namespace ExaTareo;

public enum CaptureAngle
{
    Front = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4
}

public static class CaptureAngles
{
    public static readonly IReadOnlyList<CaptureAngle> Required =
    [
        CaptureAngle.Front,
        CaptureAngle.Left,
        CaptureAngle.Right,
        CaptureAngle.Up,
        CaptureAngle.Down
    ];

    public static string Label(this CaptureAngle angle) => angle switch
    {
        CaptureAngle.Front => "Frente",
        CaptureAngle.Left => "Gira a la izquierda",
        CaptureAngle.Right => "Gira a la derecha",
        CaptureAngle.Up => "Mira arriba",
        CaptureAngle.Down => "Mira abajo",
        _ => "Captura"
    };
}
```

- [ ] **Step 2: Create template records**

Create `ExaTareo/FaceTemplate.cs`:

```csharp
namespace ExaTareo;

public sealed record FaceCaptureVector(CaptureAngle Angle, float[] Vector);

public sealed record FaceTemplate(string Id, string NombreUsuario, IReadOnlyList<FaceCaptureVector> Captures)
{
    public static FaceTemplate Create(string id, string name, IEnumerable<FaceCaptureVector> captures)
    {
        id = id.Trim();
        name = name.Trim();
        var list = captures.ToList();
        if (id.Length is < 1 or > 64) throw new InvalidOperationException("ID de perfil inválido.");
        if (name.Length is < 1 or > 100) throw new InvalidOperationException("Completa el nombre de usuario.");
        if (list.Count == 0) throw new InvalidOperationException("Captura al menos un rostro válido.");
        foreach (var capture in list) FaceEngine.Normalize(capture.Vector);
        return new FaceTemplate(id, name, list);
    }
}
```

- [ ] **Step 3: Build later with SDK**

Run when SDK exists:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\ExaTareo\ExaTareo.csproj -f net8.0-windows10.0.19041.0 -c Debug
```

Expected: compile success.

---

### Task 2: Versioned Multi-Vector Codec

**Files:**
- Modify: `ExaTareo/VectorCodec.cs`
- Modify: `verification/Program.cs`

**Interfaces:**
- Produces: `VectorCodec.EncryptTemplate(IReadOnlyList<FaceCaptureVector>, byte[], string, string)`
- Produces: `VectorCodec.DecryptTemplate(byte[], byte[], string, string)`
- Preserves: `VectorCodec.Encrypt(float[], byte[], string, string)`
- Preserves: `VectorCodec.Decrypt(byte[], byte[], string, string)`

- [ ] **Step 1: Add tests to verification**

Add v2 checks to `verification/Program.cs` after existing single-vector round trip:

```csharp
var captures = new[]
{
    new FaceCaptureVector(CaptureAngle.Front, vector),
    new FaceCaptureVector(CaptureAngle.Left, variation),
    new FaceCaptureVector(CaptureAngle.Right, repeat)
};
var templateBlob = VectorCodec.EncryptTemplate(captures, key, "001", "Prueba");
var decodedTemplate = VectorCodec.DecryptTemplate(templateBlob, key, "001", "Prueba");
Check(decodedTemplate.Count == 3, "Multi-vector encrypted template preserves capture count");
Check(decodedTemplate[0].Angle == CaptureAngle.Front, "Multi-vector encrypted template preserves angles");
Check(decodedTemplate[0].Vector.Length == 128, "Multi-vector encrypted template stores 128-value vectors");
Rejected(() => VectorCodec.DecryptTemplate(templateBlob, key, "002", "Prueba"), "Cannot swap multi-vector template to another ID");
Rejected(() => VectorCodec.EncryptTemplate(Array.Empty<FaceCaptureVector>(), key, "001", "Prueba"), "Empty multi-vector template rejected");
```

- [ ] **Step 2: Implement v2 codec**

In `VectorCodec.cs`, add v2 methods. Use format: version `2`, nonce 12, tag 16, ciphertext. Plaintext: int32 count, then byte angle + 512 vector bytes per capture.

- [ ] **Step 3: Verify compatibility**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\verification\Verification.csproj -- (Get-Location).Path
```

Expected: old v1 tests still pass plus new v2 tests.

---

### Task 3: 1:N Person Matcher

**Files:**
- Modify: `ExaTareo/FaceMatcher.cs`
- Modify: `verification/Program.cs`

**Interfaces:**
- Produces: `FaceIdentificationResult`
- Produces: `FaceMatcher.Identify(IReadOnlyList<FaceTemplate> templates, float[] candidate)`

- [ ] **Step 1: Add matcher tests**

Add to `verification/Program.cs`:

```csharp
var personA = FaceTemplate.Create("A", "Persona A", [new FaceCaptureVector(CaptureAngle.Front, vector)]);
var personB = FaceTemplate.Create("B", "Persona B", [new FaceCaptureVector(CaptureAngle.Front, orthogonalA)]);
var identified = FaceMatcher.Identify([personA, personB], repeat);
Check(identified.IsMatch && identified.Person!.Id == "A", "Identification selects best matching profile");
var noMatch = FaceMatcher.Identify([personB], repeat);
Check(!noMatch.IsMatch, "Identification rejects low score");
var ambiguousB = FaceTemplate.Create("C", "Persona C", [new FaceCaptureVector(CaptureAngle.Front, repeat)]);
var ambiguous = FaceMatcher.Identify([personA, ambiguousB], repeat);
Check(!ambiguous.IsMatch && ambiguous.IsAmbiguous, "Identification rejects ambiguous top-two scores");
```

- [ ] **Step 2: Implement identification result**

Add to `FaceMatcher.cs`:

```csharp
public sealed record FaceIdentificationResult(
    bool IsMatch,
    bool IsAmbiguous,
    RegisteredPerson? Person,
    double Score,
    double? RunnerUpScore);
```

- [ ] **Step 3: Implement identify logic**

In `FaceMatcher.cs`, add `public const double IdentificationMargin = 0.03;` and method comparing candidate against every vector of every template.

- [ ] **Step 4: Run verification**

Run SDK command above. Expected: all matcher tests pass.

---

### Task 4: Repository Multi-Vector Read/Write

**Files:**
- Modify: `ExaTareo/FaceRepository.AutomaticId.cs`
- Modify: `ExaTareo/Platforms/Android/FaceRepository.cs`
- Modify: `ExaTareo/Platforms/Windows/FaceRepository.cs`

**Interfaces:**
- Produces: `FaceRepository.RegisterAsync(string name, IReadOnlyList<FaceCaptureVector> captures)`
- Produces: `FaceRepository.ListTemplatesAsync()`
- Preserves: `FaceRepository.ListAsync()`
- Preserves: `FaceRepository.ReadVectorAsync(string id)`

- [ ] **Step 1: Add multi-vector registration**

In `FaceRepository.AutomaticId.cs`, overload `RegisterAsync` to accept capture list and call `SaveTemplateAsync`.

- [ ] **Step 2: Add platform save/read**

In each platform repository:

```csharp
internal async Task SaveTemplateAsync(string id, string name, IReadOnlyList<FaceCaptureVector> captures)
```

Use same validation, key retrieval, duplicate check, transaction, and read-back authentication as current `SaveAsync`, but call `VectorCodec.EncryptTemplate`.

- [ ] **Step 3: Add list templates**

In each platform repository:

```csharp
public async Task<List<FaceTemplate>> ListTemplatesAsync()
```

Read `Id`, `NombreUsuario`, `Vector`, decrypt with `VectorCodec.DecryptTemplate`, and return templates.

- [ ] **Step 4: Preserve v1 path**

Keep `ReadVectorAsync` using `VectorCodec.Decrypt` or route it through first capture from `DecryptTemplate`.

---

### Task 5: Guided Enrollment Page

**Files:**
- Create: `ExaTareo/EnrollmentCapturePage.cs`
- Modify: `ExaTareo/MainPage.cs`

**Interfaces:**
- Produces: `EnrollmentCapturePage.CaptureAsync(Page owner, string name)`
- Consumes: platform camera capture via `DesktopCamera.CaptureAsync(Window)` and `FaceCameraPage.CaptureAsync(Page)`.

- [ ] **Step 1: Create guided modal**

Create a `ContentPage` with current step title, progress `1/5`, guidance text, status label, `Guardar captura`, `Cancelar`, and list of completed steps.

- [ ] **Step 2: Capture current step**

On button click, open platform camera, extract vector using `EnrollmentService`, store `FaceCaptureVector(currentAngle, vector)`, advance to next step.

- [ ] **Step 3: Return captures**

When all five steps are complete, return `IReadOnlyList<FaceCaptureVector>` through `TaskCompletionSource`.

- [ ] **Step 4: Wire MainPage**

Replace single `pendingVector` with `pendingCaptures`. `CAPTURAR ROSTRO` launches enrollment page. `GUARDAR EN ESTE DISPOSITIVO` saves with `repository.RegisterAsync(username.Text, pendingCaptures)`.

---

### Task 6: Identification Page

**Files:**
- Modify: `ExaTareo/VerificationPage.cs`

**Interfaces:**
- Consumes: `FaceRepository.ListTemplatesAsync()`
- Consumes: `FaceMatcher.Identify(...)`

- [ ] **Step 1: Remove picker UI**

Delete `Picker profiles` and selected-index logic. Load templates in `OnAppearing`; enable `IDENTIFICAR` only if profiles exist.

- [ ] **Step 2: Identify against all profiles**

On button click, capture one photo, extract vector, call `FaceMatcher.Identify(templates, candidate)`.

- [ ] **Step 3: Success/failure states**

Success text: `Identificado: {NombreUsuario}` in green. Show `Siguiente` button to close. Failure text red; leave `IDENTIFICAR` enabled to retry.

---

### Task 7: Docs And Graph

**Files:**
- Modify: `README.md`
- Modify: `INICIAR-WINDOWS.md`
- Update generated: `graphify-out/*`

**Interfaces:**
- Produces: current docs explaining multi-angle enrollment and 1:N identification.

- [ ] **Step 1: Update docs**

Describe five-capture enrollment, multi-vector encrypted blob, automatic identification, threshold plus margin, and continued spoofing/liveness limitation.

- [ ] **Step 2: Update graphify**

Run:

```powershell
& 'C:\Users\USUARIO\AppData\Roaming\uv\tools\graphifyy\Scripts\python.exe' -m graphify update .
```

Expected: graph refreshed.

---

## Self-Review

- Spec coverage: enrollment UX, v2 storage, v1 compatibility, 1:N matching, error handling, tests, docs, and future auto-capture path all mapped to tasks.
- Placeholder scan: no TBD/TODO placeholders.
- Type consistency: `CaptureAngle`, `FaceCaptureVector`, `FaceTemplate`, `EncryptTemplate`, `DecryptTemplate`, `ListTemplatesAsync`, and `Identify` names remain consistent across tasks.
