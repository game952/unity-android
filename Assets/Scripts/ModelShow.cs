// ModelShow.cs — M1 验收: 无编辑器纯代码显示模型
// 放入 Assets/Scripts/ 即生效, 无需手动挂场景
// 原理: RuntimeInitializeOnLoadMethod 在场景加载后自动执行
using UnityEngine;

public static class ModelShow
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Show()
    {
        var models = Resources.LoadAll<GameObject>("models");
        if (models.Length == 0)
        {
            Debug.LogError("[ModelShow] Assets/Resources/models 下没有模型, 检查复制路径");
            return;
        }

        int i = 0;
        foreach (var go in models)
        {
            var m = Object.Instantiate(go, new Vector3(i * 2f, 0f, 0f), Quaternion.identity);
            m.name = go.name;
            i++;
        }

        // 优先复用工程现有主相机, 没有才新建
        var cam = Camera.main;
        if (cam == null)
        {
            var cgo = new GameObject("ShowCam");
            cam = cgo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
        }
        cam.transform.position = new Vector3(2f, 1.2f, -3.2f);
        cam.transform.LookAt(new Vector3(2f, 0.6f, 0f));

        Debug.Log("[ModelShow] OK 已实例化 " + i + " 个模型, 一字排开于 x=0~8");
    }
}
