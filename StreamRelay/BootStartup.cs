using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace StreamRelay;

internal readonly record struct BootTaskStatus(bool IsEnabled, bool IsRunning, IPEndPoint? Endpoint);

internal static class BootStartup
{
    public const string EnableArgument = "--enable-boot-startup";
    public const string DisableArgument = "--disable-boot-startup";

    private const string TaskName = "StreamRelay";
    // 此雜湊對應 Publish-Portable.ps1 指定版本的官方 mediamtx.exe。
    private const string EngineHash = "18EA5E8DDBE561C99262679ACD03FAAB4A284713BDA6E8A21319FC1B57F9765E";
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonServiceAccount = 5;
    private const int TaskStateRunning = 4;
    private const string EndpointDescriptionPrefix = "RTSP ";
    private const string LocalServiceSid = "S-1-5-19";
    private const string TaskPermissions = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;AU)";
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "StreamRelay Boot");

    public static BootTaskStatus ReadStatus()
    {
        dynamic? task = GetTask(OpenTaskFolder());
        if (task is null)
            return default;

        string description = (string?)task.Definition.RegistrationInfo.Description ?? string.Empty;
        return new BootTaskStatus((bool)task.Enabled, (int)task.State == TaskStateRunning, ReadEndpoint(description));
    }

    private static IPEndPoint? ReadEndpoint(string description)
        => description.StartsWith(EndpointDescriptionPrefix, StringComparison.Ordinal)
            && IPEndPoint.TryParse(description.AsSpan(EndpointDescriptionPrefix.Length), out IPEndPoint? endpoint)
            ? endpoint : null;

    public static async Task SetEnabledAsync(bool enabled)
    {
        ProcessStartInfo startInfo = new(Environment.ProcessPath!)
        {
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add(enabled ? EnableArgument : DisableArgument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("無法啟動管理員設定程序。");
        await process.WaitForExitAsync();
        switch (process.ExitCode)
        {
            case 0:
                return;
            case 2:
                throw new InvalidOperationException("無法讀取設定。請使用同一個 Windows 管理員帳戶開啟 StreamRelay 並授權。");
            case 3:
                throw new InvalidOperationException("找不到受支援的 MediaMTX，請重新解壓縮官方可攜版。");
            default:
                throw new InvalidOperationException("無法更新開機排程，請確認已授與管理員權限且工作排程器可用。");
        }
    }

    public static int RunElevated(bool enabled)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return 1;

        try
        {
            dynamic folder = OpenTaskFolder();
            return enabled ? Install(folder) : Uninstall(folder);
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static int Install(dynamic folder)
    {
        RelaySettings? settings;
        try
        {
            settings = RelaySettings.Load();
            if (settings is null)
                return 2;
            settings.Validate();
        }
        catch (Exception)
        {
            return 2;
        }

        string source = Path.Combine(AppContext.BaseDirectory, "MediaMTX", "mediamtx.exe");
        if (!File.Exists(source))
            return 3;

        // 在固定且受保護的目錄安裝，避免開機排程讀取可攜版路徑中的檔案。
        DirectoryInfo directory = new(InstallDirectory);
        EnsureProtectedDirectory(directory);
        string executable = Path.Combine(directory.FullName, "mediamtx.exe");
        string config = Path.Combine(directory.FullName, "relay.yml");
        string stagedExecutable = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + ".tmp");
        string stagedConfig = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream original = File.OpenRead(source))
            using (FileStream destination = new(stagedExecutable, FileMode.CreateNew, FileAccess.Write))
                original.CopyTo(destination);

            using (FileStream binary = File.OpenRead(stagedExecutable))
            {
                if (Convert.ToHexString(SHA256.HashData(binary)) != EngineHash)
                    return 3;
            }

            dynamic? existing = GetTask(folder);
            if (existing is not null && (bool)existing.Enabled)
                return 0;

            existing?.Stop(0);
            File.WriteAllText(stagedConfig, CreateMediaMtxConfig(settings));

            try
            {
                File.Move(stagedExecutable, executable, overwrite: true);
                File.Move(stagedConfig, config, overwrite: true);
                string account = new SecurityIdentifier(LocalServiceSid).Translate(typeof(NTAccount)).Value;
                folder.RegisterTask(TaskName, CreateTaskXml(executable, config, settings.Address), TaskCreateOrUpdate,
                    account, null, TaskLogonServiceAccount, TaskPermissions);
            }
            catch
            {
                File.Delete(config);
                File.Delete(executable);
                throw;
            }

            return 0;
        }
        finally
        {
            File.Delete(stagedConfig);
            File.Delete(stagedExecutable);
        }
    }

    private static int Uninstall(dynamic folder)
    {
        dynamic? task = GetTask(folder);
        if (task is not null)
        {
            task.Enabled = false;
            task.Stop(0);
            folder.DeleteTask(TaskName, 0);
        }

        DirectoryInfo directory = new(InstallDirectory);
        if (directory.Exists)
        {
            RejectReparsePoint(directory);
            File.Delete(Path.Combine(directory.FullName, "relay.yml"));
            File.Delete(Path.Combine(directory.FullName, "mediamtx.exe"));
            if (!directory.EnumerateFileSystemInfos().Any())
                directory.Delete();
        }

        return 0;
    }

    private static void EnsureProtectedDirectory(DirectoryInfo directory)
    {
        RejectReparsePoint(directory);
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        AddAccess(admins, FileSystemRights.FullControl);
        AddAccess(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
        AddAccess(new SecurityIdentifier(LocalServiceSid), FileSystemRights.ReadAndExecute);

        if (directory.Exists)
            directory.SetAccessControl(security);
        else
            directory.Create(security);

        void AddAccess(SecurityIdentifier sid, FileSystemRights rights)
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
    }

    private static void RejectReparsePoint(DirectoryInfo directory)
    {
        if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("開機元件目錄不能是重新解析點。");
    }

    private static dynamic OpenTaskFolder()
    {
        Type schedulerType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(schedulerType)!;
        service.Connect();
        return service.GetFolder(@"\");
    }

    private static dynamic? GetTask(dynamic folder)
    {
        try
        {
            return folder.GetTask(TaskName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (COMException ex) when (ex.HResult == FileNotFound)
        {
            return null;
        }
    }

    internal static string CreateMediaMtxConfig(RelaySettings settings)
    {
        return JsonSerializer.Serialize(new
        {
            rtsp = true,
            rtspAddress = settings.Address,
            rtspTransports = new[] { "tcp" },
            rtmp = false,
            hls = false,
            webrtc = false,
            srt = false,
            moq = false,
            authInternalUsers = new[]
            {
                new
                {
                    user = RelaySettings.HashCredential(settings.Username),
                    pass = RelaySettings.HashCredential(settings.Password),
                    permissions = new[] { new { action = "read" } },
                },
            },
            pathDefaults = new { sourceOnDemand = true },
            paths = settings.Streams.ToDictionary(stream => stream.Path, stream => new { source = stream.Source }),
        });
    }

    internal static string CreateTaskXml(string engine, string config, string address)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement task = new(ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description", EndpointDescriptionPrefix + address)),
            new XElement(ns + "Triggers",
                new XElement(ns + "BootTrigger", new XElement(ns + "Delay", "PT30S"))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Relay"),
                    new XElement(ns + "UserId", LocalServiceSid),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "RestartOnFailure",
                    new XElement(ns + "Interval", "PT1M"), new XElement(ns + "Count", "10"))),
            new XElement(ns + "Actions", new XAttribute("Context", "Relay"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", engine),
                    new XElement(ns + "Arguments", $"\"{config}\""),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(engine)))));
        return task.ToString(SaveOptions.DisableFormatting);
    }

    public static void RemoveLegacyLoginEntry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue(TaskName) is string command
                && command.EndsWith("StreamRelay.exe\" --startup", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(TaskName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // 舊版登入項目可能受 Windows 帳戶原則管理。
        }
    }
}
