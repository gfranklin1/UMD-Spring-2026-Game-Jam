using System.Collections;
using System.Reflection;
using UnityEngine;
using FishAlive;

/// <summary>
/// Spawns ambient fish schools in the underwater volume around the ship using
/// SeabedManager.GetFloorY() for accurate per-point floor depths.
/// Uses DenysAlmaral FishAlive prefabs — assign marine_clownfish and/or
/// freshWater_guppy in the fishPrefabs array in the inspector.
/// Each school uses a single species and gets a random subtle color tint for variety.
/// Fish receive personal target GOs parented to the school centre so they spread
/// out as a formation while travelling together.
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
    [Tooltip("Half-extent of the loose formation spread within each school (metres).")]
    public float schoolFormationRadius = 4f;

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

            // Cycle species so every school alternates type (0=clownfish, 1=guppy, …)
            int prefabIndex = s % fishPrefabs.Length;
            var prefab      = fishPrefabs[prefabIndex];

            // Alternate warm/cool tints so the two species look obviously different:
            //   even schools (clownfish) → warm orange-red
            //   odd schools  (guppy)    → cool blue-cyan
            Color schoolTint;
            if (prefabIndex % 2 == 0)
                schoolTint = new Color(Random.Range(0.9f, 1.0f), Random.Range(0.55f, 0.75f), Random.Range(0.2f, 0.4f));
            else
                schoolTint = new Color(Random.Range(0.2f, 0.4f), Random.Range(0.6f, 0.85f), Random.Range(0.9f, 1.0f));

            Debug.Log($"[FishSpawner] School {s}: prefab={prefab?.name ?? "NULL"} tint={schoolTint}");
            _schools.Add(SpawnSchool(globalMin, globalMax, cx, swimMin, swimMax, cz, prefab, schoolTint));
        }
    }

    private GameObject SpawnSchool(Vector3 boundsMin, Vector3 boundsMax,
                                    float cx, float swimMin, float swimMax, float cz,
                                    GameObject prefab, Color tint)
    {
        var schoolGO = new GameObject($"FishSchool_{_schools.Count}");
        schoolGO.transform.position = new Vector3(cx, (swimMin + swimMax) * 0.5f, cz);

        var school = schoolGO.AddComponent<FishSchool>();

        // Wander centre — FishSchool moves this periodically across the map.
        // Personal targets are parented here so the whole formation follows it.
        var centerGO = new GameObject("SchoolCenter");
        centerGO.transform.position = schoolGO.transform.position;
        centerGO.transform.SetParent(schoolGO.transform);
        school.SetWanderTarget(centerGO);
        school.SetWanderBounds(boundsMin, boundsMax);

        int count = Random.Range(fishPerSchoolMin, fishPerSchoolMax + 1);

        for (int f = 0; f < count; f++)
        {
            if (prefab == null) continue;

            // Personal target: parented to centerGO so it rides with the wander point.
            // Offset spreads fish into a loose formation rather than a single point.
            var personalTarget = new GameObject($"FishTarget_{f}");
            personalTarget.transform.SetParent(centerGO.transform);
            Vector3 formationOffset = Random.insideUnitSphere * schoolFormationRadius;
            formationOffset.y *= 0.4f; // flatten vertically — schools are wide, not tall
            personalTarget.transform.localPosition = formationOffset;

            Vector3 spawnPos = personalTarget.transform.position;

            var fishGO = Instantiate(prefab, spawnPos,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            fishGO.transform.SetParent(schoolGO.transform);

            // Guppy schools spawn slightly larger so they read clearly at a distance
            float baseScale = (prefab == fishPrefabs[0]) ? 1.0f : 1.4f;
            fishGO.transform.localScale = Vector3.one * baseScale * Random.Range(0.85f, 1.15f);

            // Apply per-school tint — try both URP (_BaseColor) and legacy (_Color)
            foreach (var rend in fishGO.GetComponentsInChildren<Renderer>())
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                rend.SetPropertyBlock(mpb);
            }

            var motion = fishGO.GetComponent<FishMotion>();
            if (motion != null)
            {
                motion.target = personalTarget;
                motion.EnableHardLimits(boundsMin, boundsMax);
                school.AddMember(motion, personalTarget);
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
