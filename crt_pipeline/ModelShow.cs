// ModelShow.cs — v3.2.1 修复版真机验收（M1 闭环）
// 纯代码方案：[RuntimeInitializeOnLoadMethod] 免编辑器挂场景，开机自动显示
// 加载 Assets/Resources/models/ 下全部 OBJ（Unity 导入为 Mesh）+ 同名 PNG 贴图
// 无贴图自动白模降级；日志统一 [ModelShow] 前缀
using UnityEngine;
using System.Collections.Generic;

public static class ModelShow
{
    // 源数据 z-up → Unity y-up 朝向修正。
    // 真机上模型躺倒/侧翻时，只改这一行重推即可（-90 是分析值，真机定准）。
    static readonly Vector3 ModelRot = new Vector3(-90f, 0f, 0f);

    const int Cols = 6;        // 每行模型数
    const float CellSize = 4f; // 网格间距
    const float FitSize = 2.5f;// 每个模型最大边归一化到的尺寸

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Show()
    {
        var meshes = Resources.LoadAll<Mesh>("models");
        if (meshes == null || meshes.Length == 0)
        {
            Debug.LogError("[ModelShow] Assets/Resources/models/ 下没有 Mesh —— 检查部署命令的抽样拷贝步骤输出");
            return;
        }

        // 贴图池：文件名(小写) → 贴图
        var texMap = new Dictionary<string, Texture2D>();
        foreach (var t in Resources.LoadAll<Texture2D>("models"))
            texMap[t.name.ToLower()] = t;

        // 相机（复用主相机，没有就新建）
        var cam = Camera.main;
        if (cam == null) cam = new GameObject("ModelShowCam").AddComponent<Camera>();
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 55f;

        // 平行光：白模也能看清轮廓
        var lgo = new GameObject("ModelShowLight");
        lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        lgo.AddComponent<Light>().type = LightType.Directional;

        var rootGo = new GameObject("ModelShowRoot");
        int n = meshes.Length;
        int rows = (n + Cols - 1) / Cols;
        int texOk = 0, white = 0;

        for (int i = 0; i < n; i++)
        {
            var m = meshes[i];
            var go = new GameObject(m.name);
            go.transform.SetParent(rootGo.transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();

            // 尺寸归一化：boss 大模型和小披风都看得清
            var b = m.bounds;
            float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            float s = ext > 1e-5f ? FitSize / ext : 1f;
            go.transform.localScale = new Vector3(s, s, s);
            go.transform.localRotation = Quaternion.Euler(ModelRot);

            // 贴图：与模型同名 png（部署脚本已按模型名转存）；没有则白模
            var mat = new Material(Shader.Find("Standard"));
            Texture2D tex;
            if (texMap.TryGetValue(m.name.ToLower(), out tex))
            {
                mat.mainTexture = tex;
                texOk++;
            }
            else
            {
                mat.color = new Color(0.75f, 0.75f, 0.78f);
                white++;
            }
            mr.sharedMaterial = mat;

            int r = i / Cols, c = i % Cols;
            go.transform.localPosition = new Vector3(c * CellSize, 0f, r * CellSize);
        }

        // 相机对准网格中心、俯视
        float cx = (Cols - 1) * CellSize * 0.5f;
        float cz = (rows - 1) * CellSize * 0.5f;
        cam.transform.position = new Vector3(cx, Mathf.Max(rows, 3f) * 7f, cz - 6f);
        cam.transform.rotation = Quaternion.Euler(62f, 0f, 0f);

        Debug.Log($"[ModelShow] meshes={n} textured={texOk} white={white} rows={rows}");
        // 判定：meshes=0 → 拷贝失败；white 全部 → 贴图转 png 失败；模型名在 Hierarchy 同 GameObject 名
    }
}
