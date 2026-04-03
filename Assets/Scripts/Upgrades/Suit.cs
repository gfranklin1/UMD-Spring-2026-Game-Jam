using UnityEngine;

public class Suit : MonoBehaviour, IUpgrade
{
    public void ApplyUpgrade()
    {
        throw new System.NotImplementedException();
    }

    public int Cost()
    {
        return 10;
    }
}
