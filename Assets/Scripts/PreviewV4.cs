// PreviewV4.cs — v4.1 整装拼合预览台(APK数据注入) (2026-10-11)
// ============================================================
// 移植自 crt_pipeline v3.2.1（3603/3603 全绿实证方案, 逐行等价翻译）:
//   ski_core.py     → ski 双格式解析 (老格式 组30B/尾21B + 新格式v3全链+裸流兜底)
//   act_parse.py    → role_*.act 骨架解析 (1.0f 头定位 + 双锚点扫描)
//   mass_produce.py → LBS 烘焙 p=Σw·(R·pos+t), 帧3736站立, 骨架自动匹配
//   DdsLoader.cs    → DDS 解码 (内嵌改名 DdsV4, 避免与工程旧类冲突)
// 修复 v3.9 遗留: 多骨骼穿戴部件(衣/手/靴/头饰)炸裂 = Kabsch对齐路线缺陷,
//               v4 改用 act 骨架池 + 标准蒙皮烘焙 (量产管线同源)
// 纯代码方案: [RuntimeInitializeOnLoadMethod] 免编辑器挂场景
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class PreviewV4 : MonoBehaviour
{
    // ===== 真机校准常量 (验收不对时只改这里重推) =====
    static readonly Vector3 ModelRot = new Vector3(-90f, 0f, 0f); // 源数据 z-up → Unity y-up
    const int FrameStand = 3736;   // 自然站立帧 (与量产 --frame 默认一致)
    const float FitSize = 3.4f;    // 模型归一化最大边

    // ===== 运行状态 =====
    static string actorDir, texDir;
    static readonly string[] Jobs = { "wt", "hj", "mj", "ss" };
    static readonly string[] Sexes = { "m", "f" };
    static int jobI = 0, sexI = 0;
    static string CurJob { get { return Jobs[jobI]; } }
    static string CurSex { get { return Sexes[sexI]; } }
    static string curSki = "";
    static int catI = 2;           // 默认甲袍(衣物, 验收最关键)
    static float rotY = 0f;
    static GameObject pivot, curModel;
    static string title = "", line2 = "", line3 = "";
    static readonly Dictionary<string, ActSkel> skelCache = new Dictionary<string, ActSkel>();
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    static List<string> actPool;   // role_*.act 全池

    static readonly string[] Cats = {
        "武器·刀剑(槽1)", "武器·斧杵(槽2)", "甲袍·防具(3)", "头部(脸)",
        "手套·防具(1)", "长靴·防具(2)", "外装", "其他部件", "怪物/坐骑" };
    static List<string> catFiles = new List<string>();

    // ================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        try { SetupCam(); KillOthers(); Scan(); RebuildCat(); ShowCurrent(); }
        catch (Exception e) { line3 = "[v4.1] 启动失败: " + e.Message; }
        var host = new GameObject("~PreviewV4");
        UnityEngine.Object.DontDestroyOnLoad(host);
        var pv = host.AddComponent<PreviewV4>();
        hostPv = pv;
        pv.StartCoroutine(pv.KillLoop());
    }

    static void SetupCam()
    {
        var cam = Camera.main;
        if (cam == null) cam = new GameObject("PreviewV4Cam").AddComponent<Camera>();
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.transform.position = new Vector3(0f, 1.6f, -5.2f);
        cam.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
        var lg = new GameObject("PreviewV4Light");
        lg.transform.rotation = Quaternion.Euler(42f, -25f, 0f);
        lg.AddComponent<Light>().type = LightType.Directional;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
        pivot = new GameObject("PreviewV4Pivot");
        pivot.transform.position = new Vector3(1.5f, 0f, 0f);
    }

    // ===== 资源路径探测: APK内StreamingAssets(AssetManager同步直读)优先, 外部目录兜底 =====
    static PreviewV4 hostPv;
    static Coroutine _loadCo;
    static bool useAsset = false;
    static AndroidJavaObject _am;
    static AndroidJavaObject AM()
    {
        if (_am == null)
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _am = up.GetStatic<AndroidJavaObject>("currentActivity").Call<AndroidJavaObject>("getAssets");
        }
        return _am;
    }
    static string[] ListAssets(string dir)
    {
        try { return AM().Call<string[]>("list", dir); } catch (Exception) { return new string[0]; }
    }
    static byte[] ReadAsset(string rel)
    {
        AndroidJavaObject is2 = null;
        try
        {
            is2 = AM().Call<AndroidJavaObject>("open", rel, 2);
            using (var bos = new MemoryStream())
            {
                var buf = new sbyte[65536]; int n;
                while ((n = is2.Call<int>("read", buf)) > 0)
                {
                    var tmp = new byte[n];
                    System.Buffer.BlockCopy(buf, 0, tmp, 0, n);
                    bos.Write(tmp, 0, n);
                }
                return bos.ToArray();
            }
        }
        catch (Exception) { return null; }
        finally { if (is2 != null) { try { is2.Call("close"); } catch (Exception) { } is2.Dispose(); } }
    }
    static bool HasAsset(string rel)
    {
        AndroidJavaObject is2 = null;
        try { is2 = AM().Call<AndroidJavaObject>("open", rel, 2); return true; }
        catch (Exception) { return false; }
        finally { if (is2 != null) { try { is2.Call("close"); } catch (Exception) { } is2.Dispose(); } }
    }
    static byte[] ReadActorFile(string fn)
    {
        return useAsset ? ReadAsset("creature/actor/" + fn)
                        : File.ReadAllBytes(Path.Combine(actorDir, fn));
    }
    static byte[] ReadTexFile(string fn)
    {
        return useAsset ? ReadAsset("creature/texture/" + fn)
                        : File.ReadAllBytes(Path.Combine(texDir, fn));
    }
    static bool TexExists(string fn)
    {
        return useAsset ? HasAsset("creature/texture/" + fn)
                        : File.Exists(Path.Combine(texDir, fn));
    }
    static void KillOthers()
    {
        try
        {
            foreach (var b in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (b == null || b.GetType().Name == "PreviewV4") continue;
                b.enabled = false;
            }
        }
        catch (Exception) { }
    }
    System.Collections.IEnumerator KillLoop()
    {
        for (int t = 0; t < 5; t++)
        {
            yield return new WaitForSeconds(0.6f);
            KillOthers();
        }
    }
    static void Scan()
    {
        // 1) APK 内 StreamingAssets (AssetManager 直读, 不依赖文件系统)
        var pool = new List<string>();
        foreach (var n in ListAssets("creature/actor"))
            if (n.StartsWith("role_") && n.EndsWith(".act")) pool.Add(n);
        if (pool.Count > 0)
        {
            pool.Sort();
            actPool = pool; useAsset = true;
            actorDir = "asset:creature/actor"; texDir = "asset:creature/texture";
            title = string.Format("v4.3 整装拼合 · APK数据注入 ({0} 骨架, StreamingAssets)", pool.Count);
            return;
        }
        // 2) 外部目录兜底
        string pd = Application.persistentDataPath;
        string[] cands = {
            "/storage/emulated/0/Game/pak_out/creature/actor",
            pd + "/pak_out/creature/actor",
            pd + "/creature/actor",
        };
        foreach (var c in cands)
        {
            if (!Directory.Exists(c)) continue;
            var acts = Directory.GetFiles(c, "role_*.act");
            if (acts.Length == 0) continue;
            actorDir = c; useAsset = false;
            actPool = new List<string>(acts); actPool.Sort();
            string t = Path.GetDirectoryName(c) + "/texture";
            texDir = Directory.Exists(t) ? t : c;
            title = string.Format("v4.3 整装拼合 · act骨架池+LBS烘焙 ({0} 骨架, 外部目录)", actPool.Count);
            return;
        }
        throw new Exception("未找到资源 (APK内StreamingAssets与外部目录均无 role_*.act)");
    }

    // ===== 池 + 分类 =====
    static void RebuildCat()
    {
        if (actPool == null) { curSkiList.Clear(); curIdx = 0; return; }
        catFiles.Clear();
        IEnumerable<string> all;
        if (useAsset)
        {
            var l = new List<string>();
            foreach (var f in ListAssets("creature/actor")) if (f.EndsWith(".ski")) l.Add(f);
            all = l;
        }
        else all = Directory.GetFiles(actorDir, "*.ski");
        foreach (var f in all)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            bool take = false;
            if (n.StartsWith("a_" + CurJob + "_" + CurSex + "_")) take = true;          // 当前职业部件
            else if (n.StartsWith("w_") && n.Contains("_" + CurJob)) take = true;      // 当前职业武器
            else if (!n.StartsWith("a_") && !n.StartsWith("w_")) take = true;          // 怪物/坐骑/通用
            if (take) catFiles.Add(n);
        }
        catFiles.Sort();
        FilterCat();
    }

    static int CatOf(string n)
    {
        if (n.StartsWith("w_"))
        {
            int u = n.IndexOf('_', 2);
            string seg = u > 2 ? n.Substring(2, u - 2) : n.Substring(2);
            if (seg.EndsWith("c")) return 1;
            return 0;
        }
        if (n.StartsWith("a_"))
        {
            if (n.Contains("_clo_")) return 2;
            if (n.Contains("_hea_")) return 3;
            if (n.Contains("_glo_")) return 4;
            if (n.Contains("_sho_")) return 5;
            if (n.Contains("_wai_")) return 6;
            return 7;
        }
        return 8;
    }

    static void FilterCat()
    {
        List<string> keep = new List<string>();
        foreach (var n in catFiles) if (CatOf(n) == catI) keep.Add(n);
        curSkiList = keep;
        curIdx = 0;
    }
    static List<string> curSkiList = new List<string>();
    static int curIdx = 0;

    // ================================================================
    //                    act 骨架解析 (act_parse.py 等价)
    // ================================================================
    class Bone
    {
        public string name, parent;
        public int nf;
        public float[] trans;  // nf*3
        public float[] quats;  // nf*4 (x,y,z,w)
    }
    class ActSkel { public List<Bone> bones = new List<Bone>(); public string srcName; }

    static ActSkel ParseAct(byte[] d, string srcName)
    {
        if (d.Length < 16 || d[0] != 0xff || d[1] != 0xff)
            throw new Exception("act 头非 CRT stream");
        int tl = U16(d, 2);
        int o = FindPattern(d, tl + 4, new byte[] { 0x00, 0x00, 0x80, 0x3f }); // 1.0f 结束骨架名
        if (o < 0) throw new Exception("act 无 1.0f 头");
        o += 4;
        o += 2;                                              // ver_flag + flag2
        int cnt = U32(d, o); o += 4;
        var sk = new ActSkel(); sk.srcName = srcName;
        int F = d.Length;
        for (int i = 0; i < cnt && o < F; i++)
        {
            int nl = U32(d, o); o += 4;
            string name = Str(d, o, nl); o += nl;
            int pl = U32(d, o); o += 4;
            string parent = Str(d, o, pl); o += pl;
            int nf = (int)U32(d, o); o += 4;
            var b = new Bone(); b.name = name; b.parent = parent; b.nf = nf;
            if (nf == 0)
            {
                o = FindNextAnchor(d, o);                    // 静态节点跳填充
                sk.bones.Add(b);
                continue;
            }
            if (nf < 1 || nf > 60000) throw new Exception("帧数异常@" + name);
            b.trans = new float[nf * 3];
            for (int k = 0; k < nf * 3; k++) { b.trans[k] = F32(d, o); o += 4; }
            int rq = (int)U32(d, o); o += 4;
            if (rq != nf) throw new Exception("帧数不匹配@" + name);
            b.quats = new float[nf * 4];
            for (int k = 0; k < nf * 4; k++) { b.quats[k] = F32(d, o); o += 4; }
            o = FindNextAnchor(d, o);
            sk.bones.Add(b);
        }
        return sk;
    }

    // 双锚点扫描: u32 len(1..20)+printable × 2 (act_parse.find_next_anchor)
    static int FindNextAnchor(byte[] d, int o)
    {
        int F = d.Length;
        for (int p = o; p < F - 12; p++)
        {
            int nl = U32(d, p);
            if (nl < 1 || nl > 20 || p + 4 + nl + 4 > F) continue;
            if (!Printable(d, p + 4, nl)) continue;
            int pl = U32(d, p + 4 + nl);
            if (pl < 1 || pl > 20 || p + 4 + nl + 4 + pl > F) continue;
            if (!Printable(d, p + 8 + nl, pl)) continue;
            return p;
        }
        return F;
    }

    // 四元数(x,y,z,w) → 3x3 行主序 (act_parse.quat_to_mat)
    static void QuatToMat(float[] q, float[] R) // R: 9 (行主序)
    {
        float x = q[0], y = q[1], z = q[2], w = q[3];
        float n = x * x + y * y + z * z + w * w;
        if (n < 1e-12f) { R[0] = 1; R[1] = 0; R[2] = 0; R[3] = 0; R[4] = 1; R[5] = 0; R[6] = 0; R[7] = 0; R[8] = 1; return; }
        float s = 2f / n;
        R[0] = 1 - s * (y * y + z * z); R[1] = s * (x * y - z * w);     R[2] = s * (x * z + y * w);
        R[3] = s * (x * y + z * w);     R[4] = 1 - s * (x * x + z * z); R[5] = s * (y * z - x * w);
        R[6] = s * (x * z - y * w);     R[7] = s * (y * z + x * w);     R[8] = 1 - s * (x * x + y * y);
    }

    class FramePose { public float[] R = new float[9]; public float tx, ty, tz; }

    // mass_produce.load_skeleton: 每骨骼取指定帧本地 (R, t), 无父级复合
    static List<FramePose> LoadSkeleton(ActSkel sk)
    {
        var W = new List<FramePose>(sk.bones.Count);
        foreach (var b in sk.bones)
        {
            var fp = new FramePose();
            if (b.nf > 0)
            {
                int f = FrameStand >= b.nf ? b.nf - 1 : FrameStand;   // clamp 保护
                QuatToMat(new[] { b.quats[f * 4], b.quats[f * 4 + 1], b.quats[f * 4 + 2], b.quats[f * 4 + 3] }, fp.R);
                fp.tx = b.trans[f * 3]; fp.ty = b.trans[f * 3 + 1]; fp.tz = b.trans[f * 3 + 2];
            }
            else { fp.R[0] = fp.R[4] = fp.R[8] = 1f; }               // 静态骨恒等
            W.Add(fp);
        }
        return W;
    }

    // ===== 骨架匹配 (mass_produce: a_(职业)_(性别)_ → role_x_y_*.act, 否则静态) =====
    static ActSkel MatchSkel(string skiName, out string tag)
    {
        tag = "STATIC";
        var m = Regex.Match(skiName, @"^a_([a-z]+)_([fm])_");
        if (!m.Success) return null;                                 // 怪物/武器/宠物 → 静态
        string want = "role_" + m.Groups[1].Value + "_" + m.Groups[2].Value + "_";
        string path = null;
        foreach (var p in actPool)
        {
            string fn = Path.GetFileName(p);
            if (fn.StartsWith(want)) { path = p; break; }
        }
        if (path == null)
        {
            path = actPool[0];                                       // fallback 首个骨架
            tag = "FALLBACK->" + Path.GetFileName(path);
        }
        else tag = "OK";
        ActSkel sk;
        if (!skelCache.TryGetValue(path, out sk))
        {
            sk = ParseAct(ReadActorFile(Path.GetFileName(path)), Path.GetFileName(path));
            skelCache[path] = sk;
        }
        return sk;
    }

    // ================================================================
    //                    ski 解析 (ski_core.py 等价)
    // ================================================================
    class Vtx { public byte[] bid; public float[] w; public Vector3[] pos; }

    class SkiData
    {
        public List<Vtx> verts = new List<Vtx>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> tris = new List<int>();
        public string anchor = "", fmt = "";
        public int rawN;
    }

    static int U32(byte[] d, int o) { return (int)(BitConverter.ToUInt32(d, o)); }
    static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }
    static float F32(byte[] d, int o) { return BitConverter.ToSingle(d, o); }
    static bool Printable(byte[] d, int o, int n)
    {
        for (int i = 0; i < n; i++) { int c = d[o + i]; if (c < 32 || c >= 127) return false; }
        return true;
    }
    static string Str(byte[] d, int o, int n) { return Encoding.UTF8.GetString(d, o, n); }

    static int IndexOf(byte[] d, string pat, int from)
    {
        byte[] p = Encoding.ASCII.GetBytes(pat);
        for (int i = Math.Max(0, from); i <= d.Length - p.Length; i++)
        {
            int j = 0;
            for (; j < p.Length; j++) if (d[i + j] != p[j]) break;
            if (j == p.Length) return i;
        }
        return -1;
    }

    // byte 模式搜索 (含 >127 字节, 不能走 ASCII 字符串)
    static int FindPattern(byte[] d, int from, byte[] p)
    {
        for (int i = Math.Max(0, from); i <= d.Length - p.Length; i++)
        {
            int j = 0;
            for (; j < p.Length; j++) if (d[i + j] != p[j]) break;
            if (j == p.Length) return i;
        }
        return -1;
    }

    // ---- 老格式顶点链 walk: 组30B(bone u8+w f32+pos3f+nrm3f+pad1B) 尾21B(flag+tang3f+uv2f) ----
    // 返回: 法线合格率; 失败 verts=null
    static float Walk(byte[] d, int start, int vc, out int endP, List<Vtx> verts, List<Vector2> uvs)
    {
        int p = start; long ok = 0, tot = 0; int F = d.Length;
        endP = p; verts.Clear(); if (uvs != null) uvs.Clear();
        for (int i = 0; i < vc; i++)
        {
            if (p + 4 > F) { endP = p; return -1f; }
            int nb = U32(d, p); p += 4;
            if (nb < 1 || nb > 8) { endP = p; return -1f; }
            var v = new Vtx(); v.bid = new byte[nb]; v.w = new float[nb]; v.pos = new Vector3[nb];
            for (int j = 0; j < nb; j++)
            {
                if (p + 29 > F) { endP = p; return -1f; }
                byte bone = d[p]; float w = F32(d, p + 1);
                if (bone > 128 || w < 0f || w > 1.02f) { endP = p; return -1f; }
                float x = F32(d, p + 5), y = F32(d, p + 9), z = F32(d, p + 13);
                if (x <= -60f || x >= 60f || y <= -60f || y >= 60f || z <= -60f || z >= 60f) { endP = p; return -1f; }
                float nx = F32(d, p + 17), ny = F32(d, p + 21), nz = F32(d, p + 25);
                float n2 = nx * nx + ny * ny + nz * nz;
                tot++; if (n2 > 0.8f && n2 < 1.2f) ok++;
                v.bid[j] = bone; v.w[j] = w; v.pos[j] = new Vector3(x, y, z);
                p += 30;
            }
            if (p + 21 <= F)
            {
                p += 1;                                        // flag
                p += 12;                                       // tangent (不入 mesh)
                if (uvs != null) uvs.Add(new Vector2(F32(d, p), F32(d, p + 4)));
                p += 8;
            }
            else { if (uvs != null) uvs.Add(Vector2.zero); p += 21; }
            verts.Add(v);
        }
        endP = p;
        return tot > 0 ? (float)ok / tot : 0f;
    }

    // ---- find_chain: MaterialLib 后 0x800 内扫 vc 声明 → 老格式 walk ----
    static bool FindChain(byte[] d, out int mtl, out int chainP, out int vc, List<Vtx> verts, List<Vector2> uvs)
    {
        mtl = IndexOf(d, "CRT_MaterialLib", 0);
        if (mtl < 0) mtl = IndexOf(d, "CRT_Mtl", 0);
        chainP = -1; vc = 0;
        if (mtl < 0) return false;
        int F = d.Length;
        int hi = Math.Min(mtl + 0x800, F - 64);
        for (int p = mtl + 4; p < hi; p++)
        {
            int v = U32(d, p);
            if (v <= 3 || v >= 4000) continue;
            bool ok5 = true;
            for (int k = 0; k < 5; k++) if (Math.Abs(F32(d, p + 4 + 4 * k)) >= 100f) { ok5 = false; break; }
            if (!ok5) continue;
            int u = U32(d, p + 24);
            if (u < 1 || u > 4) continue;
            int endP;
            float rate = Walk(d, p + 24, v, out endP, verts, uvs);
            if (rate >= 0f && rate > 0.9f)
            {
                int gap = endP - F;
                if (gap >= -2 && gap <= 23) { chainP = p; vc = v; return true; }
            }
        }
        return false;
    }

    // ---- 老格式索引定位 (MaterialLib 前 0x4000) ----
    static bool FindIndices(byte[] d, int mtl, out int ih, out int icnt, out int idx0)
    {
        ih = -1; icnt = 0; idx0 = 0;
        int segEnd = mtl - 4, lo = Math.Max(0x20, mtl - 0x4000);
        for (int o = lo; o < segEnd - 16; o++)
        {
            int c = U32(d, o);
            int n = (segEnd - o - 4) / 2;
            if (c <= 3 || c > n + 4 || Math.Abs(c - n) > 4) continue;
            int chk = Math.Min(8, n);
            bool ok = true;
            for (int i = 0; i < chk; i++)
            {
                int v = U16(d, o + 4 + 2 * i);
                if (v >= 0x0fff && v != 0xffff) { ok = false; break; }
            }
            if (!ok) continue;
            ih = o; icnt = c; idx0 = o + 4; return true;
        }
        return false;
    }

    // ---- 新格式 walk2: 组30B/尾21B, pos 放宽 ±500, 支持部分解析 ----
    static float Walk2(byte[] d, int start, int vc, bool allowPartial, out int endP, List<Vtx> verts, List<Vector2> uvs)
    {
        int p = start; long ok = 0, tot = 0; int F = d.Length;
        endP = p; verts.Clear(); if (uvs != null) uvs.Clear();
        for (int i = 0; i < vc; i++)
        {
            if (p + 4 > F)
            {
                if (allowPartial && verts.Count >= 3) { endP = p; return tot > 0 ? (float)ok / tot : 0f; }
                endP = p; return -1f;
            }
            int nb = U32(d, p); p += 4;
            if (nb < 1 || nb > 8)
            {
                if (allowPartial && verts.Count >= 3) { endP = p; return tot > 0 ? (float)ok / tot : 0f; }
                endP = p; return -1f;
            }
            var v = new Vtx(); v.bid = new byte[nb]; v.w = new float[nb]; v.pos = new Vector3[nb];
            bool bad = false;
            for (int j = 0; j < nb; j++)
            {
                if (p + 29 > F) { bad = true; break; }
                byte bone = d[p]; float w = F32(d, p + 1);
                if (bone > 200 || w < 0f || w > 1.02f) { bad = true; break; }
                float x = F32(d, p + 5), y = F32(d, p + 9), z = F32(d, p + 13);
                if (x <= -500f || x >= 500f || y <= -500f || y >= 500f || z <= -500f || z >= 500f) { bad = true; break; }
                float nx = F32(d, p + 17), ny = F32(d, p + 21), nz = F32(d, p + 25);
                float n2 = nx * nx + ny * ny + nz * nz;
                tot++; if (n2 > 0.8f && n2 < 1.2f) ok++;
                v.bid[j] = bone; v.w[j] = w; v.pos[j] = new Vector3(x, y, z);
                p += 30;
            }
            if (bad)
            {
                if (allowPartial && verts.Count >= 3) { endP = p; return tot > 0 ? (float)ok / tot : 0f; }
                endP = p; return -1f;
            }
            if (p + 21 <= F)
            {
                p += 21;                                       // flag + tangent + uv
                if (uvs != null) uvs.Add(new Vector2(F32(d, p - 8), F32(d, p - 4)));
            }
            else
            {
                if (allowPartial && verts.Count >= 3) { endP = p; return tot > 0 ? (float)ok / tot : 0f; }
                endP = p; return -1f;
            }
            verts.Add(v);
        }
        endP = p;
        return tot > 0 ? (float)ok / tot : 0f;
    }

    // ---- 裸顶点流兜底 (无 vc 声明, 逐顶点校验直到失败) ----
    static float WalkFree(byte[] d, int start, out int endP, List<Vtx> verts, List<Vector2> uvs)
    {
        int p = start; long ok = 0, tot = 0; int F = d.Length;
        endP = p; verts.Clear(); if (uvs != null) uvs.Clear();
        while (p + 55 <= F)
        {
            int nb = U32(d, p);
            if (nb < 1 || nb > 8) break;
            int q = p + 4; bool good = true;
            var v = new Vtx(); v.bid = new byte[nb]; v.w = new float[nb]; v.pos = new Vector3[nb];
            for (int j = 0; j < nb; j++)
            {
                if (q + 30 > F) { good = false; break; }
                byte bone = d[q]; float w = F32(d, q + 1);
                if (bone > 200 || w < 0f || w > 1.02f) { good = false; break; }
                float x = F32(d, q + 5), y = F32(d, q + 9), z = F32(d, q + 13);
                if (x <= -500f || x >= 500f || y <= -500f || y >= 500f || z <= -500f || z >= 500f) { good = false; break; }
                float nx = F32(d, q + 17), ny = F32(d, q + 21), nz = F32(d, q + 25);
                float n2 = nx * nx + ny * ny + nz * nz;
                tot++; if (n2 > 0.8f && n2 < 1.2f) ok++;
                v.bid[j] = bone; v.w[j] = w; v.pos[j] = new Vector3(x, y, z);
                q += 30;
            }
            if (!good) break;
            if (q + 21 > F) break;
            q += 1;                                            // flag
            q += 12;                                           // tangent
            if (uvs != null) uvs.Add(new Vector2(F32(d, q), F32(d, q + 4)));
            q += 8;
            verts.Add(v); p = q;
        }
        endP = p;
        float rate = tot > 0 ? (float)ok / tot : 0f;
        return (verts.Count >= 30 && rate >= 0.85f) ? rate : -1f;
    }

    // ---- 新格式全解析 (parse_ski_v3): 索引表前置 + partN 链 + MaterialLib 后顶点块 ----
    static SkiData ParseSkiV3(byte[] d)
    {
        var sd = new SkiData();
        int F = d.Length;
        int mtl = IndexOf(d, "CRT_MaterialLib", 0);
        if (mtl < 0) throw new Exception("新格式: 无 MaterialLib");
        // A. 索引表区
        int p1 = -1;
        int hiA = Math.Min(0x160, mtl - 16);
        for (int p = 0x10; p < hiA; p++)
        {
            int v = U32(d, p);
            if (v < 3 || v > 30000) continue;
            if (p + 4 + 2 * v > mtl + 8) continue;
            bool ok = true;
            int chk = Math.Min(16, v);
            for (int i = 0; i < chk; i++)
            {
                int w = U16(d, p + 4 + 2 * i);
                if (w >= 8192 && w != 0xffff) { ok = false; break; }
            }
            if (!ok) continue;
            int end = p + 4 + 2 * v;
            if (end + 8 >= mtl || d[end] == 0x01 || U32(d, end) <= 1) { p1 = p; break; }
        }
        if (p1 < 0) throw new Exception("新格式: 索引表头未定位");
        int cur = p1 + 4 + 2 * U32(d, p1);
        int nparts = 1;
        while (cur + 20 < mtl && nparts < 32)
        {
            while (cur < mtl && d[cur] == 0) cur++;
            if (cur >= mtl - 20 || d[cur] != 0x01) break;
            int q = cur + 1; bool good = true;
            for (int t = 0; t < 2; t++)
            {
                int ln = U32(d, q);
                if (ln < 2 || ln > 48 || q + 4 + ln > F) { good = false; break; }
                if (!Printable(d, q + 4, ln)) { good = false; break; }
                q += 4 + ln;
            }
            if (!good) break;
            int icnt = U32(d, q);
            if (icnt < 3 || icnt > 30000 || q + 4 + 2 * icnt > mtl + 8) break;
            nparts++;
            cur = q + 4 + 2 * icnt;
        }
        int idxEnd = cur;
        // B. 顶点区
        var tVerts = new List<Vtx>(); var tUvs = new List<Vector2>();
        var tmpV = new List<Vtx>(); var tmpU = new List<Vector2>();
        int pB = mtl;
        while (pB < F - 64)
        {
            bool advanced = false;
            int ln = U32(d, pB);
            string name = null;
            if (ln >= 4 && ln <= 40 && pB + 4 + ln <= F)
            {
                if (Printable(d, pB + 4, ln)) name = Str(d, pB + 4, ln);
            }
            if (name != null)
            {
                int qHi = Math.Min(pB + 4 + ln + 260, F - 64);
                for (int q = pB + 4 + ln; q < qHi; q++)
                {
                    int vc = U32(d, q);
                    if (vc < 3 || vc > 30000) continue;
                    int sHi = Math.Min(q + 4 + 80, F - 64);
                    for (int s0 = q + 4; s0 < sHi; s0++)
                    {
                        int end2;
                        float r = Walk2(d, s0, vc, true, out end2, tmpV, tmpU);
                        if (r >= 0f && r >= 0.85f)
                        {
                            tVerts.AddRange(tmpV); tUvs.AddRange(tmpU);
                            pB = end2; advanced = true; break;
                        }
                    }
                    if (advanced) break;
                }
                if (advanced) continue;
                pB += 4 + ln; continue;
            }
            // 无名字块: vc续排 / 裸流 / 前进1B
            bool hit = false;
            int vc2 = U32(d, pB);
            if (vc2 >= 3 && vc2 <= 30000)
            {
                int sHi = Math.Min(pB + 4 + 120, F - 64);
                for (int s0 = pB + 4; s0 < sHi; s0++)
                {
                    int end2;
                    float r = Walk2(d, s0, vc2, true, out end2, tmpV, tmpU);
                    if (r >= 0f && r >= 0.85f)
                    {
                        tVerts.AddRange(tmpV); tUvs.AddRange(tmpU);
                        pB = end2; hit = true; break;
                    }
                }
            }
            if (!hit)
            {
                int end2;
                float r = WalkFree(d, pB, out end2, tmpV, tmpU);
                if (r >= 0f) { tVerts.AddRange(tmpV); tUvs.AddRange(tmpU); pB = end2; hit = true; }
            }
            if (hit) continue;
            pB += 1;
        }
        if (tVerts.Count == 0) throw new Exception("新格式: 顶点区未解析");
        // C. 索引 → 三角 (全局池)
        int vtot = tVerts.Count;
        var raw = new List<int>();
        int n1 = U32(d, p1);
        for (int i = 0; i < n1; i++) raw.Add(U16(d, p1 + 4 + 2 * i));
        cur = p1 + 4 + 2 * U32(d, p1);
        while (cur + 20 < idxEnd)
        {
            while (cur < idxEnd && d[cur] == 0) cur++;
            if (cur >= idxEnd - 20 || d[cur] != 0x01) break;
            int q = cur + 1;
            for (int t = 0; t < 2; t++)
            {
                int l2 = U32(d, q);
                if (l2 < 2 || l2 > 48) break;
                q += 4 + l2;
            }
            int ic = U32(d, q);
            if (ic >= 3 && ic <= 30000 && q + 4 + 2 * ic <= idxEnd + 8)
            {
                for (int i = 0; i < ic; i++) raw.Add(U16(d, q + 4 + 2 * i));
                cur = q + 4 + 2 * ic;
            }
            else break;
        }
        for (int i = 0; i + 2 < raw.Count; i += 3)
        {
            int a = raw[i], b = raw[i + 1], c = raw[i + 2];
            if (a < vtot && b < vtot && c < vtot) { sd.tris.Add(a); sd.tris.Add(b); sd.tris.Add(c); }
        }
        sd.verts = tVerts; sd.uv = tUvs; sd.rawN = raw.Count;
        double lost = sd.rawN > 0 ? 1.0 - (sd.tris.Count * 3.0 / sd.rawN) : 0;
        sd.fmt = "new(" + nparts + "段)" + (lost > 0.05 ? string.Format(" PARTIAL{0:P0}", lost) : "");
        return sd;
    }

    static SkiData ParseSki(byte[] d)
    {
        var sd = new SkiData();
        int mtl, chainP, vc;
        var verts = new List<Vtx>(); var uvs = new List<Vector2>();
        sd.fmt = "old";
        bool okOld = FindChain(d, out mtl, out chainP, out vc, verts, uvs);
        List<int> tris = null;
        if (okOld)
        {
            int ih, icnt, idx0;
            if (FindIndices(d, mtl, out ih, out icnt, out idx0) && icnt >= Math.Max(12, vc / 2))
            {
                tris = new List<int>();
                for (int i = 0; i + 2 < icnt; i += 3)
                {
                    int a = U16(d, idx0 + 2 * i), b = U16(d, idx0 + 2 * (i + 1)), c = U16(d, idx0 + 2 * (i + 2));
                    if (a < vc && b < vc && c < vc) { tris.Add(a); tris.Add(b); tris.Add(c); }
                }
            }
        }
        if (tris == null || tris.Count == 0 || verts.Count == 0)
        {
            var v3 = ParseSkiV3(d);
            sd.verts = v3.verts; sd.uv = v3.uv; sd.tris = v3.tris; sd.fmt = v3.fmt; sd.rawN = v3.rawN;
        }
        else { sd.verts = verts; sd.uv = uvs; sd.tris = tris; }
        sd.anchor = ExtractAnchor(d);
        if (sd.verts.Count == 0) throw new Exception("0 顶点");
        if (sd.tris.Count == 0) throw new Exception("0 三角");
        return sd;
    }

    // ---- 贴图锚名 (mass_produce v5 定案): CRT_Mtl(Standard|Mu) 后 160B 内首个非 CRT/0_ 串 ----
    static string ExtractAnchor(byte[] d)
    {
        int m = IndexOf(d, "CRT_MtlStandard", 0);
        int m2 = IndexOf(d, "CRT_MtlMu", 0);
        int baseP = (m >= 0 && (m2 < 0 || m < m2)) ? m : m2;
        if (baseP < 0) return "";
        int s = baseP + 15, hi = Math.Min(s + 160, d.Length);
        int i = s;
        while (i < hi)
        {
            int j = i;
            while (j < hi && IsW(d[j])) j++;
            if (j - i >= 4 && j - i <= 32)
            {
                string cand = Encoding.ASCII.GetString(d, i, j - i);
                if (!cand.StartsWith("CRT") && !cand.StartsWith("0_")) return cand;
            }
            i = (j > i) ? j : i + 1;
        }
        return "";
    }
    static bool IsW(byte c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'; }

    // ---- resolve_tex 链: 原名 → 剥尾字符 → 层后缀 → 无下划线 ----
    static string ResolveTex(string anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return null;
        string n = anchor.ToLower();
        var cands = new List<string>();
        cands.Add(n);
        if (n.Length > 1) cands.Add(n.Substring(0, n.Length - 1));
        var mm = Regex.Match(n, @"^(.+?)[_\-]?[aA][lLtTgG]?$");
        if (mm.Success && mm.Groups[1].Value.Length > 0) cands.Add(mm.Groups[1].Value);
        cands.Add(n.Replace("_", ""));
        foreach (var c in cands)
        {
            foreach (var e in new[] { ".dds", ".png" })
            {
                if (TexExists(c + e)) return c + e;
            }
        }
        return null;
    }

    static Texture2D LoadTex(string anchor)
    {
        if (texCache.ContainsKey(anchor ?? "")) return texCache[anchor ?? ""];
        string p = ResolveTex(anchor);
        Texture2D tex = null;
        if (p != null)
        {
            try
            {
                byte[] raw = ReadTexFile(p);
            if (raw == null) throw new Exception("tex miss");
                if (p.EndsWith(".dds")) tex = DdsV4.LoadFromMemory(raw, Path.GetFileNameWithoutExtension(p));
                else
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(raw)) tex = null;
                }
            }
            catch (Exception) { tex = null; }
        }
        texCache[anchor ?? ""] = tex;
        return tex;
    }

    // ================================================================
    //                    LBS 烘焙 (mass_produce.bake_vertex 等价)
    // ================================================================
    static Vector3 BakeVertex(List<FramePose> W, Vtx v)
    {
        Vector3 acc = Vector3.zero;
        for (int j = 0; j < v.bid.Length; j++)
        {
            byte bid = v.bid[j]; float w = v.w[j]; Vector3 pos = v.pos[j];
            if (W == null || bid >= W.Count) { acc += w * pos; continue; }
            var fp = W[bid];
            acc += w * new Vector3(
                fp.R[0] * pos.x + fp.R[1] * pos.y + fp.R[2] * pos.z + fp.tx,
                fp.R[3] * pos.x + fp.R[4] * pos.y + fp.R[5] * pos.z + fp.ty,
                fp.R[6] * pos.x + fp.R[7] * pos.y + fp.R[8] * pos.z + fp.tz);
        }
        return acc;
    }

    // ================================================================
    //                    展示
    // ================================================================
    static void ShowCurrent()
    {
        if (curSkiList.Count == 0) { line2 = "(本分类当前职业无模型)"; return; }
        if (curIdx >= curSkiList.Count) curIdx = 0;
        curSki = curSkiList[curIdx];
        StartShow(curSki);
    }

    static void StartShow(string name)
    {
        line2 = name + "  <" + Cats[catI] + ">  [" + CurJob + "_" + CurSex + "]";
        if (hostPv == null) return;
        if (_loadCo != null) hostPv.StopCoroutine(_loadCo);
        _loadCo = hostPv.StartCoroutine(LoadShowCo(name));
    }

    static System.Collections.IEnumerator LoadShowCo(string name)
    {
        byte[] d; string err = "";
        if (useAsset)
        {
            using (var wr = UnityWebRequest.Get(Application.streamingAssetsPath + "/creature/actor/" + name + ".ski"))
            {
                yield return wr.SendWebRequest();
                if (wr.result != UnityWebRequest.Result.Success) { d = null; err = wr.error; }
                else d = wr.downloadHandler.data;
            }
        }
        else
        {
            try { d = File.ReadAllBytes(Path.Combine(actorDir, name + ".ski")); }
            catch (Exception e2) { d = null; err = e2.Message; }
        }
        ShowSkiData(name, d, err);
    }

    static void ShowSkiData(string name, byte[] d, string err)
    {
        try
        {
            if (d == null) throw new Exception("ski 读取失败: " + err);
            SkiData sd = ParseSki(d);
            string tag;
            ActSkel sk = MatchSkel(name, out tag);
            List<FramePose> W = (sk != null) ? LoadSkeleton(sk) : null;

            // 烘焙 → mesh
            int vc = sd.verts.Count;
            var vs = new Vector3[vc];
            for (int i = 0; i < vc; i++) vs[i] = BakeVertex(W, sd.verts[i]);
            var mesh = new Mesh();
            mesh.vertices = vs;
            mesh.uv = sd.uv.GetRange(0, Math.Min(sd.uv.Count, vc)).ToArray();
            mesh.triangles = sd.tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 包围盒归一化
            var b = mesh.bounds;
            float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            float s = ext > 1e-5f ? FitSize / ext : 1f;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localScale = new Vector3(s, s, s);
            curModel.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            curModel.transform.localRotation = Quaternion.Euler(ModelRot);
            curModel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = curModel.AddComponent<MeshRenderer>();

            // Shader 保底链 (Standard 可能被构建裁剪)
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Diffuse");
            if (sh == null) sh = Shader.Find("Mobile/Diffuse");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) throw new Exception("无可用Shader(构建裁剪)");
            var mat = new Material(sh);
            mat.color = new Color(0.72f, 0.72f, 0.76f);
            mr.sharedMaterial = mat;

            line3 = string.Format("[v4] {0} {1} 顶点{2} 面{3} ski:{4}B | 骨架={5} | 贴图=...",
                sd.fmt, tag == "STATIC" ? "静态" : tag + ":" + (sk != null ? sk.srcName : ""),
                vc, sd.tris.Count / 3, d.Length,
                sk != null ? sk.srcName : "无");

            // 贴图异步加载 (加载完成自动贴上)
            if (!string.IsNullOrEmpty(sd.anchor) && hostPv != null)
                hostPv.StartCoroutine(LoadTexCo(sd.anchor, mat));
        }
        catch (Exception e)
        {
            line3 = "[v4] " + name + " 解析失败: " + e.Message;
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
        }
    }

    static System.Collections.IEnumerator LoadTexCo(string anchor, Material mat)
    {
        string key = anchor ?? "";
        if (texCache.TryGetValue(key, out var tc) && tc != null)
        {
            if (mat != null) { mat.mainTexture = tc; mat.color = Color.white; }
            line3 += " ✓(缓存)";
            yield break;
        }
        string p = ResolveTex(anchor);
        if (p == null) { line3 += " ✗未找到"; yield break; }
        byte[] raw = null;
        if (useAsset)
        {
            using (var wr = UnityWebRequest.Get(Application.streamingAssetsPath + "/creature/texture/" + p))
            {
                yield return wr.SendWebRequest();
                if (wr.result == UnityWebRequest.Result.Success) raw = wr.downloadHandler.data;
            }
        }
        else
        {
            string fp = Path.Combine(texDir, p);
            if (File.Exists(fp)) { try { raw = File.ReadAllBytes(fp); } catch (Exception) { raw = null; } }
        }
        Texture2D t = null;
        if (raw != null)
        {
            if (p.EndsWith(".dds")) t = DdsV4.LoadFromMemory(raw, Path.GetFileNameWithoutExtension(p));
            else { t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (!t.LoadImage(raw)) t = null; }
        }
        if (t != null)
        {
            texCache[key] = t;
            if (mat != null) { mat.mainTexture = t; mat.color = Color.white; }
            line3 += " ✓" + p;
        }
        else line3 += " ✗解码失败";
    }

    // ================================================================
    //                    UI (OnGUI, 1080 设计宽缩放)
    // ================================================================
    static GUIStyle box, label, btn, small;
    static void InitStyle()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box); box.fontSize = 22; box.alignment = TextAnchor.MiddleLeft;
        label = new GUIStyle(GUI.skin.label); label.fontSize = 22;
        small = new GUIStyle(GUI.skin.label); small.fontSize = 16; small.wordWrap = true;
        btn = new GUIStyle(GUI.skin.button); btn.fontSize = 20; btn.alignment = TextAnchor.MiddleLeft;
    }

    void OnGUI() { DrawUI(); }
    static void DrawUI()
    {
        InitStyle();
        float sc = Screen.width / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sc, sc, 1f));
        float H = Screen.height / sc;   // 横屏约 508

        // 顶栏 (紧凑, 状态右置)
        GUI.Box(new Rect(0, 0, 1080, 86), GUIContent.none, box);
        GUI.Label(new Rect(12, 4, 700, 40), title, label);
        GUI.Label(new Rect(12, 42, 700, 40), line2, label);
        GUI.Label(new Rect(716, 8, 356, 74), line3, small);

        // 分类: 两列网格 (左侧, 4行)
        float cw = 250f, ch = 48f;
        GUI.Box(new Rect(0, 88, 2 * cw + 24, 4 * (ch + 4) + 10), GUIContent.none, box);
        for (int i = 0; i < Cats.Length; i++)
        {
            int r = i / 2, cc = i % 2;
            var old = GUI.backgroundColor;
            if (i == catI) GUI.backgroundColor = new Color(0.30f, 0.65f, 0.95f);
            if (GUI.Button(new Rect(8 + cc * (cw + 8), 92 + r * (ch + 4), cw, ch), (i == catI ? "\u25b6 " : "") + Cats[i], i == catI ? btn : small))
            { catI = i; FilterCat(); ShowCurrent(); }
            GUI.backgroundColor = old;
        }

        // 职业按钮 (右上)
        if (GUI.Button(new Rect(1080 - 300, 94, 292, 52), "职业 " + CurJob + "_" + CurSex + " \u25b6", btn))
        {
            sexI++;
            if (sexI >= Sexes.Length) { sexI = 0; jobI = (jobI + 1) % Jobs.Length; }
            skelCache.Clear(); rotY = 0;
            RebuildCat(); ShowCurrent();
        }

        // 底部翻页条
        float yb = H - 62;
        GUI.Box(new Rect(0, yb - 6, 1080, 68), GUIContent.none, box);
        if (GUI.Button(new Rect(10, yb, 90, 54), "\u25c0", btn))
        { curIdx = (curIdx - 1 + curSkiList.Count) % Math.Max(1, curSkiList.Count); ShowCurrent(); }
        if (GUI.Button(new Rect(108, yb, 90, 54), "\u25b6", btn))
        { curIdx = (curIdx + 1) % Math.Max(1, curSkiList.Count); ShowCurrent(); }
        GUI.Label(new Rect(210, yb, 700, 54), curSkiList.Count == 0
            ? "(本职业无此部件) 点右上「职业」换人 / 点左侧其他分类"
            : string.Format("{0}/{1}  {2}", curIdx + 1, curSkiList.Count, curSki), label);

        // 拖动旋转
        var e = Event.current;
        if (e.type == EventType.MouseDrag && e.button == 0) { rotY += e.delta.x * 0.45f; ApplyRot(); e.Use(); }
    }

    static void ApplyRot()
    {
        if (pivot != null) pivot.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
    }
}

