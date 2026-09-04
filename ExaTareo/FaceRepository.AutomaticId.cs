namespace ExaTareo;

public sealed partial class FaceRepository
{
    public async Task<string> RegisterAsync(string name, float[] vector)
    {
        // Keep the existing TEXT primary key and authenticated encryption format.
        // Existing IDs/vectors require no migration or re-enrollment.
        string id = Guid.NewGuid().ToString("N");
        await SaveAsync(id, name, vector);
        return id;
    }

    public async Task<string> RegisterAsync(string name, IReadOnlyList<FaceCaptureVector> captures)
    {
        string id = Guid.NewGuid().ToString("N");
        await SaveTemplateAsync(id, name, captures);
        return id;
    }
}
