using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Services;

public sealed class ConfigService
{
    private readonly string _folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GKSKLaiXe");

    private readonly string _sqlFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GKSKLaiXe");

    private string SqlPath => Path.Combine(_sqlFolder, "sql.config.dat");
    public bool HasSqlConfig => File.Exists(SqlPath);
    private string ApiPath => Path.Combine(_folder, "api.config.json");
    private string PackagePath => Path.Combine(_folder, "packages.config.json");
    private string DriverPackagePath => Path.Combine(_folder, "packages.driver.config.json");
    private string GeneralPackagePath => Path.Combine(_folder, "packages.general.config.json");
    private string DriverDefaultsPath => Path.Combine(_folder, "driver.defaults.config.json");
    private string SigningPath => Path.Combine(_folder, "signing.config.json");

    public SqlSettings LoadSql()
    {
        Directory.CreateDirectory(_sqlFolder);
        if (!File.Exists(SqlPath)) return new SqlSettings();

        var stored = JsonSerializer.Deserialize<StoredSqlSettings>(File.ReadAllText(SqlPath)) ?? new();
        return new SqlSettings
        {
            Server = stored.Server,
            Port = stored.Port,
            Database = stored.Database,
            Username = stored.Username,
            Password = Unprotect(stored.Password)
        };
    }

    public void SaveSql(SqlSettings settings)
    {
        Directory.CreateDirectory(_sqlFolder);
        var stored = new StoredSqlSettings
        {
            Server = settings.Server.Trim(),
            Port = settings.Port,
            Database = settings.Database.Trim(),
            Username = settings.Username.Trim(),
            Password = Protect(settings.Password)
        };
        File.WriteAllText(SqlPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    public ApiSettings LoadApi()
    {
        Directory.CreateDirectory(_folder);
        if (!File.Exists(ApiPath))
            return new ApiSettings();

        var stored = JsonSerializer.Deserialize<StoredApiSettings>(File.ReadAllText(ApiPath)) ?? new();
        return new ApiSettings
        {
            Url = string.IsNullOrWhiteSpace(stored.Url)
                ? "https://egw.baohiemxahoi.gov.vn/api/hssk/gksk"
                : stored.Url,
            Username = stored.Username,
            Password = Unprotect(stored.Password)
        };
    }

    public void SaveApi(ApiSettings settings)
    {
        Directory.CreateDirectory(_folder);

        var stored = new StoredApiSettings
        {
            Url = settings.Url.Trim(),
            Username = settings.Username.Trim(),
            Password = Protect(settings.Password)
        };

        File.WriteAllText(
            ApiPath,
            JsonSerializer.Serialize(
                stored,
                new JsonSerializerOptions { WriteIndented = true }));
    }


    public SigningSettings LoadSigning()
    {
        Directory.CreateDirectory(_folder);

        if (!File.Exists(SigningPath))
            return new SigningSettings();

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSigningSettings>(File.ReadAllText(SigningPath)) ?? new();
            return new SigningSettings
            {
                Mode = string.IsNullOrWhiteSpace(stored.Mode) ? "DIRECT" : stored.Mode,
                ServerUrl = string.IsNullOrWhiteSpace(stored.ServerUrl) ? "http://127.0.0.1:7443" : stored.ServerUrl,
                ApiKey = Unprotect(stored.ApiKey)
            };
        }
        catch
        {
            return new SigningSettings();
        }
    }

    public void SaveSigning(SigningSettings settings)
    {
        Directory.CreateDirectory(_folder);

        var stored = new StoredSigningSettings
        {
            Mode = settings.Mode?.Trim() ?? "DIRECT",
            ServerUrl = settings.ServerUrl?.Trim() ?? "http://127.0.0.1:7443",
            ApiKey = Protect(settings.ApiKey ?? "")
        };

        File.WriteAllText(
            SigningPath,
            JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    public DriverKskDefaults LoadDriverDefaults()
    {
        Directory.CreateDirectory(_folder);

        if (!File.Exists(DriverDefaultsPath))
            return new DriverKskDefaults();

        try
        {
            return JsonSerializer.Deserialize<DriverKskDefaults>(
                       File.ReadAllText(DriverDefaultsPath))
                   ?? new DriverKskDefaults();
        }
        catch
        {
            return new DriverKskDefaults();
        }
    }

    public void SaveDriverDefaults(
        DriverKskDefaults settings)
    {
        Directory.CreateDirectory(_folder);

        File.WriteAllText(
            DriverDefaultsPath,
            JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    public List<string> LoadSelectedPackages(
        string mode)
    {
        Directory.CreateDirectory(_folder);

        var path =
            string.Equals(
                mode,
                "DRIVER",
                StringComparison.OrdinalIgnoreCase)
                ? DriverPackagePath
                : GeneralPackagePath;

        // Tương thích bản cũ: lần đầu tab KSK LÁI XE có thể lấy cấu hình cũ.
        if (!File.Exists(path) &&
            string.Equals(mode, "DRIVER", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(PackagePath))
        {
            path = PackagePath;
        }

        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(
                       File.ReadAllText(path))
                   ?.Where(x => !string.IsNullOrWhiteSpace(x))
                   .Select(x => x.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList()
               ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveSelectedPackages(
        IEnumerable<string> packageNames,
        string mode)
    {
        Directory.CreateDirectory(_folder);

        var path =
            string.Equals(
                mode,
                "DRIVER",
                StringComparison.OrdinalIgnoreCase)
                ? DriverPackagePath
                : GeneralPackagePath;

        var values =
            packageNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                values,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    public List<string> LoadSelectedPackages()
    {
        Directory.CreateDirectory(_folder);

        if (!File.Exists(PackagePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(
                       File.ReadAllText(PackagePath))
                   ?.Where(x => !string.IsNullOrWhiteSpace(x))
                   .Select(x => x.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList()
               ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveSelectedPackages(
        IEnumerable<string> packageNames)
    {
        Directory.CreateDirectory(_folder);

        var values =
            packageNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        File.WriteAllText(
            PackagePath,
            JsonSerializer.Serialize(
                values,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try
        {
            var bytes = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
        }
    }

    private sealed class StoredSqlSettings
    {
        public string Server { get; set; } = "";
        public int Port { get; set; } = 1433;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private sealed class StoredApiSettings
    {
        public string Url { get; set; } = "https://egw.baohiemxahoi.gov.vn/api/hssk/gksk";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

}

internal sealed class StoredSigningSettings
{
    public string Mode { get; set; } = "DIRECT";
    public string ServerUrl { get; set; } = "http://127.0.0.1:7443";
    public string ApiKey { get; set; } = "";
}

