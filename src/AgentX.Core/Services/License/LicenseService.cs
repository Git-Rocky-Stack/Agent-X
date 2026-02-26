using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.License;

/// <summary>
/// Offline-first license service for Agent-X.
///
/// License key format: AX-{TIER}-{PAYLOAD}-{CHECKSUM}
/// Where:
///   AX       = product prefix
///   TIER     = S (Starter), P (Professional), U (Ultimate)
///   PAYLOAD  = 16-char Base32 encoded random data
///   CHECKSUM = 4-char HMAC-SHA256 truncated checksum
/// </summary>
public partial class LicenseService : ILicenseService
{
    private readonly AgentXDbContext _db;
    private LicenseInfo? _cachedLicense;

    // Signing key for HMAC-SHA256 checksum verification.
    // This is offline validation — the key prevents casual piracy, not determined crackers.
    private static readonly byte[] SigningKey =
    {
        0x41, 0x67, 0x65, 0x6E, 0x74, 0x58, 0x2D, 0x4C,
        0x69, 0x63, 0x65, 0x6E, 0x73, 0x65, 0x2D, 0x4B,
        0x65, 0x79, 0x2D, 0x32, 0x30, 0x32, 0x36, 0x2D,
        0x52, 0x6F, 0x63, 0x6B, 0x79, 0x53, 0x74, 0x6B
    };

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    [GeneratedRegex(@"^AX-[SPU]-[A-Z2-7]{16}-[A-Z2-7]{4}$")]
    private static partial Regex LicenseKeyPattern();

    public LicenseService(AgentXDbContext db)
    {
        _db = db;
        Log.Information("LicenseService initialized");
    }

    // ── ILicenseService Implementation ───────────────────────────────

    public async Task<LicenseInfo> GetCurrentLicenseAsync()
    {
        if (_cachedLicense != null)
        {
            Log.Debug("Returning cached license info: {Tier}", _cachedLicense.Tier);
            return _cachedLicense;
        }

        try
        {
            var entity = await _db.Licenses
                .OrderByDescending(l => l.ActivatedAt)
                .FirstOrDefaultAsync();

            if (entity == null || !entity.IsActivated)
            {
                Log.Debug("No active license found, returning Trial tier");
                _cachedLicense = CreateTrialLicense();
                return _cachedLicense;
            }

            var tier = ParseTierFromString(entity.Tier);
            _cachedLicense = new LicenseInfo
            {
                Tier = tier,
                IsActivated = true,
                CustomerName = entity.CustomerName,
                CustomerEmail = entity.CustomerEmail,
                ActivatedAt = entity.ActivatedAt,
                ExpiresAt = null, // Perpetual license — no expiry
                MaxDocuments = LicenseInfo.GetDocumentLimit(tier)
            };

            Log.Information("Loaded active license: {Tier} for {Customer}",
                tier, entity.CustomerEmail ?? "unknown");

            return _cachedLicense;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load license from database, defaulting to Trial");
            _cachedLicense = CreateTrialLicense();
            return _cachedLicense;
        }
    }

