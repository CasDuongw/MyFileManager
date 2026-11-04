using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FM.Core.Elevation;

namespace FM.App.Services.Elevation;

public class WindowsElevationService : IElevationService
{
    public bool CanElevate => OperatingSystem.IsWindows();

    public async Task<ElevationResponse> ExecuteElevatedAsync(ElevationPayload payload, CancellationToken ct = default)
    {
        if (!CanElevate)
        {
            return new ElevationResponse(payload.OperationId, false, new List<ElevationItemResult>(), "Nền tảng hiện tại không hỗ trợ nâng quyền UAC.");
        }

        string? workerPath = FindWorkerExecutable();
        if (string.IsNullOrEmpty(workerPath) || !File.Exists(workerPath))
        {
            return new ElevationResponse(payload.OperationId, false, new List<ElevationItemResult>(), "Không tìm thấy chương trình thực thi FM.Elevated.exe.");
        }

        string tempDir = Path.GetTempPath();
        string opId = string.IsNullOrWhiteSpace(payload.OperationId) ? Guid.NewGuid().ToString("N") : payload.OperationId;
        string payloadFile = Path.Combine(tempDir, $"FM_Elevated_{opId}.json");
        string outFile = Path.Combine(tempDir, $"FM_Result_{opId}.json");

        try
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await File.WriteAllBytesAsync(payloadFile, bytes, ct);

            string hash = Convert.ToHexString(SHA256.HashData(bytes));

            var psi = new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = $"--payload \"{payloadFile}\" --hash \"{hash}\" --out \"{outFile}\" --allow-untrusted-elevation-path",
                Verb = "runas", // Triggers Windows UAC prompt
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (user clicked "No" on UAC)
            {
                return new ElevationResponse(opId, false, new List<ElevationItemResult>(), "Yêu cầu cấp quyền Quản trị viên (UAC) đã bị người dùng hủy.");
            }

            await process.WaitForExitAsync(ct);

            if (File.Exists(outFile))
            {
                string resultJson = await File.ReadAllTextAsync(outFile, Encoding.UTF8, ct);
                try { File.Delete(outFile); } catch { }

                var response = JsonSerializer.Deserialize<ElevationResponse>(resultJson);
                if (response != null)
                {
                    return response;
                }
            }

            return new ElevationResponse(opId, false, new List<ElevationItemResult>(), $"Worker tiến trình đặc quyền kết thúc với mã lỗi: {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            return new ElevationResponse(opId, false, new List<ElevationItemResult>(), "Thao tác đã bị hủy.");
        }
        catch (Exception ex)
        {
            return new ElevationResponse(opId, false, new List<ElevationItemResult>(), $"Lỗi nâng quyền UAC: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(payloadFile)) File.Delete(payloadFile); } catch { }
            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
        }
    }

    private static string? FindWorkerExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string directPath = Path.Combine(baseDir, "FM.Elevated.exe");
        if (File.Exists(directPath)) return directPath;

        // Development fallback paths
        string[] devCandidates =
        [
            Path.Combine(baseDir, "..", "..", "..", "..", "FM.Elevated", "bin", "Debug", "net10.0", "FM.Elevated.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "FM.Elevated", "bin", "Release", "net10.0", "FM.Elevated.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "FM.Elevated", "bin", "Debug", "net10.0", "FM.Elevated.exe")
        ];

        foreach (var cand in devCandidates)
        {
            string full = Path.GetFullPath(cand);
            if (File.Exists(full)) return full;
        }

        return directPath;
    }
}

