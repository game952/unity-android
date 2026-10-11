using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 资源自检脚本 v2 —— 由「BUILD OK 测试桩」升级（文件名与 GUID 不变，场景无需改动）
//
// 流程：读取 StreamingAssets/manifest.txt（591 个 a_wt/w_wt 资源清单，含大小）
//       → 抽样下载并校验「大小 + 头部」（ski=CRT_SkelSkin / act=CRT_Actor / dds=DDS ）
//       → 屏幕绿字 = 通过；红字 = 失败 + 首个错误原因
//
// 说明：安卓上 StreamingAssets 在 APK 内部，只能用 UnityWebRequest 按 URL 读取，
//       无法目录枚举，所以清单 manifest.txt 由打包脚本离线生成。
public class HelloWorld : MonoBehaviour
{
    struct Item { public string path; public long size; }

    const int MaxSamples = 16;      // 抽样数（三种扩展名各保底 1 个）
    const int TimeoutSec = 20;

    readonly List<Item> _items = new List<Item>();
    readonly List<string> _lines = new List<string>();
    string _verdict = "SELF-CHECK";
    Color _verdictColor = Color.yellow;
    bool _finished;

    IEnumerator Start()
    {
        // ---- 1) 读取清单 ----
        var req = UnityWebRequest.Get(SA("manifest.txt"));
        req.timeout = TimeoutSec;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Fail("manifest.txt 读取失败: " + req.error + " (HTTP " + req.responseCode + ")");
            yield break;
        }

        long totalBytes = 0;
        foreach (var ln in Encoding.UTF8.GetString(req.downloadHandler.data).Split('\n'))
        {
            int tab = ln.LastIndexOf('\t');
            if (tab <= 0) continue;
            long sz;
            if (!long.TryParse(ln.Substring(tab + 1), out sz)) continue;
            _items.Add(new Item { path = ln.Substring(0, tab), size = sz });
            totalBytes += sz;
        }
        if (_items.Count == 0)
        {
            Fail("manifest.txt 为空或格式错误");
            yield break;
        }
        _lines.Add("manifest: " + _items.Count + " files / " + (totalBytes / (1024f * 1024f)).ToString("F1") + " MB");

        // ---- 2) 选样：三种扩展名各保底一个 + 全量等距抽样 ----
        var picked = new List<int>();
        var seenExt = new HashSet<string>();
        for (int i = 0; i < _items.Count && picked.Count < 3; i++)
            if (seenExt.Add(Ext(_items[i].path))) picked.Add(i);
        for (int i = 0; picked.Count < MaxSamples && i < _items.Count; i++)
        {
            int idx = (int)((long)i * (_items.Count - 1) / (MaxSamples - 1));
            if (!picked.Contains(idx)) picked.Add(idx);
        }
        picked.Sort();

        // ---- 3) 逐个下载校验 ----
        int ok = 0; string firstErr = null;
        foreach (int idx in picked)
        {
            var it = _items[idx];
            var r = UnityWebRequest.Get(SA(it.path));
            r.timeout = TimeoutSec;
            yield return r.SendWebRequest();

            string err = null;
            if (r.result != UnityWebRequest.Result.Success)
            {
                err = "下载失败 " + r.error + " (HTTP " + r.responseCode + ")";
            }
            else
            {
                byte[] d = r.downloadHandler.data;
                if (d.LongLength != it.size)
                    err = "大小不符: 应 " + it.size + " 实 " + d.Length;
                else
                {
                    string ext = Ext(it.path);
                    if (ext == "ski" && !HeadIs(d, 4, "CRT_SkelSkin")) err = "头部不是 CRT_SkelSkin";
                    else if (ext == "act" && !HeadIs(d, 4, "CRT_Actor")) err = "头部不是 CRT_Actor";
                    else if (ext == "dds" && !HeadIs(d, 0, "DDS ")) err = "头部不是 DDS ";
                }
            }

            if (err == null)
            {
                ok++;
                _lines.Add("OK   " + it.path + "  " + it.size + "B");
            }
            else
            {
                _lines.Add("FAIL " + it.path + "  " + err);
                if (firstErr == null) firstErr = it.path + ": " + err;
            }
        }

        // ---- 4) 结论 ----
        if (ok == picked.Count && _items.Count >= 591)
        {
            _verdict = "SELF-CHECK PASS  " + ok + "/" + picked.Count;
            _verdictColor = new Color(0.30f, 1f, 0.35f);
            // v3: 自检全绿 → 启动 CRT 资源查看器（接管屏幕）
            StartCoroutine(SpawnViewer());
        }
        else if (ok == picked.Count)
        {
            _verdict = "PASS(抽样) " + ok + "/" + picked.Count + "  清单仅 " + _items.Count + " 项(应591)";
            _verdictColor = new Color(1f, 0.75f, 0.20f);
        }
        else
        {
            _verdict = "SELF-CHECK FAIL  " + ok + "/" + picked.Count;
            _verdictColor = new Color(1f, 0.35f, 0.30f);
            _lines.Add("首个失败: " + firstErr);
        }
        _finished = true;
    }

    // v3: 延迟 2 秒让用户看清自检结果，然后挂上查看器（本文件 GUID 不变，场景零改动）
    IEnumerator SpawnViewer()
    {
        yield return new WaitForSeconds(2.0f);
        gameObject.AddComponent<CrtViewer>();
    }

    void Fail(string msg)
    {
        _verdict = "SELF-CHECK FAIL";
        _verdictColor = new Color(1f, 0.35f, 0.30f);
        _lines.Insert(0, msg);
        _finished = true;
    }

    // StreamingAssets → 可请求 URL（Android: jar:file:///xxx!/assets/...）
    static string SA(string rel)
    {
        string b = Application.streamingAssetsPath;
        if (!b.EndsWith("/")) b += "/";
        if (!b.StartsWith("jar:") && !b.StartsWith("http")) b = "file://" + b;
        return b + rel;
    }

    static string Ext(string p)
    {
        int i = p.LastIndexOf('.');
        return i < 0 ? "" : p.Substring(i + 1).ToLower();
    }

    static bool HeadIs(byte[] d, int off, string magic)
    {
        if (d.Length < off + magic.Length) return false;
        for (int i = 0; i < magic.Length; i++)
            if (d[off + i] != (byte)magic[i]) return false;
        return true;
    }

    private void OnGUI()
    {
        if (CrtViewer.ViewerActive) return;   // v3: 查看器已接管屏幕
        float h = Screen.height;
        float w = Screen.width;

        var big = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(22, (int)(h * 0.045f)),
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = _verdictColor }
        };
        GUI.Label(new Rect(0, h * 0.06f, w, h * 0.12f), _verdict + (_finished ? "" : " ..."), big);

        var small = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(11, (int)(h * 0.018f)),
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = _finished ? Color.white : Color.yellow }
        };
        var sb = new StringBuilder();
        sb.AppendLine("Unity " + Application.unityVersion + " / " + SystemInfo.deviceModel);
        foreach (var l in _lines) sb.AppendLine(l);
        GUI.Label(new Rect(10, h * 0.20f, w - 20, h * 0.78f), sb.ToString(), small);
    }
}
