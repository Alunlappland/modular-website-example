using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Genkan.Sys;

public sealed record DiskInfo(string Name, string Label, string Format, long TotalBytes, long FreeBytes);
public sealed record ProcessInfo(int Pid, string Name, long WorkingSetMb);
public sealed record ServiceInfo(string Name, string State);

public sealed record SysSnapshot(
    string Host,
    string Os,
    double CpuPercent,
    double MemoryUsedPercent,
    long MemoryTotalMb,
    long MemoryFreeMb,
    long UptimeSeconds,
    double RxKbPerSec,
    double TxKbPerSec,
    int ProcessCount,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<ProcessInfo> Processes,
    IReadOnlyList<ServiceInfo> Services);

/// <summary>
/// 轻量系统信息采集：CPU/内存用 kernel32，磁盘/网络用 BCL，服务状态用 sc.exe（带缓存）。
/// 不引入 System.Management / PerformanceCounter 包。
/// </summary>
public sealed class SysMonitor
{
    private static readonly string[] WatchedServices = { "Genkan", "Cloudflared", "OpenList", "sshd" };

    private readonly object _gate = new();
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private long _prevRx;
    private long _prevTx;
    private DateTime _prevAt = DateTime.MinValue;
    private double _cpuPercent;
    private double _rxKb;
    private double _txKb;
    private bool _primed;

    private IReadOnlyList<ServiceInfo> _serviceCache = Array.Empty<ServiceInfo>();
    private DateTime _serviceCacheAt = DateTime.MinValue;

    public SysSnapshot Sample()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;

            if (!_primed)
            {
                ReadCpu(out _prevIdle, out _prevKernel, out _prevUser);
                ReadNet(out _prevRx, out _prevTx);
                _prevAt = now;
                _primed = true;
            }

            var seconds = Math.Max(0.2, (now - _prevAt).TotalSeconds);

            if (ReadCpu(out var idle, out var kernel, out var user)
                && idle >= _prevIdle && kernel >= _prevKernel && user >= _prevUser)
            {
                var dIdle = idle - _prevIdle;
                var dKernel = kernel - _prevKernel;
                var dUser = user - _prevUser;
                var total = dKernel + dUser; // kernel 含 idle
                if (total > 0)
                {
                    _cpuPercent = Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            if (ReadNet(out var rx, out var tx))
            {
                _rxKb = Math.Max(0, (rx - _prevRx) / 1024.0 / seconds);
                _txKb = Math.Max(0, (tx - _prevTx) / 1024.0 / seconds);
                _prevRx = rx;
                _prevTx = tx;
            }

            _prevAt = now;

            var (memTotalMb, memFreeMb, memUsedPercent) = ReadMemory();

            if (now - _serviceCacheAt > TimeSpan.FromSeconds(10))
            {
                _serviceCache = WatchedServices.Select(name => new ServiceInfo(name, QueryService(name))).ToList();
                _serviceCacheAt = now;
            }

            return new SysSnapshot(
                Host: Environment.MachineName,
                Os: Environment.OSVersion.VersionString,
                CpuPercent: Math.Round(_cpuPercent, 1),
                MemoryUsedPercent: Math.Round(memUsedPercent, 1),
                MemoryTotalMb: memTotalMb,
                MemoryFreeMb: memFreeMb,
                UptimeSeconds: Environment.TickCount64 / 1000,
                RxKbPerSec: Math.Round(_rxKb, 1),
                TxKbPerSec: Math.Round(_txKb, 1),
                ProcessCount: SafeProcessCount(),
                Disks: ReadDisks(),
                Processes: ReadProcesses(),
                Services: _serviceCache);
        }
    }

    // ---------- CPU ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    private static bool ReadCpu(out ulong idle, out ulong kernel, out ulong user)
    {
        idle = kernel = user = 0;
        try
        {
            if (!GetSystemTimes(out var i, out var k, out var u))
            {
                return false;
            }

            idle = i.Value;
            kernel = k.Value;
            user = u.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- 内存 ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static (long TotalMb, long FreeMb, double UsedPercent) ReadMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                var totalMb = (long)(status.TotalPhys / 1024 / 1024);
                var freeMb = (long)(status.AvailPhys / 1024 / 1024);
                return (totalMb, freeMb, status.MemoryLoad);
            }
        }
        catch
        {
            // 忽略，返回 0
        }

        return (0, 0, 0);
    }

    // ---------- 磁盘 ----------
    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        var list = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    list.Add(new DiskInfo(
                        drive.Name.TrimEnd('\\'),
                        string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel,
                        drive.DriveFormat,
                        drive.TotalSize,
                        drive.AvailableFreeSpace));
                }
                catch
                {
                    // 单个盘读取失败不影响整体
                }
            }
        }
        catch
        {
            // 忽略
        }

        return list;
    }

    // ---------- 网络 ----------
    private static bool ReadNet(out long rx, out long tx)
    {
        rx = tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                try
                {
                    var stats = nic.GetIPStatistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
                catch
                {
                    // 忽略单个网卡
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- 进程 ----------
    private static IReadOnlyList<ProcessInfo> ReadProcesses()
    {
        var list = new List<ProcessInfo>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.WorkingSet64 <= 0) continue;
                    list.Add(new ProcessInfo(process.Id, process.ProcessName, process.WorkingSet64 / 1024 / 1024));
                }
                catch
                {
                    // 受保护进程跳过
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 忽略
        }

        return list
            .OrderByDescending(p => p.WorkingSetMb)
            .Take(8)
            .ToList();
    }

    private static int SafeProcessCount()
    {
        try { return Process.GetProcesses().Length; }
        catch { return 0; }
    }

    // ---------- 服务 ----------
    private static string QueryService(string name)
    {
        try
        {
            var start = new ProcessStartInfo("sc.exe", $"query {name}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);
            if (process is null) return "未知";

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            var line = output.Split('\n').FirstOrDefault(l => l.Contains("STATE"));
            if (line is null) return "未安装";

            if (line.Contains("RUNNING")) return "Running";
            if (line.Contains("STOPPED")) return "Stopped";
            if (line.Contains("START_PENDING")) return "启动中";
            return line.Trim();
        }
        catch
        {
            return "未知";
        }
    }
}
