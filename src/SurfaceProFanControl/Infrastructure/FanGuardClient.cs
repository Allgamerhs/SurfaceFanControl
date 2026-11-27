using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SurfaceProFanControl.Shared;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class FanGuardClient
    {
        private const string PipeName = "SurfaceProFanControl.Guard.v1";
        private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(5);
        private const int MaximumResponseBytes = 1024;
        private readonly string _leaseId = Guid.NewGuid().ToString("N");
        private readonly string _guardPath;

        public FanGuardClient() : this(FanGuardServiceManager.PackagedGuardPath) { }

        private FanGuardClient(string guardPath) => _guardPath = guardPath;

        public static FanGuardClient ConnectToExistingService()
        {
            var client = new FanGuardClient(FanGuardServiceManager.ResolveExistingGuardPath());
            client.ReadStatus();
            return client;
        }

        public FanGuardSnapshot ReadStatus() => Send("STATUS");

        public FanGuardSnapshot SendHeartbeat() => Send("PING|" + _leaseId);

        public FanGuardSnapshot SetMaximum() => Send("MAX|" + _leaseId);

        public FanGuardSnapshot SetAutomatic() => Send("AUTO");

        public FanGuardSnapshot Measure() => Send("MEASURE");

        private FanGuardSnapshot Send(string request)
        {
            using (var timeout = new CancellationTokenSource(ExchangeTimeout))
            {
                try
                {
                    return SendAsync(request, timeout.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (timeout.IsCancellationRequested && !(ex is TimeoutException))
                {
                    throw new TimeoutException("Fan koruma hizmeti beş saniye içinde yanıt vermedi.", ex);
                }
            }
        }

        private async Task<FanGuardSnapshot> SendAsync(string request, CancellationToken cancellationToken)
        {
            using (var pipe = new NamedPipeClientStream(
                       ".",
                       PipeName,
                       PipeDirection.InOut,
                       PipeOptions.Asynchronous))
            using (cancellationToken.Register(pipe.Dispose))
            {
                await pipe.ConnectAsync(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                FanGuardPipeIdentity.VerifyConnectedServer(pipe, _guardPath);
                FanGuardServiceManager.VerifyVersion17Guard(_guardPath);

                // The existing guard disconnects immediately after its reply. Have a read pending
                // before the unbuffered pipe write, so even a fast STATUS reply reaches the client.
                var requestBytes = new UTF8Encoding(false).GetBytes(request + "\n");
                var responseBytes = new byte[MaximumResponseBytes];
                var firstRead = pipe.ReadAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                var write = pipe.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);
                await Task.WhenAll(firstRead, write).ConfigureAwait(false);
                var read = await firstRead.ConfigureAwait(false);
                var count = 0;
                var terminated = false;
                while (read > 0)
                {
                    var newline = Array.IndexOf(responseBytes, (byte)'\n', count, read);
                    if (newline >= 0)
                    {
                        count = newline;
                        terminated = true;
                        break;
                    }
                    count += read;
                    if (count == responseBytes.Length) break;
                    read = await pipe.ReadAsync(responseBytes, count, responseBytes.Length - count, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!terminated && count == responseBytes.Length)
                {
                    throw new InvalidDataException("Fan koruma hizmetinin yanıtı izin verilen uzunluğu aştı.");
                }

                var response = Encoding.UTF8.GetString(responseBytes, 0, count).TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(response))
                {
                    throw new IOException("Fan koruma hizmeti boş yanıt verdi.");
                }
                return FanGuardSnapshot.Parse(response);
            }
        }
    }

    public sealed class FanGuardSnapshot
    {
        private readonly Stopwatch _age = Stopwatch.StartNew();
        public DateTime ReceivedAtUtc { get; } = DateTime.UtcNow;
        public TimeSpan Age => _age.Elapsed;

        private FanGuardSnapshot(
            string state,
            int fan0Rpm,
            int fan1Rpm,
            int fan0Duty,
            int fan1Duty,
            double? temperatureCelsius,
            double? criticalCelsius,
            byte mode,
            byte shadow)
        {
            State = state;
            Fan0Rpm = fan0Rpm;
            Fan1Rpm = fan1Rpm;
            Fan0Duty = fan0Duty;
            Fan1Duty = fan1Duty;
            TemperatureCelsius = temperatureCelsius;
            CriticalCelsius = criticalCelsius;
            Mode = mode;
            Shadow = shadow;
        }

        public string State { get; }
        public int Fan0Rpm { get; }
        public int Fan1Rpm { get; }
        public int Fan0Duty { get; }
        public int Fan1Duty { get; }
        public double? TemperatureCelsius { get; }
        public double? CriticalCelsius { get; }
        public byte Mode { get; }
        public byte Shadow { get; }
        public bool IsMaximum => string.Equals(State, "MAX", StringComparison.Ordinal) && Mode == 1 && Shadow == 1;
        public bool IsAutomatic => string.Equals(State, "AUTO", StringComparison.Ordinal) && Mode == 0 && Shadow == 0;

        internal static FanGuardSnapshot Parse(string response)
        {
            var fields = response.Split('|');
            if (fields.Length >= 2 && string.Equals(fields[0], "ERR", StringComparison.Ordinal))
            {
                var detail = fields.Length >= 3 ? fields[2] : fields[1];
                throw new IOException("Fan koruma hizmeti: " + detail);
            }
            if (fields.Length != 10 || !string.Equals(fields[0], "OK", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Fan koruma hizmetinin yanıt biçimi geçersiz.");
            }

            return new FanGuardSnapshot(
                fields[1],
                ParseInteger(fields[2], "Fan 1 RPM"),
                ParseInteger(fields[3], "Fan 2 RPM"),
                ParseInteger(fields[4], "Fan 1 görev"),
                ParseInteger(fields[5], "Fan 2 görev"),
                ParseOptionalDouble(fields[6]),
                ParseOptionalDouble(fields[7]),
                ParseHexByte(fields[8], "mod"),
                ParseHexByte(fields[9], "gölge"));
        }

        private static int ParseInteger(string value, string name)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidDataException(name + " değeri geçersiz.");
            }
            return parsed;
        }

        private static double? ParseOptionalDouble(string value)
        {
            if (value == "--")
            {
                return null;
            }
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidDataException("Sıcaklık değeri geçersiz.");
            }
            return parsed;
        }

        private static byte ParseHexByte(string value, string name)
        {
            if (!byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidDataException("EC " + name + " değeri geçersiz.");
            }
            return parsed;
        }
    }
}
