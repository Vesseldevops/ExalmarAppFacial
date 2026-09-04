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
        foreach (var capture in list)
        {
            var normalized = FaceEngine.Normalize(capture.Vector);
            Array.Clear(normalized);
        }
        return new FaceTemplate(id, name, list);
    }
}
