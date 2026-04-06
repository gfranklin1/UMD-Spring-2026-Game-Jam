using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SysRandom = System.Random;

/// <summary>
/// Server-authoritative loot spawner that streams loot in/out based on ship distance.
/// Loot selection is deterministic (seeded per-site/per-point) so all clients agree.
/// Picked-up loot is tracked and never re-spawned until a full game reset.
/// </summary>
public class LootSpawner : NetworkBehaviour
{
    public static LootSpawner Instance { get; private set; }

    [Header("References")]
    [SerializeField] private LootRegistry lootRegistry;
    [SerializeField] private GameObject   shipwreckPrefab;

    [Header("Streaming")]
    [SerializeField] private float lootLoadRadius   = 200f;
    [SerializeField] private float lootUnloadRadius = 250f;

    [Header("Shipwrecks")]
    [SerializeField] private float shipwreckChance          = 0.3f;
    [SerializeField] private int   wreckLootMin             = 3;
    [SerializeField] private int   wreckLootMax             = 5;
    [SerializeField] private float wreckLootScatterRadius   = 8f;

    // ─── Runtime state (server only) ────────────────────────────────────────
    private readonly HashSet<int> _pickedUpPoints                  = new();
    private readonly Dictionary<int, bool> _wreckDecisions         = new();
    private readonly Dictionary<int, NetworkObject> _spawnedLoot   = new();
    private readonly Dictionary<int, NetworkObject> _spawnedWrecks = new();
    private readonly HashSet<int> _loadedSites                     = new();

