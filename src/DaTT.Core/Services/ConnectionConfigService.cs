using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using Microsoft.Extensions.Logging;

namespace DaTT.Core.Services;

public sealed class ConnectionConfigService : IConnectionConfigService
{
    private readonly string _filePath;
    private readonly ILogger<ConnectionConfigService> _logger;
    private readonly byte[] _encryptionKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConnectionConfigService(string filePath, ILogger<ConnectionConfigService> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _encryptionKey = DeriveEncryptionKey();
    }

    public async Task<IReadOnlyList<ConnectionConfig>> GetAllAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var cipherBytes = await File.ReadAllBytesAsync(_filePath);
            var json = Decrypt(cipherBytes);
            return JsonSerializer.Deserialize<List<ConnectionConfig>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read connection configs from {Path}", _filePath);
            return [];
        }
    }

    public async Task<ConnectionConfig?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(c => c.Id == id);
    }

    public async Task SaveAsync(ConnectionConfig config)
    {
        var all = (await GetAllAsync()).ToList();
        var index = all.FindIndex(c => c.Id == config.Id);

        config.UpdatedAt = DateTime.UtcNow;

        if (index >= 0)
            all[index] = config;
        else
            all.Add(config);

        await PersistAsync(all);
    }

    public async Task DeleteAsync(Guid id)
    {
        var all = (await GetAllAsync()).ToList();
        var removed = all.RemoveAll(c => c.Id == id);

        if (removed > 0)
            await PersistAsync(all);
    }

    private async Task PersistAsync(IEnumerable<ConnectionConfig> configs)
    {
        var json = JsonSerializer.Serialize(configs, JsonOptions);
        var cipherBytes = Encrypt(json);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(_filePath, cipherBytes);
    }

    private byte[] Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return result;
    }

    private string Decrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        var iv = new byte[aes.BlockSize / 8];
        var cipherBytes = new byte[data.Length - iv.Length];

        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(data, iv.Length, cipherBytes, 0, cipherBytes.Length);

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveEncryptionKey()
    {
        var machineId = Environment.MachineName + Environment.UserName;
        var salt = Encoding.UTF8.GetBytes("DaTT-v1-salt");
        using var kdf = new Rfc2898DeriveBytes(machineId, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
    }
}