// ================================================================
//  DdsV4 — DDS 解码 (DdsLoader.cs 内容内嵌改名, 避免与工程旧类冲突)
//  支持: DXT1/DXT3/DXT5 + 未压缩 + 2004 非标准头自动修补
// ================================================================
public static class DdsV4
{
    public static Texture2D LoadFromMemory(byte[] raw, string name)
    {
        try { return Inner(raw, name); }
        catch (Exception) { return null; }
    }

    static Texture2D Inner(byte[] raw, string name)
    {
        if (raw == null || raw.Length < 64) return null;
        int magic = raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24);
        int dataOff, width, height, fourcc; uint pfFlags;
        if (magic == 0x20534444 && raw.Length >= 128)
        {
            dataOff = 128;
            height = BitConverter.ToInt32(raw, 12);
            width = BitConverter.ToInt32(raw, 16);
            fourcc = BitConverter.ToInt32(raw, 84);
            pfFlags = (uint)BitConverter.ToInt32(raw, 80);
        }
        else
        {
            dataOff = 0;
            int body = raw.Length, blocks = body / 8;
            int side = blocks > 0 ? (int)Math.Sqrt(blocks * 16.0) : 256;
            if (side < 16 || side > 4096 || blocks * 8 != body) side = 256;
            width = height = side; fourcc = 0x31545844;
            if ((side & (side - 1)) != 0)
            {
                blocks = body / 16;
                side = blocks > 0 ? (int)Math.Sqrt(blocks * 16.0) : 256;
                if (side >= 16 && side <= 4096 && (side & (side - 1)) == 0 && blocks * 16 == body)
                { width = height = side; fourcc = 0x35545844; }
                else { width = height = 256; fourcc = 0x31545844; }
            }
            pfFlags = 0x4;
        }
        if ((pfFlags & 0x4) != 0)
        {
            if (fourcc == 0x31545844) return DecodeDXT(raw, dataOff, width, height, false, name, 0, false);
            if (fourcc == 0x33545844) return DecodeDXT(raw, dataOff, width, height, true, name, 8, false);
            if (fourcc == 0x35545844) return DecodeDXT(raw, dataOff, width, height, true, name, 8, true);
            return null;
        }
        bool hasAlpha = (pfFlags & 0x1) != 0 || (pfFlags & 0x2) != 0;
        return DecodeRaw(raw, dataOff, width, height, hasAlpha, name);
    }

    static Texture2D DecodeDXT(byte[] raw, int off, int w, int h, bool hasAlpha, string name, int alphaBytes, bool dxt5)
    {
        var px = new Color32[w * h];
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        int stride = 8 + alphaBytes; int p = off;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                if (p + stride > raw.Length) goto done;
                byte[] alpha = null;
                if (hasAlpha)
                {
                    alpha = new byte[16];
                    if (dxt5)
                    {
                        int a0 = raw[p], a1 = raw[p + 1];
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)raw[p + 2 + i] << (8 * i);
                        byte[] lut = AlphaLut(a0, a1);
                        for (int i = 0; i < 16; i++) alpha[i] = lut[(bits >> (3 * i)) & 0x7];
                    }
                    else
                        for (int i = 0; i < 16; i++)
                            alpha[i] = (byte)(((raw[p + i / 2] >> ((i % 2) * 4)) & 0xF) * 17);
                }
                int cp = p + alphaBytes;
                int c0 = raw[cp] | (raw[cp + 1] << 8);
                int c1 = raw[cp + 2] | (raw[cp + 3] << 8);
                uint idx = (uint)(raw[cp + 4] | (raw[cp + 5] << 8) | (raw[cp + 6] << 16) | (raw[cp + 7] << 24));
                var pal = Pal(c0, c1, hasAlpha && !dxt5);
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                    if (x >= w || y >= h) continue;
                    var c = pal[(idx >> (2 * i)) & 0x3];
                    if (alpha != null) c.a = alpha[i];
                    px[y * w + x] = c;
                }
                p += stride;
            }
    done:
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(); tex.name = name;
        return tex;
    }

    static Color32[] Pal(int c0, int c1, bool dxt1Alpha)
    {
        var pal = new Color32[4];
        pal[0] = Rgb565(c0); pal[1] = Rgb565(c1);
        if (c0 > c1 || !dxt1Alpha)
        { pal[2] = Lerp(pal[0], pal[1], 1f / 3f); pal[3] = Lerp(pal[0], pal[1], 2f / 3f); }
        else { pal[2] = Lerp(pal[0], pal[1], 0.5f); pal[3] = new Color32(0, 0, 0, 0); }
        return pal;
    }

    static byte[] AlphaLut(int a0, int a1)
    {
        var lut = new byte[8];
        lut[0] = (byte)a0; lut[1] = (byte)a1;
        if (a0 > a1) for (int i = 0; i < 6; i++) lut[2 + i] = (byte)((a0 * (6 - i) + a1 * (i + 1)) / 7);
        else { for (int i = 0; i < 4; i++) lut[2 + i] = (byte)((a0 * (4 - i) + a1 * (i + 1)) / 5); lut[6] = 0; lut[7] = 255; }
        return lut;
    }

    static Color32 Rgb565(int c)
    {
        int r = (c >> 11) & 0x1F, g = (c >> 5) & 0x3F, b = c & 0x1F;
        return new Color32((byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255);
    }

    static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t),
                           (byte)(a.b + (b.b - a.b) * t), 255);
    }

    static Texture2D DecodeRaw(byte[] raw, int off, int w, int h, bool alpha, string name)
    {
        int bpp = alpha ? 4 : 3;
        if (off + (long)w * h * bpp > raw.Length) return null;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int q = off + (y * w + x) * bpp;
                if (q + bpp > raw.Length) break;
                px[(h - 1 - y) * w + x] = new Color32(raw[q + 2], raw[q + 1], raw[q], alpha ? raw[q + 3] : (byte)255);
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(); tex.name = name;
        return tex;
    }
}
