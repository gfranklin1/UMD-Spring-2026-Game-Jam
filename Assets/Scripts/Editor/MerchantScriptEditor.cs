using UnityEngine;
using UnityEditor;

[CustomEditor(typeof (MerchantManager))]
public class MerchantScriptEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if(GUILayout.Button("Summon"))
        {
            ((MerchantManager)target).Summon();
        }
        if (GUILayout.Button("SendOff"))
        {
            ((MerchantManager)target).SendOff();

        }
    }
}
