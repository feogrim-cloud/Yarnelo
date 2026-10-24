using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Polka;

// Hosted-runner-only fixture. The task backend is exercised with a unique test
// task name and manageLegacy=false, never the production registry/task identity.
internal static class StartupFixture
{
    private static string FixtureRoot()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_OS") != "Windows" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Startup integration fixtures require a disposable GitHub-hosted Windows runner.");
        string temporary = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if (String.IsNullOrWhiteSpace(temporary) || !Path.IsPathRooted(temporary))
            throw new InvalidOperationException("The hosted runner temporary path is missing.");
        return Path.GetFullPath(temporary).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string CheckedPath(string value, string root)
    {
        string path = Path.GetFullPath(value);
        if (!Path.IsPathRooted(value) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture paths must remain within the hosted runner temporary directory.");
        for (string ancestor = path; !String.IsNullOrEmpty(ancestor); ancestor = Path.GetDirectoryName(ancestor))
            if ((File.Exists(ancestor) || Directory.Exists(ancestor)) &&
                (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Fixture paths cannot use reparse points.");
        return path;
    }

    private static int ReceiveStartup()
    {
        // Task Scheduler creates a new environment and does not inherit the
        // runner job's GITHUB_ACTIONS/RUNNER_TEMP variables. This branch never
        // registers tasks: authenticate only its isolated fixture directory and
        // marker, then write the launch receipt beside this synthetic binary.
        string executable = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
        string directory = Path.GetDirectoryName(executable);
        DirectoryInfo run = Directory.GetParent(directory);
        if (!String.Equals(Path.GetFileName(executable), "Yarnelo.exe", StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(Path.GetFileName(directory), "application with spaces", StringComparison.Ordinal) ||
            run == null || !Regex.IsMatch(run.Name, @"\Ayarnelo-startup-(x64|x86|arm64)-[a-f0-9]{32}\z"))
            throw new InvalidOperationException("The scheduled receipt needs a uniquely named startup fixture directory.");
        string root = run.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        executable = CheckedPath(executable, root);
        string tokenPath = CheckedPath(Path.Combine(directory, "fixture-token.txt"), root);
        string[] marker = File.ReadAllLines(tokenPath, Encoding.UTF8);
        if (marker.Length != 3) throw new InvalidOperationException("The isolated startup marker is incomplete.");
        string token = marker[0];
        Guid parsed;
        string sid = WindowsIdentity.GetCurrent().User.Value;
        if (!Guid.TryParseExact(token, "N", out parsed) ||
            !run.Name.EndsWith("-" + token, StringComparison.Ordinal) ||
            !String.Equals(Path.GetFullPath(marker[1]), run.FullName, StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(marker[2], sid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The isolated startup fixture token is invalid.");
        string receipt = CheckedPath(Path.Combine(directory, "startup-receipt.txt"), root);
        File.WriteAllLines(receipt, new[] {
            token,
            executable,
            Path.GetFullPath(Environment.CurrentDirectory),
            sid,
            Process.GetCurrentProcess().Id.ToString(),
            "--startup"
        }, new UTF8Encoding(false));
        return 0;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--startup") return ReceiveStartup();
            string root = FixtureRoot();
            if (args.Length != 3 || (args[0] != "enable" && args[0] != "disable" && args[0] != "status")) return 80;
            string executable = CheckedPath(args[1], root);
            if (!String.Equals(Path.GetFileName(executable), "Yarnelo.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) return 80;
            if (!Regex.IsMatch(args[2], @"\AYarnelo\.Startup\.Test\.[a-f0-9]{32}\z")) return 80;
            var registration = new StartupRegistration(executable, args[2], false);
            if (args[0] == "enable") registration.SetEnabled(true);
            else if (args[0] == "disable") registration.SetEnabled(false);
            bool enabled = registration.Enabled;
            Console.WriteLine(enabled ? "enabled" : "disabled");
            if (args[0] == "enable" && !enabled) return 81;
            if (args[0] == "disable" && enabled) return 82;
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 90;
        }
    }
}
