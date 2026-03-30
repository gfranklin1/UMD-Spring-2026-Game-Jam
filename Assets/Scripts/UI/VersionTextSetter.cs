using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class VersionTextSetter : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Text>().text = "Version " + Application.version;
    }
}
