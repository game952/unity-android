using UnityEngine;

// 云端打包自检脚本：APK 装进手机后，屏幕上会显示构建信息
public class HelloWorld : MonoBehaviour
{
    private void OnGUI()
    {
        float h = Screen.height;

        var big = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(24, (int)(h * 0.06f)),
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        GUI.Label(new Rect(0, h * 0.30f, Screen.width, h * 0.40f),
            "BUILD OK\n手机云端打包成功\nUnity " + Application.unityVersion, big);

        var small = new GUIStyle(big)
        {
            fontSize = Mathf.Max(12, (int)(h * 0.02f)),
            alignment = TextAnchor.UpperLeft
        };

        GUI.Label(new Rect(10, 10, Screen.width - 20, h * 0.2f),
            SystemInfo.deviceModel + " / " + SystemInfo.operatingSystem, small);
    }
}
