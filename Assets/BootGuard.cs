// BootGuard.cs — 启动诊断版
// 作用：黑屏时把异常直接画在屏幕上（红字），右上角常驻帧率角标。
// 放到 Assets/ 下任意位置即可，无需挂到场景。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class BootGuardInit
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        var go = new GameObject("~BootGuard");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BootGuard>();
    }
}

public class BootGuard : MonoBehaviour
{
    static readonly object gate = new object();
    static readonly List<string> errors = new List<string>();
    static string logPath;
    static string fpsText = "fps --";

    int frames;
    float acc;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        logPath = Path.Combine(Application.persistentDataPath, "boot_log.txt");
        try { File.WriteAllText(logPath, "== boot " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ver " + Application.version + " ==\n"); } catch { }
        Application.logMessageReceivedThreaded += OnLog;
    }

    static void OnLog(string cond, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            string line = "[" + type + "] " + cond;
            if (line.Length > 400) line = line.Substring(0, 400);
            lock (gate)
            {
                if (errors.Count < 10) errors.Add(line);
                else if (errors.Count == 10) errors.Add("...(后续错误见 boot_log.txt)");
            }
        }
        string stamp = DateTime.Now.ToString("HH:mm:ss.fff ") + type + ": " + cond + "\n";
        try { File.AppendAllText(logPath, stamp); } catch { }
        try { File.AppendAllText("/storage/emulated/0/Download/boot_log.txt", stamp); } catch { }
    }

    void Update()
    {
        frames++;
        acc += Time.unscaledDeltaTime;
        if (acc >= 1f)
        {
            fpsText = "fps " + frames;
            frames = 0;
            acc = 0f;
        }
    }

    void OnGUI()
    {
        // 右上角帧率角标：数字在涨 = 主线程活着；冻结 = 卡死
        GUIStyle mini = new GUIStyle(GUI.skin.label);
        mini.fontSize = Mathf.Max(14, Screen.height / 40);
        mini.normal.textColor = Color.green;
        mini.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(Screen.width - 120, 10, 110, 36), fpsText, mini);

        string txt = null;
        lock (gate)
        {
            if (errors.Count > 0) txt = string.Join("\n\n", errors.ToArray());
        }
        if (txt == null) return;

        GUI.Box(new Rect(10, 50, Screen.width - 20, Screen.height - 70), "");
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = Mathf.Max(12, Screen.height / 48);
        st.normal.textColor = Color.red;
        st.wordWrap = true;
        GUI.Label(new Rect(22, 60, Screen.width - 44, Screen.height - 90),
                  "启动错误（整屏截图发给开发者）：\n\n" + txt, st);
    }
}
