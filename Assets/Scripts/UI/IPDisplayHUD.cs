using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Bottom-right corner HUD showing the host IP so players can share or verify
/// the address they're connected to.
/// </summary>
public class IPDisplayHUD : MonoBehaviour
{
    [SerializeField] private Text _ipText;

    private void Start()
    {
        if (_ipText == null) return;
        _ipText.text = GetIPString();
    }

    private string GetIPString()
    {
        if (NetworkManager.Singleton == null)
            return "";

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null) return "";

        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            // Show ALL non-loopback IPv4 addresses so the host can pick the right one.
            // Same network (LAN/Hamachi): use the 192.168.x.x or 25.x.x.x address shown.
            var ips = GetAllLocalIPs();
            return ips.Length > 0 ? $"Your IP(s):\n{string.Join("\n", ips)}" : "IP: unavailable";
        }
        else
        {
            string addr = transport.ConnectionData.Address;
            return $"Host: {addr}";
        }
    }

    private static string[] GetAllLocalIPs()
    {
        var result = new System.Collections.Generic.List<string>();
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    result.Add(ip.ToString());
            }
        }
        catch { }
        return result.ToArray();
    }
}
