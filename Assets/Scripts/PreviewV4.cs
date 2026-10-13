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
using System.Runtime.InteropServices;
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
    const float FitSize = 3.4f;    // 大件归一化最大边(甲袍/外装)
    static float _zoom = 1f;       // 手动缩放(底部＋－)
    static string _texSample = null;
    // 骨骼动画播放状态
    static ActSkel _animSk; static SkiData _animSd; static Mesh _animMesh; static List<FramePose> _animBind;
    static Coroutine _animCo; static int _skipGuard;
    const float AnimFps = 24f;
    static System.Collections.IEnumerator PlayAnimCo()
    {
        var wait = new WaitForSeconds(1f / AnimFps);
        int nf = 0; foreach (var b in _animSk.bones) { if (b.nf > nf) nf = b.nf; }
        if (nf <= 1) yield break;   // 静态部件无动画
        while (_animSk != null && _animMesh != null && _animSd != null)
        {
            int f = (int)(Time.time * AnimFps) % nf;
            var W2 = LoadSkeleton(_animSk, f, _animBind);   // 绑定相对调色板 (站姿=恒等)
            var vs = new Vector3[_animMesh.vertexCount];
            for (int i = 0; i < vs.Length; i++) vs[i] = BakeVertex(W2, _animSd.verts[i]);
            _animMesh.vertices = vs;
            _animMesh.RecalculateBounds();
            yield return wait;
        }
    }
    // 按部件类型的合理预览尺寸: 小件不再被撑成大件
    static float FitFor(string name)
    {
        string n = (name ?? "").ToLower();
        if (n.Contains("sho") || n.Contains("glo")) return 1.0f;  // 长靴/手套
        if (n.Contains("hea")) return 0.9f;                       // 头部
        if (n.Contains("swd") || n.Contains("axe") || n.Contains("spe") || n.Contains("bow")) return 1.7f; // 武器
        return 1.8f;                                              // 甲袍/外装等大件
    }

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
        catch (Exception e) { line3 = "[v4.7] 启动失败: " + e.Message; }
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
        pivot.transform.position = new Vector3(0.9f, 0f, 0f);
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
    // 显式 JNI 数组拷出: 复用已验证的 open 链, read 数据经拷贝取回
    static byte[] ReadAsset(string rel)
    {
        AndroidJavaObject is2 = null;
        try
        {
            is2 = AM().Call<AndroidJavaObject>("open", rel, 2);
            IntPtr im = is2.GetRawObject();
            IntPtr ic = AndroidJNI.GetObjectClass(im);
            IntPtr mRead = AndroidJNI.GetMethodID(ic, "read", "([B)I");
            IntPtr mAv = AndroidJNI.GetMethodID(ic, "available", "()I");
            int total = AndroidJNI.CallIntMethod(im, mAv, new jvalue[0]);
            if (total <= 0) total = 262144;
            byte[] buf = new byte[total];
            int off = 0;
            IntPtr jb = AndroidJNI.NewByteArray(65536);
            var ja = new jvalue[1];
            while (off < buf.Length)
            {
                ja[0].l = jb;
                int n = AndroidJNI.CallIntMethod(im, mRead, ja);
                if (n <= 0) break;
                if (off + n > buf.Length) { var g = new byte[off + n]; Buffer.BlockCopy(buf, 0, g, 0, off); buf = g; }
                byte[] chunk = AndroidJNI.FromByteArray(jb);
                Buffer.BlockCopy(chunk, 0, buf, off, n);
                off += n;
            }
            AndroidJNI.DeleteLocalRef(jb);
            AndroidJNI.DeleteLocalRef(ic);
            if (off == 0) return new byte[0];
            if (off == buf.Length) return buf;
            var r = new byte[off];
            Buffer.BlockCopy(buf, 0, r, 0, off);
            return r;
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
            title = string.Format("v4.7 整装拼合 · APK数据注入 ({0} 骨架, StreamingAssets)", pool.Count);
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
            title = string.Format("v4.7 整装拼合 · act骨架池+LBS烘焙 ({0} 骨架, 外部目录)", actPool.Count);
            return;
        }
        throw new Exception("未找到资源 (APK内StreamingAssets与外部目录均无 role_*.act)");
    }

    // ===== 池 + 分类 =====
    static string poolStat = "(未统计)";
    static void AddCnt(Dictionary<string, int> d, string k) { int c; d.TryGetValue(k, out c); d[k] = c + 1; }
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

        // 池内家族统计 (跨职业, 定位注入缺口)
        var fam = new Dictionary<string, int>();
        int nw = 0, nm = 0;
        foreach (var f in all)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n.StartsWith("a_")) { var seg = n.Split('_'); AddCnt(fam, seg.Length >= 3 ? seg[0] + "_" + seg[1] + "_" + seg[2] : "a_?"); }
            else if (n.StartsWith("w_")) nw++;
            else nm++;
        }
        {
            var sb = new System.Text.StringBuilder();
            foreach (var j in Jobs) foreach (var sx in Sexes)
            {
                int c; fam.TryGetValue("a_" + j + "_" + sx, out c);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("a_").Append(j).Append('_').Append(sx).Append('×').Append(c);
            }
            sb.Append(" 武器×").Append(nw).Append(" 怪×").Append(nm);
            poolStat = sb.ToString();
        }

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
        if (d == null) throw new Exception("act读取null " + srcName);
        if (d.Length < 16 || d[0] != 0xff || d[1] != 0xff)
        {
            var sb = new System.Text.StringBuilder("act诊断 len=").Append(d.Length).Append(" head=");
            int hn = Math.Min(16, d.Length);
            for (int i = 0; i < hn; i++) sb.Append(d[i].ToString("x2")).Append(' ');
            throw new Exception(sb.ToString());
        }
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

    // v4.7: 层级复合世界矩阵 + 绑定相对调色板 (station帧=绑定姿态 → 恒等)
    static FramePose MatMul(FramePose A, FramePose B)   // A∘B
    {
        var M = new FramePose();
        float[] a = A.R, b = B.R;
        M.R[0] = a[0]*b[0] + a[1]*b[3] + a[2]*b[6];
        M.R[1] = a[0]*b[1] + a[1]*b[4] + a[2]*b[7];
        M.R[2] = a[0]*b[2] + a[1]*b[5] + a[2]*b[8];
        M.R[3] = a[3]*b[0] + a[4]*b[3] + a[5]*b[6];
        M.R[4] = a[3]*b[1] + a[4]*b[4] + a[5]*b[7];
        M.R[5] = a[3]*b[2] + a[4]*b[5] + a[5]*b[8];
        M.R[6] = a[6]*b[0] + a[7]*b[3] + a[8]*b[6];
        M.R[7] = a[6]*b[1] + a[7]*b[4] + a[8]*b[7];
        M.R[8] = a[6]*b[2] + a[7]*b[5] + a[8]*b[8];
        M.tx = a[0]*B.tx + a[1]*B.ty + a[2]*B.tz + A.tx;
        M.ty = a[3]*B.tx + a[4]*B.ty + a[5]*B.tz + A.ty;
        M.tz = a[6]*B.tx + a[7]*B.ty + a[8]*B.tz + A.tz;
        return M;
    }
    static FramePose MatInv(FramePose A)
    {
        var M = new FramePose();
        float[] a = A.R;
        M.R[0]=a[0]; M.R[1]=a[3]; M.R[2]=a[6];
        M.R[3]=a[1]; M.R[4]=a[4]; M.R[5]=a[7];
        M.R[6]=a[2]; M.R[7]=a[5]; M.R[8]=a[8];
        M.tx = -(a[0]*A.tx + a[3]*A.ty + a[6]*A.tz);
        M.ty = -(a[1]*A.tx + a[4]*A.ty + a[7]*A.tz);
        M.tz = -(a[2]*A.tx + a[5]*A.ty + a[8]*A.tz);
        return M;
    }
    static FramePose BoneLocal(Bone b, int frame)
    {
        var fp = new FramePose();
        if (b.nf > 0)
        {
            int f = frame >= b.nf ? b.nf - 1 : frame; if (f < 0) f = 0;   // clamp 保护
            QuatToMat(new[] { b.quats[f * 4], b.quats[f * 4 + 1], b.quats[f * 4 + 2], b.quats[f * 4 + 3] }, fp.R);
            fp.tx = b.trans[f * 3]; fp.ty = b.trans[f * 3 + 1]; fp.tz = b.trans[f * 3 + 2];
        }
        else { fp.R[0] = fp.R[4] = fp.R[8] = 1f; }               // 静态骨恒等
        return fp;
    }
    // bind = 站姿世界矩阵; 返回 world(frame)×bind⁻¹; bind=null → 直接返回 world
    static List<FramePose> LoadSkeleton(ActSkel sk, int frame, List<FramePose> bind)
    {
        int nb = sk.bones.Count;
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < nb; i++) idx[sk.bones[i].name] = i;
        var world = new FramePose[nb];
        for (int i = 0; i < nb; i++)
        {
            var L = BoneLocal(sk.bones[i], frame);
            int pi;
            if (sk.bones[i].parent != null && idx.TryGetValue(sk.bones[i].parent, out pi)
                && pi != i && pi < i && world[pi] != null)
                world[i] = MatMul(world[pi], L);
            else world[i] = L;
        }
        var W = new List<FramePose>(nb);
        for (int i = 0; i < nb; i++)
            W.Add((bind != null && bind[i] != null) ? MatMul(world[i], MatInv(bind[i])) : world[i]);
        return W;
    }
    static List<FramePose> LoadBind(ActSkel sk) { return LoadSkeleton(sk, FrameStand, null); }

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
        public List<string> anchors = new List<string>();
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

    // ================================================================
    //  ski 解析 v4.7 — A/R/C 已验证模型 (ski_export.py 297/297 文件闭环)
    //  顶点 = R 记录; C = 当前顶点附加绑定 (R2+C 权重和=1); A = 法线+UV (A_k 配 R_k);
    //  面表 = BE-u16 顶点序号 三角形列表 (引擎 rtc_skin_subset: GetFaceNum=size/3);
    //  绑定坐标 = 模型空间 (点云已验证) → 静止渲染 = Σ w×pos
    // ================================================================
    class SkiRec { public char t; public int n, vc; public byte bone; public float w;
                   public Vector3 pos, nor; public Vector2 uv; }

    static List<SkiRec> WalkVerts(byte[] d, int start)
    {
        int F = d.Length, o = start; bool firstA = true;
        var recs = new List<SkiRec>();
        while (o < F - 3)
        {
            if (o + 4 > F) return null;
            byte t0 = d[o], t1 = d[o + 1];
            if (t0 == 1 && t1 == 1)
            {
                if (firstA)
                {
                    if (o + 26 > F) return null;
                    var r = new SkiRec(); r.t = 'A'; r.vc = U32(d, o + 2);
                    r.nor = new Vector3(F32(d, o + 6), F32(d, o + 10), F32(d, o + 14));
                    r.uv = new Vector2(F32(d, o + 18), F32(d, o + 22));
                    recs.Add(r); o += 26; firstA = false;
                }
                else
                {
                    if (o + 22 > F) return null;
                    var r = new SkiRec(); r.t = 'A';
                    r.nor = new Vector3(F32(d, o + 2), F32(d, o + 6), F32(d, o + 10));
                    r.uv = new Vector2(F32(d, o + 14), F32(d, o + 18));
                    recs.Add(r); o += 22;
                }
            }
            else if (t0 == 1 && t1 != 0)
            {
                if (o + 30 > F) return null;
                var r = new SkiRec(); r.t = 'C'; r.bone = t1; r.w = F32(d, o + 2);
                r.pos = new Vector3(F32(d, o + 6), F32(d, o + 10), F32(d, o + 14));
                r.nor = new Vector3(F32(d, o + 18), F32(d, o + 22), F32(d, o + 26));
                recs.Add(r); o += 30;
            }
            else if (t0 >= 1 && t0 <= 8 && t1 == 0 && d[o + 2] == 0 && d[o + 3] == 0)
            {
                if (o + 33 > F) return null;
                var r = new SkiRec(); r.t = 'R'; r.n = t0; r.bone = d[o + 4]; r.w = F32(d, o + 5);
                r.pos = new Vector3(F32(d, o + 9), F32(d, o + 13), F32(d, o + 17));
                r.nor = new Vector3(F32(d, o + 21), F32(d, o + 25), F32(d, o + 29));
                recs.Add(r); o += 33;
            }
            else return null;   // 未识别 → 起点错误
        }
        return recs;
    }

    static int FindVertStart(byte[] d, int mtl)
    {
        int F = d.Length, best = -1, bestn = 0;
        int lo = mtl > 0 ? mtl : 0;
        int hi = Math.Min(lo + 0x400, F - 40);
        for (int p = lo; p < hi; p++)
        {
            if (d[p] != 1 || d[p + 1] != 1) continue;
            var recs = WalkVerts(d, p);
            if (recs == null) continue;
            if (recs.Count > bestn) { best = p; bestn = recs.Count; }
            if (recs.Count > 10 && d[F - 3] == 1 && d[F - 2] == 1 && d[F - 1] == 1)
                return p;   // walk 恰好贯穿到 EOF-3 尾标 → 唯一真起点
        }
        return best;
    }

    // 所有 CRT_Mtl* 块后的候选锚名 (wai_01 第一块引用头部贴图、第二块才是本体 → 全收集按序尝试)
    static List<string> ExtractAnchors(byte[] d)
    {
        var res = new List<string>();
        int from = 0;
        while (true)
        {
            int m = IndexOf(d, "CRT_Mtl", from);
            if (m < 0) break;
            int s = m + 7, hi = Math.Min(s + 220, d.Length);
            int i = s;
            while (i < hi)
            {
                int j = i;
                while (j < hi && IsW(d[j])) j++;
                if (j - i >= 4 && j - i <= 32)
                {
                    string cand = Encoding.ASCII.GetString(d, i, j - i);
                    if (!cand.StartsWith("CRT") && !cand.StartsWith("0_") && cand != "Standard" && cand != "Mu" && !res.Contains(cand)) res.Add(cand);
                }
                i = (j > i) ? j + 1 : i + 1;
            }
            from = m + 7;
        }
        return res;
    }

    static void FlushVtx(List<Vtx> vtx, List<int> b, List<float> w, List<Vector3> p)
    {
        float s = 0; foreach (var x in w) s += x;
        var v = new Vtx();
        v.bid = b.ToArray(); v.w = w.ToArray(); v.pos = p.ToArray();
        if (s > 1e-6f) for (int i = 0; i < v.w.Length; i++) v.w[i] /= s;
        vtx.Add(v);
    }

    static SkiData ParseSki(byte[] d)
    {
        int F = d.Length;
        if (F < 64 || d[0] != 0xFF || d[1] != 0xFF) throw new Exception("非CRT对象流");
        var sd = new SkiData();

        // ---- A. CRT_SkelSkin: 骨架引用 + 子网格面表(大端) ----
        int o = 4 + "CRT_SkelSkin".Length;
        int nl = U32(d, o); o += 4;
        if (nl < 1 || nl > 64 || o + nl + 6 > F) throw new Exception("SkelSkin头坏");
        string skelRef = Str(d, o, nl); o += nl + 2;
        int nsub = U32(d, o); o += 4;
        if (nsub < 1 || nsub > 16) throw new Exception("子网格数异常 " + nsub);
        for (int s = 0; s < nsub; s++)
        {
            int l1 = U32(d, o); o += 4; if (l1 < 1 || l1 > 64 || o + l1 + 8 > F) throw new Exception("子网格名1坏");
            o += l1;
            int l2 = U32(d, o); o += 4; if (l2 < 1 || l2 > 64 || o + l2 + 8 > F) throw new Exception("子网格名2坏");
            o += l2;
            int cnt = U32(d, o); o += 4;
            if (cnt < 3 || cnt > 60000 || o + 1 + cnt * 2 > F) throw new Exception("面表数异常 " + cnt);
            o += 1;                                          // flag
            for (int i = 0; i + 2 < cnt; i += 3)
            {
                int a = (d[o + 2 * i] << 8) | d[o + 2 * i + 1];           // ★大端★
                int b2 = (d[o + 2 * i + 2] << 8) | d[o + 2 * i + 3];
                int c = (d[o + 2 * i + 4] << 8) | d[o + 2 * i + 5];
                if (a == b2 || b2 == c || a == c) continue;               // 退化剔除
                sd.tris.Add(a); sd.tris.Add(b2); sd.tris.Add(c);
            }
            o += cnt * 2;
        }

        // ---- B. 材质锚名候选 ----
        sd.anchors = ExtractAnchors(d);
        sd.anchor = sd.anchors.Count > 0 ? sd.anchors[0] : "";

        // ---- C. 顶点流 ----
        int mtl0 = IndexOf(d, "CRT_Mtl", 0);
        int start = FindVertStart(d, mtl0);
        if (start < 0) throw new Exception("顶点流未定位");
        var recs = WalkVerts(d, start);
        if (recs == null || recs.Count < 3) throw new Exception("顶点流walk失败");

        var aList = new List<SkiRec>();
        int nR = 0, cCount = 0, declared = -1;
        foreach (var r in recs)
        {
            if (r.t == 'A') { aList.Add(r); if (declared < 0 && r.vc > 0) declared = r.vc; }
            else if (r.t == 'R') nR++;
            else if (r.t == 'C') cCount++;
        }
        if (nR == 0) throw new Exception("0 顶点记录");

        // 装配: R 开新顶点(首绑定), C 并入当前顶点, 权重归一
        var vtx = new List<Vtx>(nR);
        var curB = new List<int>(); var curW = new List<float>(); var curP = new List<Vector3>();
        bool open = false;
        foreach (var r in recs)
        {
            if (r.t == 'R')
            {
                if (open) FlushVtx(vtx, curB, curW, curP);
                curB.Clear(); curW.Clear(); curP.Clear();
                curB.Add(r.bone); curW.Add(r.w); curP.Add(r.pos);
                open = true;
            }
            else if (r.t == 'C' && open)
            {
                curB.Add(r.bone); curW.Add(r.w); curP.Add(r.pos);
            }
        }
        if (open) FlushVtx(vtx, curB, curW, curP);
        sd.verts = vtx;

        // UV: A_k 配顶点 k
        sd.uv.Clear();
        for (int k = 0; k < vtx.Count; k++)
            sd.uv.Add(k < aList.Count ? aList[k].uv : Vector2.zero);

        // 面表越界过滤
        int vcp = vtx.Count, kept = 0;
        var tris2 = new List<int>(sd.tris.Count);
        for (int i = 0; i + 2 < sd.tris.Count; i += 3)
            if (sd.tris[i] < vcp && sd.tris[i + 1] < vcp && sd.tris[i + 2] < vcp)
            { tris2.Add(sd.tris[i]); tris2.Add(sd.tris[i + 1]); tris2.Add(sd.tris[i + 2]); kept += 3; }
        int dropped = sd.tris.Count - kept;
        sd.tris = tris2;
        sd.rawN = kept;

        sd.fmt = string.Format("v4.7(list) R{0}/C{1}/A{2}{3}{4}", nR, cCount, aList.Count,
            (declared >= 0 && declared != nR) ? " 声明" + declared + "!" : "",
            dropped > 0 ? " 丢" + (dropped / 3) + "角" : "") + " skel=" + skelRef;
        if (sd.verts.Count == 0) throw new Exception("0 顶点");
        if (sd.tris.Count == 0) throw new Exception("0 三角");
        return sd;
    }

    static bool IsW(byte c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'; }

    // ---- v4.7 贴图解析: 多锚名 × {裸名, a_前缀} × {全名, 剥尾字符} × {dds,png}, 同族优先 ----
    static bool IsDigits(string s) { if (s.Length == 0) return false; foreach (var ch in s) if (ch < '0' || ch > '9') return false; return true; }

    static string ResolveTex(string skiName, List<string> anchors)
    {
        if (anchors == null || anchors.Count == 0) return null;
        string ski = (skiName ?? "").ToLower();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var a0 in anchors)
            {
                string a = a0.ToLower();
                bool fam = ski.Contains(a) || a.Contains(ski);
                if (pass == 0 && !fam) continue;
                if (pass == 1 && fam) continue;
                for (int v = 0; v < 2; v++)
                {
                    string b = (v == 0 ? "" : "a_") + a;
                    for (int k = 0; k < 3; k++)
                    {
                        string c = k == 0 ? b : (k == 1 && b.Length > 1 ? b.Substring(0, b.Length - 1) : b.Replace("_", ""));
                        if (k == 2 && b.IndexOf('_') < 0) continue;
                        foreach (var e in new[] { ".dds", ".png" })
                            if (TexExists(c + e)) return c + e;
                    }
                }
            }
        }
        // 兜底: 由 ski 文件名派生 (a_wt_m_wai_01 → wt_m_wai_01 / a_wt_m_wai_01 / wt_m_wai ...)
        string b2 = ski.StartsWith("a_") ? ski.Substring(2) : ski;
        while (b2.Length > 3)
        {
            foreach (var pre in new[] { "", "a_" })
            {
                string b = pre + b2;
                foreach (var c in new[] { b, b.Replace("_", "") })
                    foreach (var e in new[] { ".dds", ".png" })
                        if (TexExists(c + e)) return c + e;
            }
            int k = b2.LastIndexOf('_');
            if (k < 0) break;
            if (!IsDigits(b2.Substring(k + 1))) break;
            b2 = b2.Substring(0, k);
        }
        return null;
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
        if (curSkiList.Count == 0) { line2 = "(本分类当前职业无模型) 池内: " + poolStat; return; }
        if (curIdx >= curSkiList.Count) curIdx = 0;
        curSki = curSkiList[curIdx];
        StartShow(curSki);
    }

    static void StartShow(string name)
    {
        line2 = name + "  <" + Cats[catI] + ">  [" + CurJob + "_" + CurSex + "]";
        _animSk = null; _animMesh = null; _animSd = null;
        if (hostPv != null && _animCo != null) { hostPv.StopCoroutine(_animCo); _animCo = null; }
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
            List<FramePose> bind = (sk != null) ? LoadBind(sk) : null;
            // 站姿调色板 = world(stand)×world(stand)⁻¹ = 恒等 → 静止渲染 = Σ w×pos (模型空间原样呈现)

            // 烘焙 → mesh
            int vc = sd.verts.Count;
            var vs = new Vector3[vc];
            for (int i = 0; i < vc; i++) vs[i] = BakeVertex(bind, sd.verts[i]);
            var mesh = new Mesh();
            mesh.vertices = vs;
            mesh.uv = sd.uv.GetRange(0, Math.Min(sd.uv.Count, vc)).ToArray();
            mesh.triangles = sd.tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 包围盒归一化
            var b = mesh.bounds;
            float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            float s = ext > 1e-5f ? FitFor(name) * _zoom / ext : _zoom;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localScale = new Vector3(s, s, s);
            var bc = mesh.bounds.center;   // 中心补偿: 部件中心对准显示点(z-up->y-up)
            curModel.transform.localPosition = new Vector3(0f, 1.55f, 0f) - s * new Vector3(bc.x, bc.z, -bc.y);
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

            line3 = string.Format("[v4.7] {0} {1} 顶点{2} 面{3} ski:{4}B | 骨架={5} | 贴图=...",
                sd.fmt, tag == "STATIC" ? "静态" : tag + ":" + (sk != null ? sk.srcName : ""),
                vc, sd.tris.Count / 3, d.Length,
                sk != null ? sk.srcName : "无");

            // 骨骼动画播放 (act自带逐帧trans/quat, 层级复合+绑定相对)
            int anf = sk != null && sk.bones.Count > 0 ? sk.bones[0].nf : 0;
            if (sk != null && anf > 1)
            {
                _animSk = sk; _animSd = sd; _animMesh = mesh; _animBind = bind;
                _animCo = hostPv.StartCoroutine(PlayAnimCo());
                line3 += string.Format(" \u25b6动画{0}帧", anf);
            }
            _skipGuard = 0;

            // 贴图异步加载 (多锚名依次解析, 加载完成自动贴上)
            if (sd.anchors != null && sd.anchors.Count > 0 && hostPv != null)
                hostPv.StartCoroutine(LoadTexCo(name, sd.anchors, mat));
        }
        catch (Exception e)
        {
            line3 = "[v4.7] " + name + " 解析失败: " + e.Message;
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
            // 不完整部件自动跳过 (整圈全失败才停)
            if (hostPv != null && curSkiList.Count > 1 && _skipGuard < curSkiList.Count)
            {
                _skipGuard++;
                curIdx = (curIdx + 1) % curSkiList.Count;
                StartShow(curSkiList[curIdx]);
            }
        }
    }

    static readonly Dictionary<string, string> texResCache = new Dictionary<string, string>();

    static string ResolveTexCached(string skiName, List<string> anchors)
    {
        string key = skiName + "|" + (anchors != null && anchors.Count > 0 ? anchors[0] : "");
        string p;
        if (texResCache.TryGetValue(key, out p)) return p;
        p = ResolveTex(skiName, anchors);
        texResCache[key] = p;
        return p;
    }

    static System.Collections.IEnumerator LoadTexCo(string skiName, List<string> anchors, Material mat)
    {
        string p = ResolveTexCached(skiName, anchors);
        if (p == null)
        {
            if (_texSample == null)
            {
                var ls = ListAssets("creature/texture");
                var tsb = new System.Text.StringBuilder();
                for (int i = 0; i < ls.Length && i < 6; i++) { if (i > 0) tsb.Append(','); tsb.Append(ls[i]); }
                _texSample = ls.Length == 0 ? "(目录空)" : tsb.ToString();
            }
            string baseN = skiName.ToLower(); if (baseN.StartsWith("a_")) baseN = baseN.Substring(2);
            bool famTex = TexExists(baseN + ".dds") || TexExists("a_" + baseN + ".dds");
            line3 += " ✗未找到[" + (famTex ? "池有同名贴图但锚名未命中" : "池无此贴图→需注入") + "] 样本[" + _texSample + "]";
            yield break;
        }
        if (texCache.TryGetValue(p, out var tc) && tc != null)
        {
            if (mat != null) { mat.mainTexture = tc; mat.color = Color.white; }
            line3 += " ✓(缓存)";
            yield break;
        }
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
            try { raw = File.ReadAllBytes(Path.Combine(texDir, p)); } catch (Exception) { raw = null; }
        }
        Texture2D t = null;
        if (raw != null)
        {
            if (p.EndsWith(".dds")) t = DdsV4.LoadFromMemory(raw, Path.GetFileNameWithoutExtension(p));
            else { t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (!t.LoadImage(raw)) t = null; }
        }
        if (t != null)
        {
            texCache[p] = t;
            if (mat != null) { mat.mainTexture = t; mat.color = Color.white; }
            line3 += " ✓" + p;
        }
        else line3 += " ✗解码失败:" + p;
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

        // 分类: 一竖排 (左侧)
        float cw = 300f, ch = 40f;
        GUI.Box(new Rect(0, 88, cw + 16, Cats.Length * (ch + 3) + 10), GUIContent.none, box);
        for (int i = 0; i < Cats.Length; i++)
        {
            var old = GUI.backgroundColor;
            if (i == catI) GUI.backgroundColor = new Color(0.30f, 0.65f, 0.95f);
            if (GUI.Button(new Rect(8, 92 + i * (ch + 3), cw, ch), (i == catI ? "\u25b6 " : "") + Cats[i], i == catI ? btn : small))
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
        if (GUI.Button(new Rect(922, yb, 64, 54), "\uff0b", btn)) { _zoom = Mathf.Clamp(_zoom * 1.2f, 0.2f, 8f); ShowCurrent(); }
        if (GUI.Button(new Rect(992, yb, 64, 54), "\uff0d", btn)) { _zoom = Mathf.Clamp(_zoom / 1.2f, 0.2f, 8f); ShowCurrent(); }

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