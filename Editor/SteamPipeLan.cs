#if UNITY_EDITOR

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace Ftg.UnitySteamPipe.Editor
{
internal sealed class SteamPipeLanDevice
{
    public string Id;
    public string Name;
    public string Platform;
    public string Project;
    public string Address;
    public int CommandPort;
    public string State;
    public string Detail;
    public DateTime LastSeenUtc;

    public string DisplayName =>
        $"{Name} ({Platform}) - {State}";
}

[InitializeOnLoad]
internal static class SteamPipeLan
{
    private const string ProtocolVersion = "1";
    private const string DiscoverHeader = "USTP_DISCOVER";
    private const string DeviceHeader = "USTP_DEVICE";
    private const string CommandHeader = "USTP_COMMAND";
    private const int DiscoveryPort = 43817;
    private static readonly TimeSpan DeviceLifetime =
        TimeSpan.FromSeconds(10d);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, SteamPipeLanDevice> DevicesById =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<Action> MainThreadActions =
        new();

    private static UdpClient discoveryClient;
    private static UdpClient discoveryListener;
    private static TcpListener commandListener;
    private static bool running;
    private static bool receiverEnabled;
    private static string deviceId = "";
    private static string deviceName = "";
    private static string platformName = "";
    private static string projectName = "";
    private static string sharedKey = "";
    private static int commandPort = 43818;
    private static string localState = "Idle";
    private static string localDetail = "";
    private static string localLog = "";

    public static event Action<string> CommandReceived;
    public static event Action DevicesChanged;

    static SteamPipeLan()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }

    public static void Start(
        string localDeviceId,
        string localDeviceName,
        string localPlatformName,
        string localProjectName)
    {
        deviceId = localDeviceId ?? "";
        deviceName = localDeviceName ?? "";
        platformName = localPlatformName ?? "";
        projectName = localProjectName ?? "";

        if (running)
        {
            return;
        }

        running = true;
        EditorApplication.update += PumpMainThreadActions;
        StartDiscoveryClient();
    }

    public static void Stop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        receiverEnabled = false;
        EditorApplication.update -= PumpMainThreadActions;

        CloseUdp(ref discoveryClient);
        CloseUdp(ref discoveryListener);

        try
        {
            commandListener?.Stop();
        }
        catch
        {
        }

        commandListener = null;

        lock (Sync)
        {
            DevicesById.Clear();
        }

        while (MainThreadActions.TryDequeue(out _))
        {
        }
    }

    public static void ConfigureReceiver(
        bool enabled,
        string localDeviceName,
        string key,
        int port)
    {
        deviceName =
            string.IsNullOrWhiteSpace(localDeviceName)
                ? Environment.MachineName
                : localDeviceName.Trim();

        sharedKey = key ?? "";
        commandPort = Math.Max(1024, Math.Min(65535, port));

        StopReceiver();

        receiverEnabled =
            enabled &&
            !string.IsNullOrWhiteSpace(sharedKey);

        if (!receiverEnabled || !running)
        {
            return;
        }

        StartDiscoveryListener();
        StartCommandListener();
    }

    public static void Refresh()
    {
        if (!running)
        {
            return;
        }

        PruneDevices();

        try
        {
            byte[] data =
                Encoding.UTF8.GetBytes(
                    DiscoverHeader + "|" + ProtocolVersion);

            discoveryClient?.Send(
                data,
                data.Length,
                new IPEndPoint(
                    IPAddress.Broadcast,
                    DiscoveryPort));
        }
        catch
        {
        }
    }

    public static IReadOnlyList<SteamPipeLanDevice> GetDevices()
    {
        PruneDevices();

        lock (Sync)
        {
            return DevicesById.Values
                .Where(device =>
                    !string.Equals(
                        device.Id,
                        deviceId,
                        StringComparison.Ordinal))
                .OrderBy(device => device.Name)
                .Select(CloneDevice)
                .ToArray();
        }
    }

    public static void SetLocalStatus(
        string state,
        string detail)
    {
        localState =
            string.IsNullOrWhiteSpace(state)
                ? "Idle"
                : state;

        localDetail =
            detail ?? "";
    }

    public static void SetLocalLog(
        string value)
    {
        const int maxLogCharacters = 16000;

        string snapshot =
            value ?? "";

        if (snapshot.Length >
            maxLogCharacters)
        {
            snapshot =
                snapshot.Substring(
                    snapshot.Length -
                    maxLogCharacters);
        }

        localLog =
            snapshot;
    }

    public static async Task<string> SendCommandAsync(
        SteamPipeLanDevice device,
        string action,
        string key)
    {
        if (device == null)
        {
            return "対象デバイスが選択されていません。";
        }

        try
        {
            using var client =
                new TcpClient(
                    AddressFamily.InterNetwork);

            Task connectTask =
                client.ConnectAsync(
                    IPAddress.Parse(device.Address),
                    device.CommandPort);

            Task completedTask =
                await Task.WhenAny(
                    connectTask,
                    Task.Delay(5000));

            if (completedTask != connectTask)
            {
                return "接続がタイムアウトしました。";
            }

            await connectTask;

            using NetworkStream stream =
                client.GetStream();

            using var writer =
                new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    1024,
                    true)
                {
                    AutoFlush = true
                };

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true,
                    1024,
                    true);

            string request =
                string.Join(
                    "|",
                    CommandHeader,
                    ProtocolVersion,
                    action ?? "",
                    Encode(key ?? ""));

            await writer.WriteLineAsync(request);

            Task<string> readTask =
                reader.ReadLineAsync();

            completedTask =
                await Task.WhenAny(
                    readTask,
                    Task.Delay(5000));

            if (completedTask != readTask)
            {
                return "応答がタイムアウトしました。";
            }

            string response =
                await readTask;

            if (string.IsNullOrWhiteSpace(response))
            {
                return "応答がありませんでした。";
            }

            string[] parts =
                response.Split('|');

            string message =
                parts.Length > 1
                    ? Decode(parts[1])
                    : response;

            return parts[0] == "OK"
                ? message
                : "拒否: " + message;
        }
        catch (Exception exception)
        {
            return "送信失敗: " + exception.Message;
        }
    }

    private static void StartDiscoveryClient()
    {
        try
        {
            discoveryClient =
                new UdpClient(
                    AddressFamily.InterNetwork);

            discoveryClient.EnableBroadcast = true;
            discoveryClient.Client.Bind(
                new IPEndPoint(
                    IPAddress.Any,
                    0));

            _ = ReceiveDiscoveryResponsesAsync(
                discoveryClient);
        }
        catch
        {
            CloseUdp(ref discoveryClient);
        }
    }

    private static async Task ReceiveDiscoveryResponsesAsync(
        UdpClient client)
    {
        while (running && client == discoveryClient)
        {
            try
            {
                UdpReceiveResult result =
                    await client.ReceiveAsync();

                ParseDeviceAnnouncement(
                    result.Buffer,
                    result.RemoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!running)
                {
                    return;
                }
            }
            catch
            {
            }
        }
    }

    private static void StartDiscoveryListener()
    {
        try
        {
            discoveryListener =
                new UdpClient(
                    AddressFamily.InterNetwork);

            discoveryListener.ExclusiveAddressUse =
                false;

            discoveryListener.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            discoveryListener.Client.Bind(
                new IPEndPoint(
                    IPAddress.Any,
                    DiscoveryPort));

            _ = ListenForDiscoveryAsync(
                discoveryListener);
        }
        catch
        {
            CloseUdp(ref discoveryListener);
            receiverEnabled = false;
        }
    }

    private static async Task ListenForDiscoveryAsync(
        UdpClient listener)
    {
        while (running &&
               receiverEnabled &&
               listener == discoveryListener)
        {
            try
            {
                UdpReceiveResult result =
                    await listener.ReceiveAsync();

                string request =
                    Encoding.UTF8.GetString(
                        result.Buffer);

                if (request !=
                    DiscoverHeader + "|" + ProtocolVersion)
                {
                    continue;
                }

                if (!IsPrivateAddress(
                        result.RemoteEndPoint.Address))
                {
                    continue;
                }

                byte[] response =
                    Encoding.UTF8.GetBytes(
                        CreateDeviceAnnouncement());

                await listener.SendAsync(
                    response,
                    response.Length,
                    result.RemoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!receiverEnabled)
                {
                    return;
                }
            }
            catch
            {
            }
        }
    }

    private static string CreateDeviceAnnouncement()
    {
        return string.Join(
            "|",
            DeviceHeader,
            ProtocolVersion,
            Encode(deviceId),
            Encode(deviceName),
            Encode(platformName),
            Encode(projectName),
            commandPort.ToString(),
            Encode(localState),
            Encode(localDetail));
    }

    private static void ParseDeviceAnnouncement(
        byte[] buffer,
        IPEndPoint remoteEndPoint)
    {
        try
        {
            string[] parts =
                Encoding.UTF8.GetString(buffer)
                    .Split('|');

            if (parts.Length != 9 ||
                parts[0] != DeviceHeader ||
                parts[1] != ProtocolVersion ||
                !int.TryParse(
                    parts[6],
                    out int port))
            {
                return;
            }

            var device =
                new SteamPipeLanDevice
                {
                    Id = Decode(parts[2]),
                    Name = Decode(parts[3]),
                    Platform = Decode(parts[4]),
                    Project = Decode(parts[5]),
                    Address =
                        remoteEndPoint.Address.ToString(),
                    CommandPort = port,
                    State = Decode(parts[7]),
                    Detail = Decode(parts[8]),
                    LastSeenUtc = DateTime.UtcNow
                };

            if (string.IsNullOrWhiteSpace(device.Id) ||
                string.Equals(
                    device.Id,
                    deviceId,
                    StringComparison.Ordinal))
            {
                return;
            }

            lock (Sync)
            {
                DevicesById[device.Id] =
                    device;
            }

            MainThreadActions.Enqueue(
                () => DevicesChanged?.Invoke());
        }
        catch
        {
        }
    }

    private static void StartCommandListener()
    {
        try
        {
            commandListener =
                new TcpListener(
                    IPAddress.Any,
                    commandPort);

            commandListener.Start();

            _ = ListenForCommandsAsync(
                commandListener);
        }
        catch
        {
            try
            {
                commandListener?.Stop();
            }
            catch
            {
            }

            commandListener = null;
            receiverEnabled = false;
        }
    }

    private static async Task ListenForCommandsAsync(
        TcpListener listener)
    {
        while (running &&
               receiverEnabled &&
               listener == commandListener)
        {
            try
            {
                TcpClient client =
                    await listener.AcceptTcpClientAsync();

                _ = HandleCommandClientAsync(
                    client);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!receiverEnabled)
                {
                    return;
                }
            }
            catch
            {
            }
        }
    }

    private static async Task HandleCommandClientAsync(
        TcpClient client)
    {
        using (client)
        {
            try
            {
                var remoteEndPoint =
                    client.Client.RemoteEndPoint
                    as IPEndPoint;

                if (remoteEndPoint == null ||
                    !IsPrivateAddress(
                        remoteEndPoint.Address))
                {
                    return;
                }

                using NetworkStream stream =
                    client.GetStream();

                using var reader =
                    new StreamReader(
                        stream,
                        Encoding.UTF8,
                        true,
                        1024,
                        true);

                using var writer =
                    new StreamWriter(
                        stream,
                        new UTF8Encoding(false),
                        1024,
                        true)
                    {
                        AutoFlush = true
                    };

                string request =
                    await reader.ReadLineAsync();

                string[] parts =
                    (request ?? "").Split('|');

                if (parts.Length != 4 ||
                    parts[0] != CommandHeader ||
                    parts[1] != ProtocolVersion)
                {
                    await WriteResponseAsync(
                        writer,
                        false,
                        "不正なリクエストです。");

                    return;
                }

                string requestKey =
                    Decode(parts[3]);

                if (!SecureEquals(
                        requestKey,
                        sharedKey))
                {
                    await WriteResponseAsync(
                        writer,
                        false,
                        "共有キーが一致しません。");

                    return;
                }

                string action =
                    parts[2];

                if (!IsSupportedAction(action))
                {
                    await WriteResponseAsync(
                        writer,
                        false,
                        "未対応の操作です。");

                    return;
                }

                if (action == "get-log")
                {
                    await WriteResponseAsync(
                        writer,
                        true,
                        localLog);

                    return;
                }

                await WriteResponseAsync(
                    writer,
                    true,
                    "リクエストを受け付けました。");

                MainThreadActions.Enqueue(
                    () => CommandReceived?.Invoke(action));
            }
            catch
            {
            }
        }
    }

    private static Task WriteResponseAsync(
        StreamWriter writer,
        bool succeeded,
        string message)
    {
        return writer.WriteLineAsync(
            (succeeded ? "OK" : "ERR") +
            "|" +
            Encode(message));
    }

    private static bool IsSupportedAction(
        string action)
    {
        return
            action == "git-fetch"
            ||
            action == "git-pull"
            ||
            action == "full-pipeline"
            ||
            action == "get-log";
    }

    private static void StopReceiver()
    {
        receiverEnabled = false;

        CloseUdp(ref discoveryListener);

        try
        {
            commandListener?.Stop();
        }
        catch
        {
        }

        commandListener = null;
    }

    private static void PumpMainThreadActions()
    {
        while (MainThreadActions.TryDequeue(
                   out Action action))
        {
            action();
        }

        PruneDevices();
    }

    private static void PruneDevices()
    {
        bool changed = false;
        DateTime now = DateTime.UtcNow;

        lock (Sync)
        {
            string[] expiredIds =
                DevicesById
                    .Where(pair =>
                        now - pair.Value.LastSeenUtc >
                        DeviceLifetime)
                    .Select(pair => pair.Key)
                    .ToArray();

            foreach (string expiredId in expiredIds)
            {
                DevicesById.Remove(expiredId);
                changed = true;
            }
        }

        if (changed)
        {
            MainThreadActions.Enqueue(
                () => DevicesChanged?.Invoke());
        }
    }

    private static SteamPipeLanDevice CloneDevice(
        SteamPipeLanDevice source)
    {
        return
            new SteamPipeLanDevice
            {
                Id = source.Id,
                Name = source.Name,
                Platform = source.Platform,
                Project = source.Project,
                Address = source.Address,
                CommandPort = source.CommandPort,
                State = source.State,
                Detail = source.Detail,
                LastSeenUtc = source.LastSeenUtc
            };
    }

    private static bool IsPrivateAddress(
        IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] bytes =
            address.GetAddressBytes();

        if (bytes.Length != 4)
        {
            return false;
        }

        return
            bytes[0] == 10
            ||
            bytes[0] == 192 &&
            bytes[1] == 168
            ||
            bytes[0] == 172 &&
            bytes[1] >= 16 &&
            bytes[1] <= 31
            ||
            bytes[0] == 169 &&
            bytes[1] == 254;
    }

    private static bool SecureEquals(
        string left,
        string right)
    {
        byte[] leftBytes =
            Encoding.UTF8.GetBytes(left ?? "");

        byte[] rightBytes =
            Encoding.UTF8.GetBytes(right ?? "");

        int difference =
            leftBytes.Length ^
            rightBytes.Length;

        int length =
            Math.Max(
                leftBytes.Length,
                rightBytes.Length);

        for (int index = 0;
             index < length;
             index++)
        {
            byte leftByte =
                index < leftBytes.Length
                    ? leftBytes[index]
                    : (byte)0;

            byte rightByte =
                index < rightBytes.Length
                    ? rightBytes[index]
                    : (byte)0;

            difference |=
                leftByte ^ rightByte;
        }

        return difference == 0;
    }

    private static string Encode(
        string value)
    {
        return Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                value ?? ""));
    }

    private static string Decode(
        string value)
    {
        return Encoding.UTF8.GetString(
            Convert.FromBase64String(
                value ?? ""));
    }

    private static void CloseUdp(
        ref UdpClient client)
    {
        try
        {
            client?.Close();
        }
        catch
        {
        }

        client = null;
    }
}
}

#endif