    private const int WreckPointBase = 100; // wreck scatter loot uses pointIndex 100+

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        var seabed = SeabedManager.Instance;
        if (seabed != null)
            seabed.OnSiteDiscovered += OnSiteDiscovered;
    }

    public override void OnNetworkDespawn()
    {
        if (SeabedManager.Instance != null)
            SeabedManager.Instance.OnSiteDiscovered -= OnSiteDiscovered;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnSiteDiscovered(DiveSite site) { } // site registered; Update handles streaming

    // ─── Streaming loop ─────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsServer) return;
        var seabed = SeabedManager.Instance;
        if (seabed == null || !seabed.IsGenerated || seabed.ShipTransform == null) return;

        Vector3 shipPos = seabed.ShipTransform.position;
        var allSites = seabed.GetAllSites();
        float loadSq   = lootLoadRadius   * lootLoadRadius;
        float unloadSq = lootUnloadRadius * lootUnloadRadius;

        for (int i = 0; i < allSites.Count; i++)
        {
            var site = allSites[i];
            float dx = shipPos.x - site.CenterWorldPos.x;
            float dz = shipPos.z - site.CenterWorldPos.z;
            float distSq = dx * dx + dz * dz;

            if (distSq <= loadSq && !_loadedSites.Contains(site.SiteIndex))
                LoadSite(site, seabed);
            else if (distSq > unloadSq && _loadedSites.Contains(site.SiteIndex))
                UnloadSite(site);
        }
    }

    // ─── Load / Unload ──────────────────────────────────────────────────────

    private void LoadSite(DiveSite site, SeabedManager seabed)
    {
        _loadedSites.Add(site.SiteIndex);

        int seed = QuotaManager.Instance != null ? QuotaManager.Instance.SeabedSeed.Value : 0;
        var siteRng = new SysRandom(SeabedManager.CellHash(seed, site.SiteIndex, 0x4C4F4F54));

        // Shipwreck at Deep sites
        if (site.CenterZone == DepthZone.Deep)
            TrySpawnWreck(site, seabed, seed, siteRng);

        // Individual loot points
        foreach (var point in site.LootPoints)
        {
            int key = PackKey(site.SiteIndex, point.PointIndex);
            if (_pickedUpPoints.Contains(key) || _spawnedLoot.ContainsKey(key)) continue;

            var itemRng = new SysRandom(SeabedManager.CellHash(seed, site.SiteIndex, point.PointIndex));
            LootRarity rarity = LootRegistry.ZoneToRarity(point.Zone);
            ItemData[] pool = lootRegistry.GetByRarity(rarity);
            if (pool.Length == 0) continue;

            ItemData chosen = pool[itemRng.Next(pool.Length)];
            if (chosen.worldPrefab == null) continue;

            float yaw = (float)(itemRng.NextDouble() * 360.0);
            var go = Instantiate(chosen.worldPrefab, point.WorldPosition, Quaternion.Euler(0f, yaw, 0f));
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn(true);
                _spawnedLoot[key] = netObj;
                var pickup = go.GetComponent<LootPickup>();
                if (pickup != null)
                    pickup.SetSpawnKey(site.SiteIndex, point.PointIndex);
            }
        }
    }

    private void UnloadSite(DiveSite site)
    {
        _loadedSites.Remove(site.SiteIndex);

        // Despawn loot
        foreach (var point in site.LootPoints)
        {
            int key = PackKey(site.SiteIndex, point.PointIndex);
            DespawnAndRemove(_spawnedLoot, key);
        }

        // Despawn wreck scatter loot
        for (int i = 0; i < wreckLootMax; i++)
        {
            int key = PackKey(site.SiteIndex, WreckPointBase + i);
            DespawnAndRemove(_spawnedLoot, key);
        }

        // Despawn wreck model
        DespawnAndRemove(_spawnedWrecks, site.SiteIndex);
    }

    // ─── Shipwrecks ─────────────────────────────────────────────────────────

    private void TrySpawnWreck(DiveSite site, SeabedManager seabed, int seed, SysRandom rng)
    {
        if (shipwreckPrefab == null) return;

        if (!_wreckDecisions.TryGetValue(site.SiteIndex, out bool hasWreck))
        {
            hasWreck = rng.NextDouble() < shipwreckChance;
            _wreckDecisions[site.SiteIndex] = hasWreck;
        }

        if (!hasWreck || _spawnedWrecks.ContainsKey(site.SiteIndex)) return;

        float yaw  = (float)(rng.NextDouble() * 360.0);
        float roll = (float)(rng.NextDouble() * 20.0 - 10.0);
        var wreckGO = Instantiate(shipwreckPrefab, site.CenterWorldPos, Quaternion.Euler(roll, yaw, 0f));
        var wreckNet = wreckGO.GetComponent<NetworkObject>();
        if (wreckNet != null)
        {
            wreckNet.Spawn(true);
            _spawnedWrecks[site.SiteIndex] = wreckNet;
        }

        // Scatter rare loot around the wreck
        ItemData[] rarePool = lootRegistry.GetByRarity(LootRarity.Rare);
        if (rarePool.Length == 0) return;

        int extraCount = rng.Next(wreckLootMin, wreckLootMax + 1);
        for (int i = 0; i < extraCount; i++)
        {
            int key = PackKey(site.SiteIndex, WreckPointBase + i);
            if (_pickedUpPoints.Contains(key) || _spawnedLoot.ContainsKey(key)) continue;

            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist  = (float)(rng.NextDouble() * wreckLootScatterRadius);
            float lx = site.CenterWorldPos.x + Mathf.Cos(angle) * dist;
            float lz = site.CenterWorldPos.z + Mathf.Sin(angle) * dist;
            float ly = seabed.GetFloorY(lx, lz);

            ItemData item = rarePool[rng.Next(rarePool.Length)];
            if (item.worldPrefab == null) continue;

            float lootYaw = (float)(rng.NextDouble() * 360.0);
            var lootGO = Instantiate(item.worldPrefab, new Vector3(lx, ly, lz), Quaternion.Euler(0f, lootYaw, 0f));
            var lootNet = lootGO.GetComponent<NetworkObject>();
            if (lootNet != null)
            {
                lootNet.Spawn(true);
                _spawnedLoot[key] = lootNet;
                var pickup = lootGO.GetComponent<LootPickup>();
                if (pickup != null)
                    pickup.SetSpawnKey(site.SiteIndex, WreckPointBase + i);
            }
        }
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>Called by PlayerController.PickupLootServerRpc to mark a point as consumed.</summary>
    public void NotifyLootPickedUp(int siteIndex, int pointIndex)
    {
        _pickedUpPoints.Add(PackKey(siteIndex, pointIndex));
        _spawnedLoot.Remove(PackKey(siteIndex, pointIndex));
    }

    /// <summary>Called by QuotaManager on game reset. Clears all tracking and despawns everything.</summary>
    public void ResetAllLoot()
    {
        foreach (var kvp in _spawnedLoot)
            if (kvp.Value != null && kvp.Value.IsSpawned) kvp.Value.Despawn(true);
        _spawnedLoot.Clear();

        foreach (var kvp in _spawnedWrecks)
            if (kvp.Value != null && kvp.Value.IsSpawned) kvp.Value.Despawn(true);
        _spawnedWrecks.Clear();

        _pickedUpPoints.Clear();
        _wreckDecisions.Clear();
        _loadedSites.Clear();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static int PackKey(int siteIndex, int pointIndex) => siteIndex * 10000 + pointIndex;

    private static void DespawnAndRemove(Dictionary<int, NetworkObject> dict, int key)
    {
        if (dict.TryGetValue(key, out var netObj))
        {
            if (netObj != null && netObj.IsSpawned) netObj.Despawn(true);
            dict.Remove(key);
        }
    }

    /// <summary>Get the floor Y at a world XZ from SeabedManager.</summary>
    private static float GetFloorY(SeabedManager seabed, float x, float z)
    {
        return seabed.GetFloorY(x, z);
    }
}
