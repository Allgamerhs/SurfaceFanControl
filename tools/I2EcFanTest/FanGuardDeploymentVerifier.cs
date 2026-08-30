using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using SurfaceProFanControl.Shared;

namespace SurfaceProI2EcFanTest;

internal static class FanGuardDeploymentVerifier
{
    private const int MaximumResponseBytes = 1024;

    public static int Run(string requestedResultPath)
    {
        try
        {
            var directory = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SurfaceProFanControl"));
            Directory.CreateDirectory(directory);
            var resultPath = Path.GetFullPath(requestedResultPath);
            var fileName = Path.GetFileName(resultPath);
            if (!string.Equals(Path.GetDirectoryName(resultPath), directory, StringComparison.OrdinalIgnoreCase) ||
                !fileName.StartsWith("deploy-pipe-verify-", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            var response = Exchange("AUTO");

            var fields = response.Split('|');
            if (fields.Length != 10 || fields[0] != "OK" || fields[1] != "AUTO" ||
                fields[8] != "00" || fields[9] != "00")
            {
                return 3;
            }

            var payload = new UTF8Encoding(false).GetBytes(response + Environment.NewLine);
            var temporaryPath = resultPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.Read,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }
            File.Move(temporaryPath, resultPath);
            return 0;
        }
        catch (Exception ex)
        {
            GuardLog.Error("Deployment pipe verification failed.", ex);
            return 100;
        }
    }

    public static int RunAuthenticatedCommand(string request)
    {
        try
        {
            RequireAllowedVerificationCommand(request);
            var response = Exchange(request);
            Console.WriteLine(response);
            return response.StartsWith("OK|", StringComparison.Ordinal) ? 0 : 3;
        }
        catch (Exception ex)
        {
            GuardLog.Error("Authenticated pipe command failed.", ex);
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 100;
        }
    }

    internal static string Exchange(string request)
    {
            using (var pipe = new NamedPipeClientStream(
                       ".",
                       FanGuardService.PipeName,
                       PipeDirection.InOut,
                       PipeOptions.Asynchronous))
            {
                pipe.Connect(3000);
                FanGuardPipeIdentity.VerifyConnectedServer(
                    pipe,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SurfaceProFanGuard.exe"));
                using (var writer = new StreamWriter(
                           pipe,
                           new UTF8Encoding(false),
                           1024,
                           true) { AutoFlush = true })
                {
                    writer.WriteLine(request);
                    var readTask = ReadBoundedResponseAsync(pipe);
                    var completed = Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)))
                        .GetAwaiter().GetResult();
                    if (!ReferenceEquals(completed, readTask))
                    {
                        pipe.Dispose();
                        throw new TimeoutException("The fan guard did not respond within five seconds.");
                    }
                    var response = readTask.GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        throw new IOException("The fan guard returned an empty response.");
                    }
                    return response;
                }
            }
    }

    private static void RequireAllowedVerificationCommand(string request)
    {
        if (string.Equals(request, "AUTO", StringComparison.Ordinal) ||
            string.Equals(request, "STATUS", StringComparison.Ordinal))
        {
            return;
        }

        var fields = request.Split('|');
        if (fields.Length == 2 &&
            (string.Equals(fields[0], "MAX", StringComparison.Ordinal) ||
             string.Equals(fields[0], "PING", StringComparison.Ordinal)) &&
            Guid.TryParseExact(fields[1], "N", out _))
        {
            return;
        }

        throw new InvalidDataException("The verification pipe command is not allowed.");
    }

    private static async Task<string> ReadBoundedResponseAsync(Stream stream)
    {
        var bytes = new byte[MaximumResponseBytes];
        var count = 0;
        var terminated = false;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes, count, bytes.Length - count).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var newline = Array.IndexOf(bytes, (byte)'\n', count, read);
            if (newline >= 0)
            {
                count = newline;
                terminated = true;
                break;
            }

            count += read;
        }

        if (!terminated && count == bytes.Length)
        {
            throw new InvalidDataException("The fan guard response exceeded 1024 bytes.");
        }

        return Encoding.UTF8.GetString(bytes, 0, count).TrimEnd('\r');
    }
}
