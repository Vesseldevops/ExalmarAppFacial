#if FACE_VERIFICATION
using Android.Database.Sqlite;

namespace ExaTareo;

// Only compiled into the separate .verification package; never into the demo APK.
internal static class AndroidVerification
{
    private static bool started;
    public static async Task RunAsync()
    {
        if (started) return;
        started = true;
        var report = new List<string>();
        string fixture = Path.Combine(FileSystem.CacheDirectory, "verification-face.jpg");
        void Check(bool result, string name)
        {
            if (!result) throw new Exception(name);
            report.Add("PASS " + name);
        }
        try
        {
            await using (var input = await FileSystem.OpenAppPackageFileAsync("verification-face.jpg"))
            await using (var output = File.Create(fixture)) await input.CopyToAsync(output);
            var service = new EnrollmentService();
            var extraction = await service.ExtractAsync(new FileResult(fixture));
            var vector = extraction.Vector ?? throw new Exception(extraction.Error);
            Check(vector.Length == 128, "Android ONNX + bitmap + EXIF: real vector");
            var repository = new FaceRepository();
            var before = await repository.ListAsync();
            if (before.Count == 0)
            {
                await repository.SaveAsync("TEST-001", "Test local A", vector);
                await repository.SaveAsync("TEST-002", "Test local B", vector);
                report.Add("PASS two independent IDs saved with real vectors");
            }
            else Check(before.Count == 2, "Records survive process restart");
            var rows = await new FaceRepository().ListAsync();
            Check(rows.Count == 2 && rows.Any(p => p.Id == "TEST-001") && rows.Any(p => p.Id == "TEST-002"), "SQLite reopen retains both records");
            bool duplicate = false;
            try { await repository.SaveAsync("TEST-001", "Changed", vector); }
            catch (InvalidOperationException) { duplicate = true; }
            Check(duplicate, "Duplicate ID rejected without replacement");
            using var db = SQLiteDatabase.OpenDatabase(Path.Combine(FileSystem.AppDataDirectory, "rostros.db3"), null, DatabaseOpenFlags.OpenReadonly)!;
            using var columns = db.RawQuery("PRAGMA table_info(Personas)", null)!;
            var names = new List<string>();
            while (columns.MoveToNext()) names.Add(columns.GetString(1)!);
            Check(names.SequenceEqual(new[] { "Id", "NombreUsuario", "Vector" }), "Exactly three columns");
            using var stored = db.RawQuery("SELECT Id, NombreUsuario, Vector, typeof(Vector) FROM Personas ORDER BY Id", null)!;
            var key = Convert.FromBase64String((await SecureStorage.Default.GetAsync("exalmar.face-vector.aes.v1"))!);
            while (stored.MoveToNext())
            {
                var decoded = VectorCodec.Decrypt(stored.GetBlob(2)!, key, stored.GetString(0)!, stored.GetString(1)!);
                Check(stored.GetString(3) == "blob" && decoded.Length == 128, "Stored blob decrypts using Android SecureStorage key");
                Array.Clear(decoded);
            }
            Array.Clear(vector); Array.Clear(key);
            Check(!Directory.EnumerateFiles(FileSystem.CacheDirectory, "enroll-*.jpg").Any(), "Processing photos removed from cache");
            report.Add("COMPLETE");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
            await File.WriteAllLinesAsync(Path.Combine(FileSystem.AppDataDirectory, "verification.txt"), report);
        }
    }
}
#endif
