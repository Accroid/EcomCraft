using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;

namespace EcomCraftStudio.Studio;

/// <summary>Administrator-registered ComfyUI servers that EcomCraft requests may use: the self-starting local ComfyUI and LAN ComfyUI addresses registered as ComfyUI API backends.</summary>
public static class ExecutionServers
{
    /// <summary>A server whose ComfyUI queue holds more than this many tasks is shown as busy.</summary>
    public const int BusyQueueThreshold = 2;

    /// <summary>Host suffixes treated as LAN names; public DNS names are rejected so cloud services cannot be registered as execution servers.</summary>
    private static readonly string[] LanHostSuffixes = [".local", ".lan", ".home.arpa", ".internal"];

    /// <summary>Other ComfyUI-derived backend types, remote SwarmUI instances and cloud services are excluded.</summary>
    public static bool IsExecutionServer(AbstractT2IBackend backend)
    {
        if (backend is null || !backend.IsReal)
        {
            return false;
        }
        if (backend is ComfyUISelfStartBackend)
        {
            return true;
        }
        return backend.GetType() == typeof(ComfyUIAPIBackend) && IsLanAddress(((ComfyUIAPIBackend)backend).Settings.Address);
    }

    /// <summary>Accepts loopback, private-range IP addresses and LAN host names only.</summary>
    public static bool IsLanAddress(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out Uri uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }
        if (uri.IsLoopback)
        {
            return true;
        }
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(uri.DnsSafeHost, out IPAddress ip) && IsPrivateAddress(ip);
        }
        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }
        string host = uri.DnsSafeHost.TrimEnd('.');
        return !host.Contains('.') || LanHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RFC 1918, link-local and IPv6 unique-local ranges.</summary>
    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }
        byte[] bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC;
    }

    /// <summary>All registered execution servers, regardless of status.</summary>
    public static IEnumerable<BackendHandler.T2IBackendData> All => Program.Backends.EnumerateT2IBackends.Where(data => IsExecutionServer(data.Backend));

    /// <summary>User-facing name; never includes the address.</summary>
    public static string DisplayName(BackendHandler.T2IBackendData data)
    {
        string title = data.Backend.Title?.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            return title.Length <= 40 ? title : title[..40];
        }
        return data.Backend is ComfyUISelfStartBackend ? $"本机 #{data.ID}" : $"服务器 #{data.ID}";
    }

    /// <summary>Loaded capabilities, even while another user occupies the server.</summary>
    public static bool IsReady(BackendHandler.T2IBackendData data) => IsExecutionServer(data.Backend)
        && data.Backend.IsEnabled && !data.Backend.ShutDownReserve && data.Backend.CanLoadModels
        && data.Backend.MaxUsages > 0 && data.Backend.Status == BackendStatus.RUNNING;

    /// <summary>Ready and able to accept another SwarmUI request now.</summary>
    public static bool IsAvailable(BackendHandler.T2IBackendData data) => IsReady(data)
        && data.Usages < data.Backend.MaxUsages && data.Backend.Reservations == 0;

    /// <summary>The node and model listing reported by the server's ComfyUI, or null before it has loaded.</summary>
    public static JObject Schema(BackendHandler.T2IBackendData data) => (data.Backend as ComfyUIAPIAbstractBackend)?.RawObjectInfo;
}