    public async Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey)
    {
        Log.Information("Attempting license activation for key: {KeyPrefix}...",
            licenseKey.Length >= 8 ? licenseKey[..8] : licenseKey);

        // Step 1: Validate format
        if (string.IsNullOrWhiteSpace(licenseKey) || !LicenseKeyPattern().IsMatch(licenseKey))
        {
            Log.Warning("License key has invalid format");
            return new LicenseActivationResult
            {
                Success = false,
                Message = "Invalid license key format. Expected format: AX-X-XXXXXXXXXXXXXXXX-XXXX",
                Error = LicenseActivationError.InvalidFormat
            };
        }

        // Step 2: Parse components
        var parts = licenseKey.Split('-');
        // parts[0] = "AX", parts[1] = tier char, parts[2] = payload, parts[3] = checksum
        var tierChar = parts[1][0];
        var payload = parts[2];
        var providedChecksum = parts[3];

        // Step 3: Verify HMAC-SHA256 checksum
        var expectedChecksum = ComputeChecksum($"AX-{tierChar}-{payload}");
        if (!string.Equals(providedChecksum, expectedChecksum, StringComparison.Ordinal))
        {
            Log.Warning("License key checksum mismatch: expected {Expected}, got {Provided}",
                expectedChecksum, providedChecksum);
            return new LicenseActivationResult
            {
                Success = false,
                Message = "Invalid license key. The checksum does not match.",
                Error = LicenseActivationError.InvalidChecksum
            };
        }

        // Step 4: Extract tier
        var tier = tierChar switch
        {
            'S' => LicenseTier.Starter,
            'P' => LicenseTier.Professional,
            'U' => LicenseTier.Ultimate,
            _ => LicenseTier.Trial // Should not happen given regex validation
        };

        // Step 5: Check for duplicate activation
        try
        {
            var existing = await _db.Licenses
                .FirstOrDefaultAsync(l => l.LicenseKey == licenseKey);

            if (existing != null && existing.IsActivated)
            {
                Log.Warning("License key is already activated on this machine");
                return new LicenseActivationResult
                {
                    Success = false,
                    Message = "This license key is already activated.",
                    Error = LicenseActivationError.AlreadyActivated
                };
            }

            // Step 6: Deactivate any existing license before activating the new one
            var currentLicenses = await _db.Licenses.ToListAsync();
            if (currentLicenses.Count > 0)
            {
                _db.Licenses.RemoveRange(currentLicenses);
                Log.Debug("Removed {Count} existing license record(s)", currentLicenses.Count);
            }

            // Step 7: Store the new activation
            var fingerprint = GetMachineFingerprint();
            var now = DateTime.UtcNow;

            var entity = new LicenseEntity
            {
                LicenseKey = licenseKey,
                InstanceId = fingerprint,
                Tier = tier.ToString().ToLowerInvariant(),
                IsActivated = true,
                ActivatedAt = now,
                LastValidatedAt = now,
                CustomerEmail = null,
                CustomerName = null
            };

            _db.Licenses.Add(entity);
            await _db.SaveChangesAsync();

            // Step 8: Update cache
            _cachedLicense = new LicenseInfo
            {
                Tier = tier,
                IsActivated = true,
                CustomerName = entity.CustomerName,
                CustomerEmail = entity.CustomerEmail,
                ActivatedAt = now,
                ExpiresAt = null,
                MaxDocuments = LicenseInfo.GetDocumentLimit(tier)
            };

            Log.Information("License activated successfully: {Tier} tier, fingerprint {Fingerprint}",
                tier, fingerprint[..12] + "...");

            return new LicenseActivationResult
            {
                Success = true,
                Message = $"License activated successfully! Welcome to Agent-X {tier}.",
                LicenseInfo = _cachedLicense
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database error during license activation");
            return new LicenseActivationResult
            {
                Success = false,
                Message = "A database error occurred while activating the license. Please try again.",
                Error = LicenseActivationError.DatabaseError
            };
        }
    }

    public async Task<bool> DeactivateLicenseAsync()
    {
        Log.Information("Deactivating current license");

        try
        {
            var licenses = await _db.Licenses.ToListAsync();
            if (licenses.Count == 0)
            {
                Log.Debug("No license to deactivate");
                _cachedLicense = CreateTrialLicense();
                return true;
            }

            _db.Licenses.RemoveRange(licenses);
            await _db.SaveChangesAsync();

            _cachedLicense = CreateTrialLicense();

            Log.Information("License deactivated, reverted to Trial tier");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to deactivate license");
            return false;
        }
    }

    public async Task<bool> ValidateCurrentLicenseAsync()
    {
        Log.Debug("Validating current license");

        try
        {
            var entity = await _db.Licenses
                .OrderByDescending(l => l.ActivatedAt)
                .FirstOrDefaultAsync();

            if (entity == null || !entity.IsActivated)
            {
                Log.Debug("No active license to validate");
                return false;
            }

            // Re-validate format and checksum
            if (!LicenseKeyPattern().IsMatch(entity.LicenseKey))
            {
                Log.Warning("Stored license key has invalid format");
                return false;
            }

            var parts = entity.LicenseKey.Split('-');
            var tierChar = parts[1][0];
            var payload = parts[2];
            var providedChecksum = parts[3];

            var expectedChecksum = ComputeChecksum($"AX-{tierChar}-{payload}");
            if (!string.Equals(providedChecksum, expectedChecksum, StringComparison.Ordinal))
            {
                Log.Warning("Stored license key has invalid checksum");
                return false;
            }

            // Update last validated timestamp
            entity.LastValidatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            Log.Debug("License validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to validate license");
            return false;
        }
    }

    public string GetMachineFingerprint()
    {
        // Combine machine-specific attributes into a deterministic fingerprint.
        // This is not security-critical — it provides a stable instance identifier.
        var machineName = Environment.MachineName;
        var osVersion = Environment.OSVersion.VersionString;
        var processorCount = Environment.ProcessorCount.ToString();

        var rawData = $"{machineName}|{osVersion}|{processorCount}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));

        var fingerprint = Convert.ToHexString(hashBytes).ToLowerInvariant();
        Log.Debug("Generated machine fingerprint: {FingerprintPrefix}...", fingerprint[..12]);

        return fingerprint;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Computes a 4-character Base32 checksum of the given data using HMAC-SHA256.
    /// </summary>
    private static string ComputeChecksum(string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hashBytes = HMACSHA256.HashData(SigningKey, dataBytes);

        // Take the first 3 bytes (24 bits) to produce 4 Base32 characters (5 bits each, 20 bits used)
        // We use a simple truncation to get 4 characters from the hash.
        var sb = new StringBuilder(4);
        for (var i = 0; i < 4; i++)
        {
            var index = hashBytes[i] % 32;
            sb.Append(Base32Alphabet[index]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a tier string from the database into the LicenseTier enum.
    /// Falls back to Trial for unrecognized values.
    /// </summary>
    private static LicenseTier ParseTierFromString(string tier) => tier.ToLowerInvariant() switch
    {
        "starter" => LicenseTier.Starter,
        "professional" => LicenseTier.Professional,
        "ultimate" => LicenseTier.Ultimate,
        "trial" => LicenseTier.Trial,
        _ => LicenseTier.Trial
    };

    /// <summary>
    /// Creates a default Trial license info with standard limits.
    /// </summary>
    private static LicenseInfo CreateTrialLicense() => new()
    {
        Tier = LicenseTier.Trial,
        IsActivated = false,
        CustomerName = null,
        CustomerEmail = null,
        ActivatedAt = null,
        ExpiresAt = null,
        MaxDocuments = LicenseInfo.GetDocumentLimit(LicenseTier.Trial)
    };
}
