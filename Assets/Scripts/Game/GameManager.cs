using UnityEngine;

/// <summary>
/// Singleton game manager. Persists across scenes.
/// Future home for quota tracking, cycle state, merchant spawning, etc.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
