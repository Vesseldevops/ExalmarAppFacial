using Android.Content;
using Android.Database.Sqlite;
using System.Security.Cryptography;

namespace ExaTareo;

public sealed partial class FaceRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const string KeyName = "exalmar.face-vector.aes.v1";
    private readonly string path = Path.Combine(FileSystem.AppDataDirectory, "rostros.db3");

    private SQLiteDatabase Open()
    {
        var db = SQLiteDatabase.OpenOrCreateDatabase(path, null)!;
        EnsureSchema(db);
        return db;
    }

    private static void EnsureSchema(SQLiteDatabase db)
    {
        db.ExecSQL(CurrentSchemaSql);
        using var cursor = db.RawQuery("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Personas'", null)!;
        if (!cursor.MoveToFirst()) return;
        var schema = cursor.GetString(0);
        if (schema is null || !schema.Contains("length(Vector)=541", StringComparison.OrdinalIgnoreCase)) return;
        db.BeginTransaction();
        try
        {
            db.ExecSQL("ALTER TABLE Personas RENAME TO Personas_old");
            db.ExecSQL(CurrentSchemaSql);
            db.ExecSQL("INSERT INTO Personas (Id, NombreUsuario, Vector) SELECT Id, NombreUsuario, Vector FROM Personas_old");
            db.ExecSQL("DROP TABLE Personas_old");
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }

    private const string CurrentSchemaSql =
        "CREATE TABLE IF NOT EXISTS Personas (Id TEXT NOT NULL PRIMARY KEY CHECK(length(trim(Id)) BETWEEN 1 AND 64), NombreUsuario TEXT NOT NULL CHECK(length(trim(NombreUsuario)) BETWEEN 1 AND 100), Vector BLOB NOT NULL CHECK(length(Vector)>=541))";

    public async Task<List<RegisteredPerson>> ListAsync()
    {
        await gate.WaitAsync();
        try
        {
            using var db = Open();
            using var cursor = db.RawQuery("SELECT Id, NombreUsuario FROM Personas ORDER BY NombreUsuario COLLATE NOCASE", null)!;
            var people = new List<RegisteredPerson>();
            while (cursor.MoveToNext()) people.Add(new(cursor.GetString(0)!, cursor.GetString(1)!));
            return people;
        }
        finally { gate.Release(); }
    }

    public async Task<float[]> ReadVectorAsync(string id)
    {
        await gate.WaitAsync();
        byte[]? key = null;
        try
        {
            using var db = Open();
            using var cursor = db.RawQuery("SELECT NombreUsuario, Vector FROM Personas WHERE Id = ?", [id])!;
            if (!cursor.MoveToFirst()) throw new InvalidOperationException("El perfil ya no está disponible.");
            var name = cursor.GetString(0)!;
            var blob = cursor.GetBlob(1) ?? throw new IOException("Vector vacío.");
            var encoded = await SecureStorage.Default.GetAsync(KeyName);
            if (encoded is null) throw new InvalidOperationException("No está disponible la clave del perfil. No se puede verificar.");
            key = Convert.FromBase64String(encoded);
            return VectorCodec.DecryptTemplate(blob, key, id, name)[0].Vector;
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); gate.Release(); }
    }

    public async Task<List<FaceTemplate>> ListTemplatesAsync()
    {
        await gate.WaitAsync();
        byte[]? key = null;
        try
        {
            using var db = Open();
            var encoded = await SecureStorage.Default.GetAsync(KeyName);
            if (encoded is null) return [];
            key = Convert.FromBase64String(encoded);
            using var cursor = db.RawQuery("SELECT Id, NombreUsuario, Vector FROM Personas ORDER BY NombreUsuario COLLATE NOCASE", null)!;
            var templates = new List<FaceTemplate>();
            while (cursor.MoveToNext())
            {
                var id = cursor.GetString(0)!;
                var name = cursor.GetString(1)!;
                var blob = cursor.GetBlob(2) ?? throw new IOException("Vector vacío.");
                templates.Add(FaceTemplate.Create(id, name, VectorCodec.DecryptTemplate(blob, key, id, name)));
            }
            return templates;
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); gate.Release(); }
    }

    public async Task DeleteAsync(string id)
    {
        id = id.Trim();
        if (id.Length is < 1 or > 64) throw new InvalidOperationException("ID de perfil inválido.");
        await gate.WaitAsync();
        try
        {
            using var db = Open();
            db.Delete("Personas", "Id = ?", [id]);
        }
        finally { gate.Release(); }
    }

    internal async Task SaveAsync(string id, string name, float[] vector)
    {
        id = id.Trim(); name = name.Trim();
        if (id.Length is < 1 or > 64 || name.Length is < 1 or > 100)
            throw new InvalidOperationException("Completa el ID y el nombre de usuario.");
        await gate.WaitAsync();
        byte[]? key = null;
        try
        {
            using var db = Open();
            using (var existing = db.RawQuery("SELECT Id FROM Personas WHERE Id = ?", [id])!)
                if (existing.MoveToFirst()) throw new InvalidOperationException("Ese ID ya está registrado. No se reemplazó el rostro existente.");
            var encoded = await SecureStorage.Default.GetAsync(KeyName);
            if (encoded is null)
            {
                using var count = db.RawQuery("SELECT COUNT(*) FROM Personas", null)!;
                count.MoveToFirst();
                if (count.GetInt(0) != 0) throw new InvalidOperationException("No está disponible la clave de los rostros existentes. No se guardaron cambios.");
                key = RandomNumberGenerator.GetBytes(32);
                await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(key));
            }
            else key = Convert.FromBase64String(encoded);
            var blob = VectorCodec.Encrypt(vector, key, id, name);
            using var values = new ContentValues();
            values.Put("Id", id); values.Put("NombreUsuario", name); values.Put("Vector", blob);
            db.BeginTransaction();
            try
            {
                db.InsertOrThrow("Personas", null, values);
                // Read back and authenticate before committing; the UI reports only durable success.
                using var stored = db.RawQuery("SELECT Vector FROM Personas WHERE Id = ?", [id])!;
                if (!stored.MoveToFirst()) throw new IOException("No se pudo verificar el registro.");
                var decoded = VectorCodec.Decrypt(stored.GetBlob(0) ?? throw new IOException("Vector vacío."), key, id, name);
                Array.Clear(decoded);
                db.SetTransactionSuccessful();
            }
            finally { db.EndTransaction(); }
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); gate.Release(); }
    }

    internal async Task SaveTemplateAsync(string id, string name, IReadOnlyList<FaceCaptureVector> captures)
    {
        id = id.Trim(); name = name.Trim();
        var template = FaceTemplate.Create(id, name, captures);
        await gate.WaitAsync();
        byte[]? key = null;
        try
        {
            using var db = Open();
            using (var existing = db.RawQuery("SELECT Id FROM Personas WHERE Id = ?", [id])!)
                if (existing.MoveToFirst()) throw new InvalidOperationException("Ese ID ya está registrado. No se reemplazó el rostro existente.");
            var encoded = await SecureStorage.Default.GetAsync(KeyName);
            if (encoded is null)
            {
                using var count = db.RawQuery("SELECT COUNT(*) FROM Personas", null)!;
                count.MoveToFirst();
                if (count.GetInt(0) != 0) throw new InvalidOperationException("No está disponible la clave de los rostros existentes. No se guardaron cambios.");
                key = RandomNumberGenerator.GetBytes(32);
                await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(key));
            }
            else key = Convert.FromBase64String(encoded);
            var blob = VectorCodec.EncryptTemplate(template.Captures, key, id, name);
            using var values = new ContentValues();
            values.Put("Id", id); values.Put("NombreUsuario", name); values.Put("Vector", blob);
            db.BeginTransaction();
            try
            {
                db.InsertOrThrow("Personas", null, values);
                using var stored = db.RawQuery("SELECT Vector FROM Personas WHERE Id = ?", [id])!;
                if (!stored.MoveToFirst()) throw new IOException("No se pudo verificar el registro.");
                var decoded = VectorCodec.DecryptTemplate(stored.GetBlob(0) ?? throw new IOException("Vector vacío."), key, id, name);
                foreach (var capture in decoded) Array.Clear(capture.Vector);
                db.SetTransactionSuccessful();
            }
            finally { db.EndTransaction(); }
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); gate.Release(); }
    }
}
