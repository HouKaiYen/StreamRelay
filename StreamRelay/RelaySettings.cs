using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamRelay;

internal sealed record RelayStream(string Path, string Source);

internal sealed record RelaySettings(string Ip, int Port, string Username, string Password, RelayStream[] Streams)
{
    public const string DefaultCredential = "admin";

    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "StreamRelay.Data", "settings.dat");

    public string Address => FormatAddress(Ip, Port);

    public static string FormatAddress(string ip, int port)
        => ip.Contains(':') ? $"[{ip}]:{port}" : $"{ip}:{port}";

    public string GetOutputUrl(string path)
        => $"rtsp://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{Address}/{path}";

    public bool HasValidCredentials => IsValidCredential(Username) && IsValidCredential(Password);

    private static bool IsValidCredential(string value)
        => !string.IsNullOrWhiteSpace(value) && !value.Contains(':') && !value.Any(char.IsControl);

    public static string HashCredential(string value)
        => "sha256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Validate()
    {
        if (!IPAddress.TryParse(Ip, out IPAddress? address)
            || (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            || !NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address)))
        {
            throw new InvalidOperationException("請輸入這台電腦已設定的本機 IP，例如 127.0.0.1。");
        }

        if (Port is < 1 or > 65535)
            throw new InvalidOperationException("連接埠必須介於 1 至 65535。");

        if (!HasValidCredentials)
        {
            throw new InvalidOperationException("請輸入轉發帳號及密碼，兩者皆不可包含冒號或控制字元。");
        }

        if (Streams.Length == 0)
            throw new InvalidOperationException("請至少輸入一條來源 RTSP 連結。");

        for (int index = 0; index < Streams.Length; index++)
        {
            if (!TryParseSource(Streams[index].Source, out Uri? source))
                throw new InvalidOperationException($"第 {index + 1} 列需要完整的 rtsp:// 來源連結。");

            string host = source!.IdnHost.Trim('[', ']');
            if ((source.IsDefaultPort ? 554 : source.Port) == Port
                && (string.Equals(host, Ip, StringComparison.OrdinalIgnoreCase)
                    || IPAddress.TryParse(host, out IPAddress? sourceAddress) && sourceAddress.Equals(address)
                    || IPAddress.IsLoopback(address) && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"第 {index + 1} 列的來源指向此轉發服務，請改用攝影機或來源伺服器的連結。");
            }
        }
    }

    public static bool TryParseSource(string value, out Uri? source)
        => Uri.TryCreate(value, UriKind.Absolute, out source)
            && source.Scheme == "rtsp" && source.Host.Length > 0 && source.Fragment.Length == 0
            && (source.IsDefaultPort || source.Port is > 0 and <= 65535)
            && !value.Any(char.IsWhiteSpace);

    public static RelaySettings? Load()
    {
        if (!File.Exists(SettingsPath))
            return null;

        byte[] contents = ProtectedData.Unprotect(File.ReadAllBytes(SettingsPath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<RelaySettings>(contents);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        byte[] contents = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this), null, DataProtectionScope.CurrentUser);
        string tempPath = SettingsPath + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        File.Move(tempPath, SettingsPath, overwrite: true);
    }
}
