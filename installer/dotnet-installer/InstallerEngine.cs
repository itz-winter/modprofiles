using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

#nullable enable

namespace ModProfileSwitcherInstaller
{
    /// <summary>
    /// Core installer logic — extracts payload, creates shortcuts, registers uninstaller.
    /// Works without any external tools (no Inno Setup, no WiX).
    /// </summary>
    public class InstallerEngine
    {
        public const string AppName = "Minecraft Mod Profile Switcher";
        public const string AppExeName = "ModProfileSwitcher.exe";
        public const string AppVersion = "1.0.0";
        public const string Publisher = "ModProfileSwitcher";
        public const string UninstallRegistryKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModProfileSwitcher";

        public static string DefaultInstallDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "ModProfileSwitcher");

        /// <summary>
        /// Run the full install sequence.
        /// </summary>
        public void Install(string installDir, bool createDesktopShortcut, bool launchOnFinish)
        {
            Directory.CreateDirectory(installDir);

            // 1. Extract payload
            ExtractPayload(installDir);

            // 2. Create Start Menu shortcut
            CreateShortcut(
                Path.Combine(GetStartMenuDir(), AppName + ".lnk"),
                Path.Combine(installDir, AppExeName),
                installDir);

            // 3. Optional Desktop shortcut
            if (createDesktopShortcut)
            {
                CreateShortcut(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        AppName + ".lnk"),
                    Path.Combine(installDir, AppExeName),
                    installDir);
            }

            // 4. Write uninstaller batch + registry
            WriteUninstaller(installDir);
            RegisterUninstall(installDir);

            // 5. Launch
            if (launchOnFinish)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(installDir, AppExeName),
                    UseShellExecute = true
                });
            }
        }

        /// <summary>
        /// Extract the embedded ZIP payload into the install directory.
        /// </summary>
        private void ExtractPayload(string installDir)
        {
            var asm = Assembly.GetExecutingAssembly();

            // Find the embedded payload.zip resource
            string? payloadName = null;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                {
                    payloadName = name;
                    break;
                }
            }

            if (payloadName == null)
                throw new FileNotFoundException(
                    "Embedded payload not found. The installer may be corrupt.\n" +
                    "Available resources: " + string.Join(", ", asm.GetManifestResourceNames()));

            using var stream = asm.GetManifestResourceStream(payloadName)!;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // skip directories
                var destPath = Path.Combine(installDir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        /// <summary>
        /// Create a Windows shortcut (.lnk) using a small VBScript shim
        /// (avoids COM interop / IShellLink dependency).
        /// </summary>
        private void CreateShortcut(string lnkPath, string targetExe, string workingDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

            // Use PowerShell to create .lnk — universally available on Windows 10+
            var ps = $@"
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut('{lnkPath.Replace("'", "''")}')
$sc.TargetPath = '{targetExe.Replace("'", "''")}'
$sc.WorkingDirectory = '{workingDir.Replace("'", "''")}'
$sc.Description = '{AppName}'
$sc.Save()
";
            RunPowerShell(ps);
        }

        /// <summary>
        /// Write a small uninstall.bat that removes files, shortcuts, and registry keys.
        /// </summary>
        private void WriteUninstaller(string installDir)
        {
            var startMenuLnk = Path.Combine(GetStartMenuDir(), AppName + ".lnk");
            var desktopLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                AppName + ".lnk");

            var bat = $@"@echo off
echo Uninstalling {AppName}...
echo.

:: Remove shortcuts
del /f /q ""{startMenuLnk}"" 2>nul
del /f /q ""{desktopLnk}"" 2>nul

:: Remove registry entry
reg delete ""HKCU\{UninstallRegistryKey}"" /f 2>nul

:: Remove startup entry (if any)
reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""{AppName}"" /f 2>nul

:: Wait a moment for exe to release
timeout /t 2 /nobreak >nul

:: Remove install directory (including this script)
cd /d ""%TEMP%""
rmdir /s /q ""{installDir}"" 2>nul

echo Done.
timeout /t 3
";
            File.WriteAllText(Path.Combine(installDir, "uninstall.bat"), bat);
        }

        /// <summary>
        /// Register in Add/Remove Programs (HKCU so no admin required).
        /// </summary>
        private void RegisterUninstall(string installDir)
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", Publisher);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString",
                $"cmd /c \"\"{Path.Combine(installDir, "uninstall.bat")}\"\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            // Approximate installed size in KB
            try
            {
                long totalBytes = 0;
                foreach (var f in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    totalBytes += new FileInfo(f).Length;
                key.SetValue("EstimatedSize", (int)(totalBytes / 1024), RegistryValueKind.DWord);
            }
            catch { /* non-critical */ }
        }

        private static string GetStartMenuDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
        }

        private static void RunPowerShell(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit(15000);
        }
    }
}
