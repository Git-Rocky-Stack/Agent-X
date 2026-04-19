using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public sealed class EncryptionStateFile : IEncryptionStateFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public EncryptionStateFile()
        : this(DefaultPath())
    {
    }

    // Exposed for tests — lets the tests point at a temp path.
    public EncryptionStateFile(string filePath)
    {
        _filePath = filePath;
    }

    public bool Exists() => File.Exists(_filePath);

    public EncryptionStateInfo? Read()
    {
        if (!File.Exists(_filePath)) return null;
        var json = File.ReadAllText(_filePath);
        var info = JsonSerializer.Deserialize<EncryptionStateInfo>(json, JsonOpts);
        if (info is null)
            throw new InvalidDataException($"Encryption state file at '{_filePath}' is empty or invalid JSON.");
        return info;
    }

    public async Task WriteAsync(EncryptionStateInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(info, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);

        // Restrict file ACL to current user only on Windows.
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var acl = new FileSecurity();
            var currentUser = WindowsIdentity.GetCurrent().Owner;
            acl.SetOwner(currentUser);
            acl.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            new FileInfo(_filePath).SetAccessControl(acl);
        }
    }

    public void Delete()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    private static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AgentX", "encryption.info.json");
    }
}
