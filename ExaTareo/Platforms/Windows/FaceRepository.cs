using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace ExaTareo;

public sealed partial class FaceRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const string KeyName = "exalmar.face-vector.aes.v1";
    private readonly string path;
    private readonly string storageKey;

    public FaceRepository() : this(FileSystem.AppDataDirectory, KeyName) { }

    internal FaceRepository(string directory, string keyName)
    { path = Path.Combine(directory, "rostros.db3"); storageKey = keyName; }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();
        EnsureSchema(db);
        return db;
    }

    private static void EnsureSchema(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = CurrentSchemaSql;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Personas'";
        var schema = (string?)command.ExecuteScalar();
        if (schema is null || !schema.Contains("length(Vector)=541", StringComparison.OrdinalIgnoreCase)) return;
        using var transaction = db.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE Personas RENAME TO Personas_old";
        command.ExecuteNonQuery();
        command.CommandText = CurrentSchemaSql;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO Personas (Id, NombreUsuario, Vector) SELECT Id, NombreUsuario, Vector FROM Personas_old";
        command.ExecuteNonQuery();
        command.CommandText = "DROP TABLE Personas_old";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private const string CurrentSchemaSql =
        "CREATE TABLE IF NOT EXISTS Personas (Id TEXT NOT NULL PRIMARY KEY CHECK(length(trim(Id)) BETWEEN 1 AND 64), NombreUsuario TEXT NOT NULL CHECK(length(trim(NombreUsuario)) BETWEEN 1 AND 100), Vector BLOB NOT NULL CHECK(length(Vector)>=541))";

    public async Task<List<RegisteredPerson>> ListAsync()
    {
        await gate.WaitAsync();
        try
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT Id, NombreUsuario FROM Personas ORDER BY NombreUsuario COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            var people = new List<RegisteredPerson>();
            while (reader.Read()) people.Add(new(reader.GetString(0), reader.GetString(1)));
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
            using var command = db.CreateCommand();
            command.CommandText = "SELECT NombreUsuario, Vector FROM Personas WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("El perfil ya no está disponible.");
            var name = reader.GetString(0);
            var blob = (byte[])reader.GetValue(1);
            var encoded = await SecureStorage.Default.GetAsync(storageKey);
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
            var encoded = await SecureStorage.Default.GetAsync(storageKey);
            if (encoded is null) return [];
            key = Convert.FromBase64String(encoded);
            using var command = db.CreateCommand();
            command.CommandText = "SELECT Id, NombreUsuario, Vector FROM Personas ORDER BY NombreUsuario COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            var templates = new List<FaceTemplate>();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var blob = (byte[])reader.GetValue(2);
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
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM Personas WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
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
            using var command = db.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Personas WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            if ((long)command.ExecuteScalar()! != 0)
                throw new InvalidOperationException("Ese ID ya está registrado. No se reemplazó el rostro existente.");
            var encoded = await SecureStorage.Default.GetAsync(storageKey);
            if (encoded is null)
            {
                command.CommandText = "SELECT COUNT(*) FROM Personas";
                if ((long)command.ExecuteScalar()! != 0)
                    throw new InvalidOperationException("No está disponible la clave de los rostros existentes. No se guardaron cambios.");
                key = RandomNumberGenerator.GetBytes(32);
                await SecureStorage.Default.SetAsync(storageKey, Convert.ToBase64String(key));
            }
            else key = Convert.FromBase64String(encoded);
            var blob = VectorCodec.Encrypt(vector, key, id, name);
            using var transaction = db.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Personas (Id, NombreUsuario, Vector) VALUES ($id, $name, $vector)";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.Add("$vector", SqliteType.Blob).Value = blob;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT Vector FROM Personas WHERE Id = $id";
            var stored = (byte[]?)command.ExecuteScalar() ?? throw new IOException("No se pudo verificar el registro.");
            var decoded = VectorCodec.Decrypt(stored, key, id, name);
            Array.Clear(decoded);
            transaction.Commit();
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
            using var command = db.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Personas WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            if ((long)command.ExecuteScalar()! != 0)
                throw new InvalidOperationException("Ese ID ya está registrado. No se reemplazó el rostro existente.");
            var encoded = await SecureStorage.Default.GetAsync(storageKey);
            if (encoded is null)
            {
                command.CommandText = "SELECT COUNT(*) FROM Personas";
                if ((long)command.ExecuteScalar()! != 0)
                    throw new InvalidOperationException("No está disponible la clave de los rostros existentes. No se guardaron cambios.");
                key = RandomNumberGenerator.GetBytes(32);
                await SecureStorage.Default.SetAsync(storageKey, Convert.ToBase64String(key));
            }
            else key = Convert.FromBase64String(encoded);
            var blob = VectorCodec.EncryptTemplate(template.Captures, key, id, name);
            using var transaction = db.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Personas (Id, NombreUsuario, Vector) VALUES ($id, $name, $vector)";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.Add("$vector", SqliteType.Blob).Value = blob;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT Vector FROM Personas WHERE Id = $id";
            var stored = (byte[]?)command.ExecuteScalar() ?? throw new IOException("No se pudo verificar el registro.");
            var decoded = VectorCodec.DecryptTemplate(stored, key, id, name);
            foreach (var capture in decoded) Array.Clear(capture.Vector);
            transaction.Commit();
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); gate.Release(); }
    }
}
