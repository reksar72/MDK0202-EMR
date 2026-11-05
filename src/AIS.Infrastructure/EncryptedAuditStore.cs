using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ais.Core;

namespace Ais.Infrastructure;
public sealed record AuditEnvelope(string Nonce, string Ciphertext, string Tag);
public sealed class EncryptedAuditStore(string path, byte[] key) : IAuditStore
{
    readonly byte[] secret = key.Length == 32 ? key.ToArray() : throw new ArgumentException("Ключ AES-256 должен иметь 32 байта");
    readonly object gate = new();
    static readonly byte[] Aad = Encoding.UTF8.GetBytes("EMR-AUDIT-v1");
    public void Write(string actor, Guid patient, string action, string result)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new { actor, patient, action, result, at = DateTimeOffset.UtcNow });
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(secret, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Aad);
        var envelope = new AuditEnvelope(Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
        lock (gate)
            File.AppendAllText(path, JsonSerializer.Serialize(envelope) + Environment.NewLine);
        CryptographicOperations.ZeroMemory(plain);
    }

    public string DecryptForVerification(string line)
    {
        var e = JsonSerializer.Deserialize<AuditEnvelope>(line)!;
        var bytes = Convert.FromBase64String(e.Ciphertext);
        var plain = new byte[bytes.Length];
        using var aes = new AesGcm(secret, 16);
        aes.Decrypt(Convert.FromBase64String(e.Nonce), bytes, Convert.FromBase64String(e.Tag), plain, Aad);
        return Encoding.UTF8.GetString(plain);
    }
}
