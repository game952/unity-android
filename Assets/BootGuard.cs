// BootGuard.cs — v4.0.3 诊断版
// 屏幕直接显示：场景统计 + 最近日志 + 错误红字；顺带解锁 60 帧。
// 覆盖 Assets/ 下旧的 BootGuard.cs 即可，无需挂场景。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    static readonly List<string> logs = new List<string>();
    static readonly List<string> errors = new List<string>();
    static string statsText = "stats --";
    static string framesText = "fps --";
    static bool hasError;

    int frames;
    float acc;
    float statAcc;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Application.logMessageReceivedThreaded += OnLog;
        Push("BootGuard v4.0.3 启动（60帧）");
    }

    static void Push(string s)
    {
        lock (gate)
        {
            logs.Add(s);
            if (logs.Count > 8) logs.RemoveAt(0);
        }
        string stamp = DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\n";
        try { File.AppendAllText("/storage/emulated/0/Download/boot_log.txt", stamp); } catch { }
        try { File.AppendAllText(Path.Combine(Application.persistentDataPath, "boot_log.txt"), stamp); } catch { }
    }

    static void OnLog(string cond, string stack, LogType type)
    {
        string line = type + ": " + cond;
        if (line.Length > 220) line = line.Substring(0, 220);
        lock (gate)
        {
            logs.Add(line);
            if (logs.Count > 8) logs.RemoveAt(0);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                hasError = true;
                if (errors.Count < 5) errors.Add(cond.Length > 300 ? cond.Substring(0, 300) : cond);
            }
        }
        Push(line);
    }

    void Update()
    {
        frames++;
        acc += Time.unscaledDeltaTime;
        statAcc += Time.unscaledDeltaTime;
        if (acc >= 1f) { framesText = "fps " + frames; frames = 0; acc = 0f; }
        if (statAcc >= 2f)
        {
            statAcc = 0f;
            try
            {
                string active = SceneManager.GetActiveScene().name;
                var all = FindObjectsOfType<GameObject>();
                int canvasN = 0;
                foreach (var g in all) if (g.GetComponent<Canvas>() != null) canvasN++;
                statsText = "场景[" + active + "] 物体:" + all.Length + " Canvas:" + canvasN;
            }
            catch (Exception e) { statsText = "stats err " + e.Message; }
        }
    }

    void OnGUI()
    {
        GUIStyle mini = new GUIStyle(GUI.skin.label);
        mini.fontSize = Mathf.Max(14, Screen.height / 40);
        mini.normal.textColor = Color.green;
        mini.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(Screen.width - 150, 10, 140, 36), framesText, mini);

        GUIStyle stat = new GUIStyle(GUI.skin.label);
        stat.fontSize = Mathf.Max(12, Screen.height / 55);
        stat.normal.textColor = Color.cyan;
        GUI.Label(new Rect(10, 6, Screen.width * 0.75f, 40), statsText, stat);

        string recent = null;
        lock (gate) { recent = string.Join("\n", logs.ToArray()); }
        GUIStyle lg = new GUIStyle(GUI.skin.label);
        lg.fontSize = Mathf.Max(10, Screen.height / 60);
        lg.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        lg.wordWrap = true;
        GUI.Label(new Rect(10, 46, Screen.width - 20, Screen.height * 0.4f), recent, lg);

        if (!hasError) return;
        string errs = null;
        lock (gate) { errs = string.Join("\n\n", errors.ToArray()); }
        GUI.Box(new Rect(10, Screen.height * 0.5f, Screen.width - 20, Screen.height * 0.45f), "");
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = Mathf.Max(12, Screen.height / 48);
        st.normal.textColor = Color.red;
        st.wordWrap = true;
        GUI.Label(new Rect(22, Screen.height * 0.5f + 10, Screen.width - 44, Screen.height * 0.4f), errs, st);
    }
}
