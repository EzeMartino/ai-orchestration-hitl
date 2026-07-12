using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public sealed class IsolatedFinancialDocumentMarkdownConverter : IFinancialDocumentMarkdownConverter
{
    private const int DefaultMaxStandardOutputBytes = 1_000_000;
    private const int DefaultMaxStandardErrorBytes = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _pythonExecutable;
    private readonly string _scriptPath;
    private readonly IFinancialDocumentProcessRunner _processRunner;
    private readonly long _maxStandardInputBytes;
    private readonly long _maxWorkerMemoryBytes;
    private readonly int _maxStandardOutputBytes;
    private readonly int _maxStandardErrorBytes;

    public IsolatedFinancialDocumentMarkdownConverter(
        string pythonHome,
        long maxStandardInputBytes,
        long maxWorkerMemoryBytes)
        : this(
            ResolvePythonExecutable(pythonHome, OperatingSystem.IsWindows()),
            Path.Combine(pythonHome, "document_markdown.py"),
            new SystemFinancialDocumentProcessRunner(),
            maxStandardInputBytes,
            maxWorkerMemoryBytes,
            DefaultMaxStandardOutputBytes,
            DefaultMaxStandardErrorBytes)
    {
    }

    internal IsolatedFinancialDocumentMarkdownConverter(
        string pythonExecutable,
        string scriptPath,
        IFinancialDocumentProcessRunner processRunner,
        long maxStandardInputBytes,
        long maxWorkerMemoryBytes,
        int maxStandardOutputBytes,
        int maxStandardErrorBytes)
    {
        _pythonExecutable = pythonExecutable;
        _scriptPath = scriptPath;
        _processRunner = processRunner;
        _maxStandardInputBytes = Math.Max(1, maxStandardInputBytes);
        _maxWorkerMemoryBytes = Math.Max(1, maxWorkerMemoryBytes);
        _maxStandardOutputBytes = maxStandardOutputBytes;
        _maxStandardErrorBytes = maxStandardErrorBytes;
    }

    public async Task<FinancialDocumentMarkdownResult> ConvertPdfAsync(
        Stream pdf,
        int maxCharacters,
        int maxPages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = CreateStartInfo(maxCharacters, maxPages);
            var request = new FinancialDocumentProcessRequest(
                startInfo,
                pdf,
                _maxStandardInputBytes,
                _maxWorkerMemoryBytes,
                GetMaximumStandardOutputBytes(maxCharacters),
                _maxStandardErrorBytes);
            var result = await _processRunner.RunAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode != 0 || result.OutputLimitExceeded)
            {
                return ConversionFailed();
            }

            return ParseResponse(result.StandardOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ConversionFailed();
        }
    }

    internal static string ResolvePythonExecutable(string pythonHome, bool isWindows)
    {
        return isWindows
            ? Path.Combine(pythonHome, ".venv", "Scripts", "python.exe")
            : Path.Combine(pythonHome, ".venv", "bin", "python");
    }

    internal int GetMaximumStandardOutputBytes(int maxCharacters)
    {
        const int protocolOverheadBytes = 65_536;
        const int maximumJsonBytesPerCharacter = 6;
        var calculated = (long)Math.Max(1, maxCharacters)
            * maximumJsonBytesPerCharacter
            + protocolOverheadBytes;
        return (int)Math.Min(
            int.MaxValue,
            Math.Max(_maxStandardOutputBytes, calculated));
    }

    private ProcessStartInfo CreateStartInfo(int maxCharacters, int maxPages)
    {
        var startInfo = new ProcessStartInfo(_pythonExecutable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add(_scriptPath);
        startInfo.ArgumentList.Add("--max-characters");
        startInfo.ArgumentList.Add(Math.Max(1, maxCharacters).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--max-pages");
        startInfo.ArgumentList.Add(Math.Max(1, maxPages).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--max-input-bytes");
        startInfo.ArgumentList.Add(_maxStandardInputBytes.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--max-memory-bytes");
        startInfo.ArgumentList.Add(_maxWorkerMemoryBytes.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static FinancialDocumentMarkdownResult ParseResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return InvalidResponse();
        }

        try
        {
            var response = JsonSerializer.Deserialize<DocumentMarkdownResponse>(responseJson, JsonOptions);

            if (response?.Succeeded is null ||
                response.Markdown is null ||
                response.Truncated is null)
            {
                return InvalidResponse();
            }

            return new FinancialDocumentMarkdownResult(
                response.Succeeded.Value,
                response.Markdown,
                response.Truncated.Value,
                response.FailureReason);
        }
        catch (JsonException)
        {
            return InvalidResponse();
        }
    }

    private static FinancialDocumentMarkdownResult InvalidResponse()
    {
        return new FinancialDocumentMarkdownResult(false, "", false, "invalid_response");
    }

    private static FinancialDocumentMarkdownResult ConversionFailed()
    {
        return new FinancialDocumentMarkdownResult(false, "", false, "conversion_failed");
    }

    private sealed record DocumentMarkdownResponse(
        bool? Succeeded,
        string? Markdown,
        bool? Truncated,
        string? FailureReason);
}

internal sealed record FinancialDocumentProcessRequest(
    ProcessStartInfo StartInfo,
    Stream StandardInput,
    long MaxStandardInputBytes,
    long MaxWorkerMemoryBytes,
    int MaxStandardOutputBytes,
    int MaxStandardErrorBytes);

internal sealed record FinancialDocumentProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputLimitExceeded);

internal interface IFinancialDocumentProcessRunner
{
    Task<FinancialDocumentProcessResult> RunAsync(
        FinancialDocumentProcessRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SystemFinancialDocumentProcessRunner : IFinancialDocumentProcessRunner
{
    private static readonly TimeSpan PostKillWaitTimeout = TimeSpan.FromSeconds(1);

    public async Task<FinancialDocumentProcessResult> RunAsync(
        FinancialDocumentProcessRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = request.StartInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Python process could not be started.");
        }

        using var processTree = WindowsProcessTreeLifetime.TryAttach(
            process,
            request.MaxWorkerMemoryBytes);
        var outputLimitExceeded = 0;

        void HandleOutputLimitExceeded()
        {
            if (Interlocked.Exchange(ref outputLimitExceeded, 1) == 0)
            {
                TryKill(process, processTree);
            }
        }

        try
        {
            var inputTask = CopyInputAsync(
                request.StandardInput,
                process.StandardInput.BaseStream,
                request.MaxStandardInputBytes,
                cancellationToken);
            var outputTask = ReadBoundedAsync(
                process.StandardOutput.BaseStream,
                request.MaxStandardOutputBytes,
                cancellationToken,
                HandleOutputLimitExceeded);
            var errorTask = ReadBoundedAsync(
                process.StandardError.BaseStream,
                request.MaxStandardErrorBytes,
                cancellationToken,
                HandleOutputLimitExceeded);
            var exitTask = process.WaitForExitAsync(cancellationToken);

            await Task.WhenAll(inputTask, outputTask, errorTask, exitTask);
            var output = await outputTask;
            var error = await errorTask;

            return new FinancialDocumentProcessResult(
                process.ExitCode,
                output.Text,
                error.Text,
                output.LimitExceeded || error.LimitExceeded
                    || Volatile.Read(ref outputLimitExceeded) != 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process, processTree);
            await WaitForExitAfterKillAsync(process);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (Volatile.Read(ref outputLimitExceeded) != 0)
        {
            TryKill(process, processTree);
            await WaitForExitAfterKillAsync(process);
            return new FinancialDocumentProcessResult(-1, "", "", true);
        }
        catch
        {
            TryKill(process, processTree);
            await WaitForExitAfterKillAsync(process);
            throw;
        }
    }

    private static async Task CopyInputAsync(
        Stream input,
        Stream standardInput,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, maxBytes);
        var buffer = new byte[81_920];
        long written = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read > limit - written)
            {
                throw new InvalidDataException("PDF input exceeds the configured worker limit.");
            }

            await standardInput.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
            written += read;
        }

        await standardInput.FlushAsync(cancellationToken);
        standardInput.Close();
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken,
        Action onLimitExceeded)
    {
        var limit = Math.Max(1, maxBytes);
        using var captured = new MemoryStream(Math.Min(limit, 16_384));
        var buffer = new byte[8_192];
        var limitExceeded = false;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            var bytesToCapture = Math.Min(bytesRead, limit - (int)captured.Length);

            if (bytesToCapture > 0)
            {
                captured.Write(buffer, 0, bytesToCapture);
            }

            if (bytesToCapture < bytesRead)
            {
                if (!limitExceeded)
                {
                    limitExceeded = true;
                    onLimitExceeded();
                }
            }
        }

        return new BoundedText(
            Encoding.UTF8.GetString(captured.GetBuffer(), 0, (int)captured.Length),
            limitExceeded);
    }

    private static void TryKill(Process process, WindowsProcessTreeLifetime? processTree)
    {
        processTree?.Terminate();

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        using var cleanupCts = new CancellationTokenSource(PostKillWaitTimeout);

        try
        {
            await process.WaitForExitAsync(cleanupCts.Token);
        }
        catch (OperationCanceledException) when (cleanupCts.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record BoundedText(string Text, bool LimitExceeded);

    private sealed class WindowsProcessTreeLifetime : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const uint JobObjectLimitJobMemory = 0x00000200;
        private const int ExtendedLimitInformationClass = 9;
        private IntPtr _jobHandle;

        private WindowsProcessTreeLifetime(IntPtr jobHandle)
        {
            _jobHandle = jobHandle;
        }

        public static WindowsProcessTreeLifetime? TryAttach(
            Process process,
            long maxMemoryBytes)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                KillUncontainedProcess(process);
                throw new System.ComponentModel.Win32Exception(
                    error,
                    "A bounded worker Job Object could not be created.");
            }

            var lifetime = new WindowsProcessTreeLifetime(jobHandle);
            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                        | JobObjectLimitJobMemory
                },
                JobMemoryLimit = ToNativeUnsigned(Math.Max(1, maxMemoryBytes))
            };

            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var limitsPointer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(limits, limitsPointer, false);
                if (!SetInformationJobObject(
                        jobHandle,
                        ExtendedLimitInformationClass,
                        limitsPointer,
                        (uint)size) ||
                    !AssignProcessToJobObject(jobHandle, process.Handle))
                {
                    var error = Marshal.GetLastWin32Error();
                    lifetime.Dispose();
                    KillUncontainedProcess(process);
                    throw new System.ComponentModel.Win32Exception(
                        error,
                        "The PDF worker could not be assigned to its bounded Job Object.");
                }

                return lifetime;
            }
            finally
            {
                Marshal.FreeHGlobal(limitsPointer);
            }
        }

        public void Terminate()
        {
            var handle = Interlocked.Exchange(ref _jobHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }

        public void Dispose()
        {
            Terminate();
        }

        private static nuint ToNativeUnsigned(long value)
        {
            return Environment.Is64BitProcess
                ? (nuint)value
                : (nuint)Math.Min(value, uint.MaxValue);
        }

        private static void KillUncontainedProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr jobHandle,
            int jobObjectInfoClass,
            IntPtr jobObjectInfo,
            uint jobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }
    }
}

public sealed class FinancialDocumentConversionGate : IFinancialDocumentProcessingGate
{
    private readonly SemaphoreSlim _semaphore;

    public FinancialDocumentConversionGate(int maximumConcurrency)
    {
        var capacity = Math.Max(1, maximumConcurrency);
        _semaphore = new SemaphoreSlim(capacity, capacity);
    }

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
