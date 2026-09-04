using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace ExaTareo;

public static class VectorCodec
{
    // Fixed v1 format: version + nonce(12) + tag(16) + 128 little-endian float32 values.
    // The app pins SFace 2021dec; changing the model requires re-enrollment/migration.
    private const int HeaderSize = 29;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int VectorBytes = FaceEngine.VectorSize * sizeof(float);
    private const byte VersionSingleVector = 1;
    private const byte VersionTemplate = 2;

    public static byte[] Encrypt(float[] vector, byte[] key, string id, string name)
    {
        var normalized = FaceEngine.Normalize(vector);
        var plain = new byte[VectorBytes];
        for (int i = 0; i < normalized.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(i * 4), normalized[i]);
        var blob = new byte[HeaderSize + plain.Length];
        blob[0] = VersionSingleVector;
        RandomNumberGenerator.Fill(blob.AsSpan(1, NonceSize));
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(blob.AsSpan(1, NonceSize), plain, blob.AsSpan(HeaderSize), blob.AsSpan(13, TagSize), Context(id, name));
            return blob;
        }
        finally { CryptographicOperations.ZeroMemory(plain); Array.Clear(normalized); }
    }

    public static float[] Decrypt(byte[] blob, byte[] key, string id, string name)
    {
        if (blob.Length != HeaderSize + VectorBytes || blob[0] != VersionSingleVector)
            throw new CryptographicException("Formato de vector no compatible.");
        var plain = new byte[VectorBytes];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(1, NonceSize), blob.AsSpan(HeaderSize), blob.AsSpan(13, TagSize), plain, Context(id, name));
            return ReadVector(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static byte[] EncryptTemplate(IReadOnlyList<FaceCaptureVector> captures, byte[] key, string id, string name)
    {
        if (captures.Count == 0) throw new InvalidOperationException("Captura al menos un rostro válido.");
        if (captures.Count > 255) throw new InvalidOperationException("Demasiadas capturas faciales.");
        var plain = new byte[sizeof(int) + captures.Count * (1 + VectorBytes)];
        BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(0, sizeof(int)), captures.Count);
        try
        {
            int offset = sizeof(int);
            foreach (var capture in captures)
            {
                plain[offset++] = (byte)capture.Angle;
                var normalized = FaceEngine.Normalize(capture.Vector);
                try
                {
                    for (int i = 0; i < normalized.Length; i++)
                        BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(offset + i * 4), normalized[i]);
                }
                finally { Array.Clear(normalized); }
                offset += VectorBytes;
            }
            var blob = new byte[HeaderSize + plain.Length];
            blob[0] = VersionTemplate;
            RandomNumberGenerator.Fill(blob.AsSpan(1, NonceSize));
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(blob.AsSpan(1, NonceSize), plain, blob.AsSpan(HeaderSize), blob.AsSpan(13, TagSize), Context(id, name));
            return blob;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static IReadOnlyList<FaceCaptureVector> DecryptTemplate(byte[] blob, byte[] key, string id, string name)
    {
        if (blob.Length == HeaderSize + VectorBytes && blob[0] == VersionSingleVector)
            return [new FaceCaptureVector(CaptureAngle.Front, Decrypt(blob, key, id, name))];
        if (blob.Length < HeaderSize + sizeof(int) + 1 + VectorBytes || blob[0] != VersionTemplate)
            throw new CryptographicException("Formato de plantilla facial no compatible.");
        var plain = new byte[blob.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(1, NonceSize), blob.AsSpan(HeaderSize), blob.AsSpan(13, TagSize), plain, Context(id, name));
            int count = BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(0, sizeof(int)));
            if (count is < 1 or > 255 || plain.Length != sizeof(int) + count * (1 + VectorBytes))
                throw new CryptographicException("Plantilla facial dañada.");
            var captures = new List<FaceCaptureVector>(count);
            int offset = sizeof(int);
            for (int i = 0; i < count; i++)
            {
                var angle = (CaptureAngle)plain[offset++];
                if (!Enum.IsDefined(angle)) throw new CryptographicException("Ángulo facial no compatible.");
                var vector = ReadVector(plain.AsSpan(offset, VectorBytes));
                captures.Add(new FaceCaptureVector(angle, vector));
                offset += VectorBytes;
            }
            return captures;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static float[] ReadVector(ReadOnlySpan<byte> plain)
    {
        var vector = new float[FaceEngine.VectorSize];
        for (int i = 0; i < vector.Length; i++) vector[i] = BinaryPrimitives.ReadSingleLittleEndian(plain.Slice(i * 4, 4));
        if (vector.Any(v => !float.IsFinite(v)) || Math.Abs(vector.Sum(v => (double)v * v) - 1) > .001)
            throw new CryptographicException("Vector facial dañado.");
        return vector;
    }

    private static byte[] Context(string id, string name) => Encoding.UTF8.GetBytes($"sface-2021dec:128:v1:{id.Length}:{id}{name}");
}
