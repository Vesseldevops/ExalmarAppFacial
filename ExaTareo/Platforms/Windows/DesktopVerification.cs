#if FACE_VERIFICATION
using Microsoft.Data.Sqlite;

namespace ExaTareo;

internal static class DesktopVerification
{
    private static bool started;
    public static async Task RunAsync()
    {
        if (started) return;
        started = true;
        var report = new List<string>();
        var output = Environment.GetEnvironmentVariable("EXALMAR_TEST_REPORT");
        if (string.IsNullOrEmpty(output)) return;
        string folder = Path.Combine(FileSystem.CacheDirectory, "desktop-verification");
        Directory.CreateDirectory(folder);
        string fixture = Path.Combine(folder, "face.jpg");
        const string keyName = "exalmar.desktop.verification.key";
        void Check(bool result, string label)
        { if (!result) throw new Exception(label); report.Add("PASS " + label); }
        try
        {
            await using (var input = await FileSystem.OpenAppPackageFileAsync("verification-face.jpg"))
            await using (var file = File.Create(fixture)) await input.CopyToAsync(file);
            var extraction = await new EnrollmentService().ExtractAsync(new FileResult(fixture));
            var vector = extraction.Vector ?? throw new Exception(extraction.Error);
            Check(vector.Length == 128, "Windows bitmap + EXIF + real ONNX vector");
            var repository = new FaceRepository(folder, keyName);
            var previous = await repository.ListAsync();
            if (previous.Count == 0)
            {
                await repository.SaveAsync("PC-001", "Prueba A", vector);
                await repository.SaveAsync("PC-002", "Prueba B", vector);
                report.Add("PASS two records saved to desktop SQLite");
            }
            else Check(previous.Count == 2, "Desktop records survive restart");
            Check((await new FaceRepository(folder, keyName).ListAsync()).Count == 2, "SQLite reopen retains records");
            bool rejected = false;
            try { await repository.SaveAsync("PC-001", "Changed", vector); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Duplicate ID rejected");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "rostros.db3") }.ToString());
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(Personas)";
            var columns = new List<string>();
            using (var reader = cmd.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
            Check(columns.SequenceEqual(new[] { "Id", "NombreUsuario", "Vector" }), "Exactly three columns");
            cmd.CommandText = "SELECT Vector FROM Personas WHERE Id='PC-001'";
            var key = Convert.FromBase64String((await SecureStorage.Default.GetAsync(keyName))!);
            var decoded = VectorCodec.Decrypt((byte[])cmd.ExecuteScalar()!, key, "PC-001", "Prueba A");
            Check(decoded.Length == 128, "Windows SecureStorage key decrypts stored vector");
            Array.Clear(key); Array.Clear(decoded); Array.Clear(vector);
            report.Add("COMPLETE");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
            await File.WriteAllLinesAsync(output, report);
        }
    }
}
#endif
