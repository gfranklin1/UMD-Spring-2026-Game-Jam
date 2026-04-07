/// <summary>
/// Static intent store — set in MainMenuController before loading the gameplay scene,
/// read by NetworkSetup.Start() to auto-start as host or client.
/// Pure static class; statics survive scene transitions without DontDestroyOnLoad.
/// </summary>
public static class NetworkLauncher
{
    public enum Intent { None, Host, Client }

    public static Intent LaunchIntent  { get; private set; } = Intent.None;
    public static string ClientAddress { get; private set; } = "127.0.0.1";
    private static readonly string[] _pirateNames = { "Scurvy Dog", "Bilge Rat", "Salty Pete", "Barnacle Bill", "Captain No-Name" };
    private static string PickRandomPirateName() => _pirateNames[new System.Random().Next(_pirateNames.Length)];

    public static string PlayerName { get; private set; } = PickRandomPirateName();

    public static void SetPlayerName(string n) { PlayerName = string.IsNullOrWhiteSpace(n) ? PickRandomPirateName() : n.Trim(); }

    public static void SetHost()
    {
        LaunchIntent = Intent.Host;
    }

    public static void SetClient(string address)
    {
        LaunchIntent  = Intent.Client;
        ClientAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address;
    }

    public static void Clear()
    {
        LaunchIntent  = Intent.None;
        ClientAddress = "127.0.0.1";
    }
}
