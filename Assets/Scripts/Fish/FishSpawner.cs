using System.Collections;
using System.Reflection;
using UnityEngine;
using FishAlive;

/// <summary>
/// Spawns ambient fish schools in the underwater volume around the ship using
/// SeabedManager.GetFloorY() for accurate per-point floor depths.
/// Uses DenysAlmaral FishAlive prefabs — assign marine_clownfish and/or
/// freshWater_guppy in the fishPrefabs array in the inspector.
/// </summary>
public class FishSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("FishAlive prefabs to pick from (marine_clownfish, freshWater_guppy).")]
    public GameObject[] fishPrefabs;

    [Header("School Config")]
    [Tooltip("How many schools to spawn around the ship.")]
    public int schoolCount = 6;
    [Tooltip("Min fish per school.")]
    public int fishPerSchoolMin = 4;
    [Tooltip("Max fish per school.")]
    public int fishPerSchoolMax = 8;

    [Header("Spawn Area")]
    [Tooltip("XZ radius around the ship in which schools are randomly placed.")]
    public float localRadius = 80f;

    [Header("Depth")]
    [Tooltip("How many metres above the seabed floor fish swim (lower bound).")]
    public float floorClearance   = 2f;
    [Tooltip("How many metres below the water surface fish stay (upper bound).")]
    public float surfaceClearance = 2f;
    [Tooltip("Maximum height above the floor that fish roam (caps the water-column).")]
    public float maxSwimHeight    = 18f;

    private readonly System.Collections.Generic.List<GameObject> _schools = new();
    private Transform _ship;

    // ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        _ship = GameObject.Find("Ship")?.transform;

        if (fishPrefabs == null || fishPrefabs.Length == 0)
        {
            Debug.LogWarning("[FishSpawner] No fish prefabs assigned.", this);
            return;
        }

        StartCoroutine(WaitAndSpawn());
    }

    private IEnumerator WaitAndSpawn()
    {
        SeabedManager sm = null;
        FieldInfo genField = null;
        float timeout = 20f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            sm = FindFirstObjectByType<SeabedManager>();
            if (sm != null)
            {
                if (genField == null)
                    genField = sm.GetType().GetField("_generated",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                if (genField != null && (bool)genField.GetValue(sm))
                    break;
            }

            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }

        if (sm == null)
        {
            Debug.LogWarning("[FishSpawner] SeabedManager not found — fish won't spawn.", this);
            yield break;
        }

        SpawnAllSchools(sm);
    }

    private void SpawnAllSchools(SeabedManager sm)
    {
        Vector3 shipPos = _ship != null ? _ship.position : Vector3.zero;

        // Compute global swim bounds spanning the full spawn area so every fish
        // can roam across the whole map rather than a tight per-school box.
        float globalFloorY  = sm.GetFloorY(shipPos.x, shipPos.z);
        float globalSwimMin = globalFloorY + floorClearance;
        float globalSwimMax = Mathf.Min(-surfaceClearance, globalFloorY + maxSwimHeight);
        if (globalSwimMin >= globalSwimMax) globalSwimMax = globalSwimMin + 3f;

        Vector3 globalMin = new Vector3(shipPos.x - localRadius, globalSwimMin, shipPos.z - localRadius);
        Vector3 globalMax = new Vector3(shipPos.x + localRadius, globalSwimMax, shipPos.z + localRadius);

        for (int s = 0; s < schoolCount; s++)
        {
            Vector2 offset = Random.insideUnitCircle * localRadius;
            float cx = shipPos.x + offset.x;
            float cz = shipPos.z + offset.y;

            float floorY  = sm.GetFloorY(cx, cz);
            float swimMin = floorY + floorClearance;
            float swimMax = Mathf.Min(-surfaceClearance, floorY + maxSwimHeight);
            if (swimMin >= swimMax) swimMax = swimMin + 3f;

            _schools.Add(SpawnSchool(globalMin, globalMax, cx, swimMin, swimMax, cz));
        }
    }

    private GameObject SpawnSchool(Vector3 boundsMin, Vector3 boundsMax,
                                    float cx, float swimMin, float swimMax, float cz)
    {
        var schoolGO = new GameObject($"FishSchool_{_schools.Count}");
        schoolGO.transform.position = new Vector3(cx, (swimMin + swimMax) * 0.5f, cz);

        var school = schoolGO.AddComponent<FishSchool>();

        // Wander target starts at the school centre; FishSchool relocates it periodically.
        var centerGO = new GameObject("SchoolCenter");
        centerGO.transform.position = schoolGO.transform.position;
        centerGO.transform.SetParent(schoolGO.transform);
        school.SetWanderTarget(centerGO);
        school.SetWanderBounds(boundsMin, boundsMax);

        int count = Random.Range(fishPerSchoolMin, fishPerSchoolMax + 1);

        for (int f = 0; f < count; f++)
        {
            var prefab = fishPrefabs[Random.Range(0, fishPrefabs.Length)];
            if (prefab == null) continue;

            Vector3 spawnPos = new Vector3(
                Random.Range(boundsMin.x, boundsMax.x),
                Random.Range(boundsMin.y, boundsMax.y),
                Random.Range(boundsMin.z, boundsMax.z));

            var fishGO = Instantiate(prefab, spawnPos,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            fishGO.transform.SetParent(schoolGO.transform);
            fishGO.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);

            var motion = fishGO.GetComponent<FishMotion>();
            if (motion != null)
            {
                motion.target = centerGO;
                motion.EnableHardLimits(boundsMin, boundsMax);
                school.members.Add(motion);
            }
            else
            {
                Debug.LogWarning($"[FishSpawner] Prefab {prefab.name} has no FishMotion component.", this);
            }
        }

        return schoolGO;
    }

    private void OnDrawGizmos()
    {
        Vector3 center = _ship != null ? _ship.position : transform.position;
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(center, localRadius);
    }
}
