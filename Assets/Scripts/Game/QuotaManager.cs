using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative quota cycle manager. Tracks day/time progression,
/// quota targets, and game-over state. Scene-placed singleton like GoldTracker.
/// </summary>
public class QuotaManager : NetworkBehaviour
{
    public static QuotaManager Instance { get; private set; }

    [Header("Quota")]
    [SerializeField] private int[] quotaTargets = { 100, 250, 500, 800 };
    [SerializeField] private int   quotaIncrement = 400; // added per cycle beyond the list

    [Header("Time")]
    [SerializeField] private float dayDurationSeconds = 180f; // 3 real minutes per in-game day
    [SerializeField] private int   daysPerCycle       = 3;

    // ── Networked state (server-write, everyone-read) ────────────────────────

    // Seabed terrain seed — server picks once at session start; clients read it to generate identical terrain.
    public NetworkVariable<int> SeabedSeed = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int>   _currentCycle    = new(0,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int>   _currentDay      = new(1,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> _timeOfDay       = new(0.25f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool>  _gameOver        = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int>   _totalGoldEarned = new(0,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int>   _gameOverReason  = new(0,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Public read-only accessors ───────────────────────────────────────────

    public int   CurrentCycle      => _currentCycle.Value;
    public int   CurrentDay        => _currentDay.Value;
    public int   DaysPerCycle      => daysPerCycle;
    public float TimeOfDay01       => _timeOfDay.Value;
    public bool  IsGameOver        => _gameOver.Value;
    public int   TotalGoldEarned   => _totalGoldEarned.Value;
    public int   GameOverReason    => _gameOverReason.Value; // 0 = quota fail, 1 = death

    public int CurrentQuotaTarget
    {
        get
        {
            int c = _currentCycle.Value;
            if (c < quotaTargets.Length) return quotaTargets[c];
            return quotaTargets[quotaTargets.Length - 1] + (c - quotaTargets.Length + 1) * quotaIncrement;
        }
    }

    // ── Events ───────────────────────────────────────────────────────────────

    public event System.Action OnDayChanged;
    public event System.Action OnCycleChanged;
    public event System.Action OnCycleAdvanced;   // fires on quota met (cycle increments mid-game)
    public event System.Action OnGameOverTriggered;
    public event System.Action OnGameReset;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer && SeabedSeed.Value == 0)
            SeabedSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
        _currentDay.OnValueChanged   += (_, __) => OnDayChanged?.Invoke();
        _currentCycle.OnValueChanged += (prev, next) =>
        {
            OnCycleChanged?.Invoke();
            if (next > prev) OnCycleAdvanced?.Invoke(); // quota met → revive dead players
        };
        _gameOver.OnValueChanged += (prev, cur) =>
        {
            if (cur)        OnGameOverTriggered?.Invoke();
            if (!cur && prev) OnGameReset?.Invoke();
        };
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Server Update ────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsServer || _gameOver.Value) return;

        _timeOfDay.Value += Time.deltaTime / dayDurationSeconds;

        if (_timeOfDay.Value >= 1f)
        {
            _timeOfDay.Value = 0f;

            if (_currentDay.Value >= daysPerCycle)
                EndOfCycleCheck();
            else
                _currentDay.Value++;
        }
    }

    private void EndOfCycleCheck()
    {
        int gold = GoldTracker.Instance != null ? GoldTracker.Instance.TotalGold : 0;

        if (gold >= CurrentQuotaTarget)
        {
            // Quota met — advance cycle
            _totalGoldEarned.Value += gold;
            GoldTracker.Instance?.ResetGold();
            _currentCycle.Value++;
            _currentDay.Value = 1;
            _timeOfDay.Value = 0.25f;
            RespawnLoot();
        }
        else
        {
            // Quota failed
            _totalGoldEarned.Value += gold;
            _gameOverReason.Value = 0;
            _gameOver.Value = true;
        }
    }

    /// <summary>Called from PlayerController when a player dies, or from EndOfCycleCheck.</summary>
    public void TriggerGameOver(int reason)
    {
        if (!IsServer || _gameOver.Value) return;
        int gold = GoldTracker.Instance != null ? GoldTracker.Instance.TotalGold : 0;
        _totalGoldEarned.Value += gold;
        _gameOverReason.Value = reason;
        _gameOver.Value = true;
    }

    // ── Game Reset ───────────────────────────────────────────────────────────

    /// <summary>Host-only full reset. Resets all state to Day 1 / Cycle 1 without disconnecting.</summary>
    public void ResetGame()
    {
        if (!IsServer || !_gameOver.Value) return;
        _currentCycle.Value    = 0;
        _currentDay.Value      = 1;
        _timeOfDay.Value       = 0.25f;
        _totalGoldEarned.Value = 0;
        _gameOverReason.Value  = 0;
        SeabedSeed.Value       = UnityEngine.Random.Range(1, int.MaxValue); // new world each restart
        GoldTracker.Instance?.ResetGold();
        RespawnLoot();
        ResetSuitRacks();
        _gameOver.Value = false; // fires OnGameReset on all clients via OnValueChanged
    }

    // ── Loot Respawn ─────────────────────────────────────────────────────────

    private void ResetSuitRacks()
    {
        foreach (var rack in FindObjectsByType<DivingSuitRack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            rack.ServerForceReset();
    }

    private void RespawnLoot()
    {
        if (LootSpawner.Instance != null)
            LootSpawner.Instance.ResetAllLoot();
    }
}
