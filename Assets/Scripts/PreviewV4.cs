// PreviewV4.cs — v4.8.1d 整装拼合预览台 (2026-10-13 晚热修)
// ============================================================
// v4.8.1d 热修（装机报错定位后当天修复）:
//  ① "骨架表异常 nb=1050/2622" → ski 头不再解码骨架表 (role ski 头布局与 bpf10 样本不符,
//     dissect_ski 式解码把面数当骨数); 顶点流改为自 offset 16 全长扫描, 最长自校验链者胜
//  ② 面表段收集改为"≥最长段50%"多段收集, 排除骨架父索引表等短段污染
//  ③ ACT 点云: 节点表锚点多候选重试(≤10个, root/Bip01/父NULL优先);
//     动画变体(_01/_02)无网格时报"无网格节点(纯动画/特效)"属正常, 不是错误
// v4.8.1d 修复清单（对应 MEMO 待修清单，全部落地）:
//  ① P0 网格炸裂/碎片: ski 解析整个重写 = ski_obj.py(297-OBJ 验证版)逐行移植:
//     顶点 = [u32 nbk][nbk×30B 绑定块(u8骨id, f32权重, f32×3 pos, f32×3法线, 1B垫)][u8 05][f32×3 tangent][f32×2 uv]
//     面表 = 小端 u16 序列 (v4.7 误用大端 + 误把骨架父索引表当面表 + 自创 R/C/A 记录机 → 碎片/全灭)
//     顶点流定位 = 自校验链式扫描(ski_final.py valid_rec 策略): 长链者胜
//  ② P0 白模: 贴图解析改为 ski 文件名同名优先(池内 1:1) → 数字尾剥离 → 锚名清洗 → 家族兜底
//  ③ P0 骨架错配: 只认 role_{职业}_{性别}_*.act; 无匹配 → 静态渲染(首绑定块原始坐标), 删除"抓第一个骨架"的错配兜底
//  ④ P0 武器/怪物/坐骑: CRT_Actor(.act) 内嵌网格 → 点云渲染 (act_dissect v0.12 布局移植, 盲同步节点表)
//  ⑤ P1 解析失败不再整类静默跳过: 报错停在当前件, 手动翻页
//  ⑥ P1 UI 紧凑化: 顶栏 86→58, 标题 22→15, 左列 300×40→186×26, 分类按槽位排序, 职业/底栏同步缩小
// 骨架动画 (act_parse.py 等价) 与 LBS 烘焙 (mass_produce.bake_vertex 等价) 保持不变。
// DDS 解码 DdsV4 保持不变。
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
    // ===== 真机校准常量 =====
    static readonly Vector3 ModelRot = new Vector3(0f, 0f, 0f); // v4.8.2a: 源数据=Y-up(真机横躺+发辫下垂+双靴直立三重实证), 撤销双重旋转
    const int FrameStand = 477; // v4.8.2a: frame_scan 全帧联合自洽最优站立帧(score 565)
    const float FitSize = 3.4f;
    static float _zoom = 1f;
    static string _texSample = null;
    // 骨骼动画播放状态
    static ActSkel _animSk; static SkiData _animSd; static Mesh _animMesh; static List<FramePose> _animBind;
    static Coroutine _animCo;
    const float AnimFps = 24f;
    static System.Collections.IEnumerator PlayAnimCo()
    {
        var wait = new WaitForSeconds(1f / AnimFps);
        int nf = 0; foreach (var b in _animSk.bones) { if (b.nf > nf) nf = b.nf; }
        if (nf <= 1) yield break;
        while (_animSk != null && _animMesh != null && _animSd != null)
        {
            int f = (int)(Time.time * AnimFps) % nf;
            var W2 = LoadSkeleton(_animSk, f, _animBind);
            var vs = new Vector3[_animMesh.vertexCount];
            for (int i = 0; i < vs.Length; i++) vs[i] = BakeVertex(W2, _animSd.verts[i]);
            _animMesh.vertices = vs;
            _animMesh.RecalculateBounds();
            yield return wait;
        }
    }
    // 按部件类型的合理预览尺寸
    static float FitFor(string name)
    {
        string n = (name ?? "").ToLower();
        if (n.Contains("sho") || n.Contains("glo")) return 1.0f;
        if (n.Contains("hea")) return 0.9f;
        if (n.Contains("swd") || n.Contains("axe") || n.Contains("spe") || n.Contains("bow")) return 1.7f;
        if (RegexWeaponAct.IsMatch(n)) return 1.7f;               // 武器 act
        if (n.Contains("chibang") || n.Contains("iwgong")) return 2.4f; // 翅膀
        return 1.8f;
    }

    // ===== 运行状态 =====
    static string actorDir, texDir;
    static readonly string[] Jobs = { "wt", "hj", "mj", "ss" };
    static readonly string[] Sexes = { "m", "f" };
    static int jobI = 0, sexI = 0;
    static string CurJob { get { return Jobs[jobI]; } }
    static string CurSex { get { return Sexes[sexI]; } }
    static string curSki = "";
    static int catI = 2;           // 默认甲胄
    static float rotY = 0f;
    static GameObject pivot, curModel;
    static string title = "", line2 = "", line3 = "";
    static readonly Dictionary<string, ActSkel> skelCache = new Dictionary<string, ActSkel>();
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    static List<string> actPool;   // role_*.act 骨架池

    static readonly string[] Cats = {
        "武器·刀剑(槽1)", "武器·斧钺(槽2)", "甲胄·防具(3)", "头部(脸)",
        "手套·防具(1)", "长靴·防具(2)", "外装", "其他部件", "怪物/坐骑" };
    // 左列显示顺序(按槽位): 甲胄→外装→头部→手套→长靴→刀剑→斧钺→其他→怪物
    static readonly int[] CatOrder = { 2, 6, 3, 4, 5, 0, 1, 7, 8 };
    static readonly Regex RegexWeaponAct = new Regex(
        @"^\d+(dao|zhan|jian|feng|shu|gong|qiang|axe|sword|spear|bow|stick|gun)",
        RegexOptions.Compiled);
    static List<string> catFiles = new List<string>();   // 含扩展名的文件名

    // ================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        try { SetupCam(); KillOthers(); Scan(); RebuildCat(); ShowCurrent(); }
        catch (Exception e) { line3 = "[v4.8.2a] 启动失败: " + e.Message; }
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

    // ===== 资源路径探测: APK内StreamingAssets优先, 外部目录兜底 =====
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
        // 1) APK 内 StreamingAssets
        var pool = new List<string>();
        int nSki = 0;
        foreach (var n in ListAssets("creature/actor"))
        {
            if (n.StartsWith("role_") && n.EndsWith(".act")) pool.Add(n);
            else if (n.EndsWith(".ski")) nSki++;
        }
        if (pool.Count > 0 || nSki > 0)
        {
            pool.Sort();
            actPool = pool; useAsset = true;
            actorDir = "asset:creature/actor"; texDir = "asset:creature/texture";
            title = string.Format("v4.8.2a 整装拼合 · APK注入 ({0} 骨架/{1} ski, StreamingAssets)", pool.Count, nSki);
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
            var skis = Directory.GetFiles(c, "*.ski");
            if (acts.Length == 0 && skis.Length == 0) continue;
            actorDir = c; useAsset = false;
            actPool = new List<string>();
            foreach (var a in acts) actPool.Add(a);
            actPool.Sort();
            string t = Path.GetDirectoryName(c) + "/texture";
            texDir = Directory.Exists(t) ? t : c;
            title = string.Format("v4.8.2a 整装拼合 · 外部目录 ({0} 骨架/{1} ski)", actPool.Count, skis.Length);
            return;
        }
        throw new Exception("未找到资源 (APK内StreamingAssets与外部目录均无数据)");
    }

    // ===== 池 + 分类 =====
    static string poolStat = "(未统计)";
    static void AddCnt(Dictionary<string, int> d, string k) { int c; d.TryGetValue(k, out c); d[k] = c + 1; }
    static void RebuildCat()
    {
        if (actPool == null) { curSkiList.Clear(); curIdx = 0; return; }
        catFiles.Clear();
        List<string> all = new List<string>();
        if (useAsset)
        {
            foreach (var f in ListAssets("creature/actor"))
                if (f.EndsWith(".ski") || f.EndsWith(".act")) all.Add(f);
        }
        else
        {
            try
            {
                foreach (var f in Directory.GetFiles(actorDir))
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext == ".ski" || ext == ".act") all.Add(Path.GetFileName(f));
                }
            }
            catch (Exception) { }
        }

        // 池内家族统计 (跨职业, 定位注入缺口)
        var fam = new Dictionary<string, int>();
        int nw = 0, nm = 0;
        foreach (var f in all)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n.StartsWith("role_")) continue;
            if (n.StartsWith("a_")) { var seg = n.Split('_'); AddCnt(fam, seg.Length >= 3 ? seg[0] + "_" + seg[1] + "_" + seg[2] : "a_?"); }
            else if (CatOf(n) == 0 || CatOf(n) == 1) nw++;
            else if (CatOf(n) == 8) nm++;
        }
        {
            var sb = new System.Text.StringBuilder();
            foreach (var j in Jobs) foreach (var sx in Sexes)
            {
                int c; fam.TryGetValue("a_" + j + "_" + sx, out c);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("a_").Append(j).Append('_').Append(sx).Append('×').Append(c);
            }
            sb.Append(" 武器×").Append(nw).Append(" 怪翅×").Append(nm);
            poolStat = sb.ToString();
        }

        foreach (var f in all)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n.StartsWith("role_")) continue;                       // 骨架不进浏览列表
            bool take = false;
            if (n.StartsWith("a_" + CurJob + "_" + CurSex + "_")) take = true;   // 当前职业部件
            else if (n.StartsWith("w_") && n.Contains("_" + CurJob)) take = true; // 当前职业武器ski
            else if (CatOf(n) == 0 || CatOf(n) == 1) take = true;      // 武器(全职业通用)
            else if (CatOf(n) == 8) take = true;                       // 怪物/坐骑/翅膀
            if (take) catFiles.Add(f);
        }
        catFiles.Sort();
        FilterCat();
    }

    static int CatOf(string n)   // n 不带扩展名; -1 = 不收录
    {
        if (n.StartsWith("w_"))
        {
            int u = n.IndexOf('_', 2);
            string seg = u > 2 ? n.Substring(2, u - 2) : n.Substring(2);
            if (seg.EndsWith("c")) return 1;
            return 0;
        }
        if (RegexWeaponAct.IsMatch(n))
        {
            var m = RegexWeaponAct.Match(n);
            string fword = m.Groups[1].Value;
            if (fword == "feng" || fword == "shu" || fword == "axe" || fword == "stick") return 1;
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
        if (n.Contains("chibang") || n.Contains("iwgong") || n.StartsWith("mon")
            || n.StartsWith("boss") || n.Contains("ride") || n.Contains("zuoqi")
            || n.Contains("horse") || n.StartsWith("pet") || n.StartsWith("gua"))
            return 8;
        return -1;   // epf/eph/s_/skill 等特效动画不进列表
    }

    static void FilterCat()
    {
        List<string> keep = new List<string>();
        foreach (var f in catFiles)
            if (CatOf(Path.GetFileNameWithoutExtension(f)) == catI) keep.Add(f);
        curSkiList = keep;
        curIdx = 0;
    }
    static List<string> curSkiList = new List<string>();
    static int curIdx = 0;

    // ================================================================
    //                    act 骨架解析 (act_parse.py 等价, 不变)
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
        o += 2;
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
                o = FindNextAnchor(d, o);
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

    static void QuatToMat(float[] q, float[] R)
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

    static FramePose MatMul(FramePose A, FramePose B)
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
            int f = frame >= b.nf ? b.nf - 1 : frame; if (f < 0) f = 0;
            QuatToMat(new[] { b.quats[f * 4], b.quats[f * 4 + 1], b.quats[f * 4 + 2], b.quats[f * 4 + 3] }, fp.R);
            fp.tx = b.trans[f * 3]; fp.ty = b.trans[f * 3 + 1]; fp.tz = b.trans[f * 3 + 2];
        }
        else { fp.R[0] = fp.R[4] = fp.R[8] = 1f; }
        return fp;
    }
    static List<FramePose> LoadSkeleton(ActSkel sk, int frame, List<FramePose> bind)
    {
        // v4.8.2a 定案: act 每骨 quat/trans 即蒙皮空间变换, 绑定块 pos=骨局部坐标 → 纯直接用
        // (全件全帧形状比 0.7~1.27x 刚体稳定; 父链组合/混合式/bind逆均炸形, 本地五组实验定案)
        // bind 参数保留兼容旧调用点, 本配方下不再使用
        var W = new List<FramePose>(sk.bones.Count);
        for (int i = 0; i < sk.bones.Count; i++) W.Add(BoneLocal(sk.bones[i], frame));
        return W;
    }
    static List<FramePose> LoadBind(ActSkel sk) { return LoadSkeleton(sk, FrameStand, null); }

    // ===== 骨架匹配: 只认同族骨架, 无匹配 → 静态 (v4.8.1d: 删除错配兜底) =====
    static ActSkel MatchSkel(string skiName, out string tag)
    {
        tag = "STATIC·未配骨架";
        var m = Regex.Match(skiName, @"^a_([a-z]+)_([fm])_");
        if (!m.Success) return null;                    // 怪物/武器/宠物 → 静态
        string want = "role_" + m.Groups[1].Value + "_" + m.Groups[2].Value + "_";
        string path = null;
        foreach (var p in actPool)
        {
            string fn = Path.GetFileName(p);
            if (fn.StartsWith(want)) { path = p; break; }
        }
        if (path == null) return null;                  // 无同族骨架 → 静态渲染, 不再错配
        tag = "OK";
        ActSkel sk;
        if (!skelCache.TryGetValue(path, out sk))
        {
            sk = ParseAct(ReadActorFile(Path.GetFileName(path)), Path.GetFileName(path));
            skelCache[path] = sk;
        }
        return sk;
    }

    // ================================================================
    //            ski 解析 v4.8.1d — ski_obj.py 实测格式移植 (297-OBJ 验证)
    // ================================================================
    class Vtx { public byte[] bid; public float[] w; public Vector3[] pos; public Vector3 pos0; }

    class SkiData
    {
        public List<Vtx> verts = new List<Vtx>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> tris = new List<int>();
        public List<string> anchors = new List<string>();
        public string skelRef = "", fmt = "";
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
    static string ReadCStr(byte[] d, ref int o, int maxn)
    {
        int nl = U32(d, o);
        if (nl < 1 || nl > maxn || o + 4 + nl > d.Length) throw new Exception("字符串字段异常 len=" + nl);
        string s = Str(d, o + 4, nl);
        o += 4 + nl;
        return s;
    }
    static bool IsW(byte c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'; }
    static bool IsDigits(string s) { if (s.Length == 0) return false; foreach (var ch in s) if (ch < '0' || ch > '9') return false; return true; }

    // 顶点记录自校验链长 (ski_final.py valid_rec 策略)
    static int ChainVtx(byte[] d, int p, int F)
    {
        int o = p, n = 0;
        while (o < F - 8)
        {
            if (o + 4 > F) break;
            int nbk = U32(d, o);
            if (nbk < 1 || nbk > 4) break;
            int q = o + 4;
            float tw = 0;
            for (int i = 0; i < nbk; i++)
            {
                if (q + 30 > F) return n;
                if (d[q] > 127) return n;   // v4.8.1d: 64→127, 骨id≥65 提前断链 → vcp偏小 → 面表被切碎
                float w = F32(d, q + 1);
                if (w < -0.01f || w > 1.001f) return n;
                tw += w;
                q += 30;
            }
            if (nbk > 1 && (tw < 0.5f || tw > 1.6f)) return n;
            if (q + 21 <= F)
            {
                float u = F32(d, q + 13), v = F32(d, q + 17);
                if (u < -8f || u > 8f || v < -8f || v > 8f) return n;
                o = q + 21;
            }
            else if (q <= F) { o = q; }          // 尾顶点: 缺 tangent/uv
            else return n;
            n++;
            if (n > 60000) break;
        }
        return n;
    }

    static SkiData ParseSki(byte[] d)
    {
        int F = d.Length;
        if (F < 64 || d[0] != 0xFF || d[1] != 0xFF) throw new Exception("非CRT对象流");
        var sd = new SkiData();

        // ---- A. 头只验证 CRT_SkelSkin; 不再解码骨架表 ----
        // v4.8.1d 热修: role ski 头布局与 bpf10 样本不符, v4.8.1d 的 dissect_ski 式解码把
        // 面数当骨数 → "骨架表异常 nb=1050/2622"。骨架引用仅作展示(尽力读取)。
        if (F < 16 || Str(d, 4, 12) != "CRT_SkelSkin") throw new Exception("缺CRT_SkelSkin头");
        string skelRef = "";
        try { int t = 16; skelRef = ReadCStr(d, ref t, 64); } catch { }
        sd.skelRef = skelRef;

        // ---- B. 顶点流定位: 自16起全长扫描, 最长自校验链者胜 (ski_obj.py 实测格式) ----
        int best = -1, bestN = 0;
        for (int p = 16; p < F - 60; p++)
        {
            int n = ChainVtx(d, p, F);
            if (n > bestN) { bestN = n; best = p; }
            if (bestN > 4000) break;
        }
        if (bestN < 8) throw new Exception("顶点流未定位(最长链" + bestN + ")");
        int p2 = best;
        while (p2 < F - 8)
        {
            if (p2 + 4 > F) break;
            int nbk = U32(d, p2);
            if (nbk < 1 || nbk > 4) break;
            int q = p2 + 4;
            var v = new Vtx();
            v.bid = new byte[nbk]; v.w = new float[nbk]; v.pos = new Vector3[nbk];
            for (int i = 0; i < nbk; i++)
            {
                v.bid[i] = d[q];
                v.w[i] = F32(d, q + 1);
                v.pos[i] = new Vector3(F32(d, q + 5), F32(d, q + 9), F32(d, q + 13));
                q += 30;
            }
            v.pos0 = v.pos[0];
            if (q + 21 <= F)
            {
                sd.uv.Add(new Vector2(F32(d, q + 13), F32(d, q + 17)));
                p2 = q + 21;
            }
            else { sd.uv.Add(Vector2.zero); p2 = q; }
            // 权重归一
            float sw = 0; for (int i = 0; i < v.w.Length; i++) sw += v.w[i];
            if (sw > 1e-6f) for (int i = 0; i < v.w.Length; i++) v.w[i] /= sw;
            sd.verts.Add(v);
        }
        int vcp = sd.verts.Count;
        if (vcp < 3) throw new Exception("0 顶点");

        // ---- C. 面表: 小端 u16 (v4.7 误用大端 = 碎片化根因) ----
        // v4.8.1d (真机指纹定案): 面表在顶点流【之后】—— 流前最长u16段仅骨架/头部碎段,
        // 而计数器vcp与链长互证(294@0xa14)。双区间收集: 流前[16,best) + 流后[walkEnd,F)
        int walkEnd = p2;
        // v4.8.1f: 双对齐×双端序候选段 → 按长度降序贪心; 重叠过半跳过(幽灵段), 产量(有效面)为纲
        // (LE 表的 BE 奇对齐幽灵会复刻同值序列致双倍计数; 幽灵产量恒0 → 产出为零不占位)
        var cands = new List<int[]>();
        cands.AddRange(CollectRuns(d, 16, best, vcp, false));
        cands.AddRange(CollectRuns(d, 16, best, vcp, true));
        cands.AddRange(CollectRuns(d, walkEnd, F, vcp, false));
        cands.AddRange(CollectRuns(d, walkEnd, F, vcp, true));
        cands.Sort((x, y) => y[1].CompareTo(x[1]));
        int maxLen = cands.Count > 0 ? cands[0][1] : 0;
        var taken = new List<int[]>();   // 已产出段 [start, end)
        bool faceAfter = false, beUsed = false;
        foreach (var r in cands)
        {
            if (r[1] < maxLen / 2) continue;
            int s0 = r[0], e0 = r[0] + 2 * r[1];
            bool blocked = false;
            foreach (var t in taken)
            {
                int ov = Math.Min(e0, t[1]) - Math.Max(s0, t[0]);
                if (ov > 0 && ov * 2 > (e0 - s0)) { blocked = true; break; }
            }
            if (blocked) continue;
            int before = sd.tris.Count;
            FlushRun(sd, d, r[0], r[1], true, vcp, r[3] != 0);
            if (sd.tris.Count > before)
            {
                taken.Add(new[] { s0, e0 });
                if (r[3] != 0) beUsed = true;
                if (r[0] >= walkEnd) faceAfter = true;
            }
        }
        if (sd.tris.Count == 0) throw new Exception("0 面表(候选" + cands.Count + "段)");

        // ---- D. 材质锚名 ----
        sd.anchors = ExtractAnchors(d);

        sd.fmt = string.Format("v4.8.2a 顶点{0} 面{1} 流@0x{2:x}{3}{4}{5}",
            vcp, sd.tris.Count / 3, best,
            sd.anchors.Count > 0 ? " 锚" + sd.anchors.Count : "",
            beUsed ? " 面[大端]" : "", faceAfter ? " 面在流后" : "");
        return sd;
    }
    // 面索引解码: 大端 u16 (v4.8.1e 定案: 面表=头部后 BE u16, v4.7 曾被顶点流问题连坐冤枉)
    static int I16(byte[] d, int p, bool be) { return be ? ((d[p] << 8) | d[p + 1]) : U16(d, p); }
    // 收集 [lo,hi) 内 u16<vb 的极大段; be=大端解码; 返回 [start, len, maxval, be]
    static List<int[]> CollectRuns(byte[] d, int lo, int hi, int vcp, bool be)
    {
        int vb = Math.Max(vcp, 1024);
        var res = new List<int[]>();
        for (int par = 0; par < 2; par++)          // 面表可能落在奇数偏移 → 两种字节对齐都扫
        {
            int rs = -1, rl = 0, mx = 0; bool hot = false;
            for (int p = lo + par; p + 2 <= hi; p += 2)
            {
                int v0 = I16(d, p, be);
                if (v0 < vb)
                {
                    if (rl == 0) { rs = p; hot = false; mx = 0; }
                    rl++;
                    if (v0 >= 4) hot = true;
                    if (v0 > mx) mx = v0;
                }
                else { if (rl >= 9 && hot) res.Add(new[] { rs, rl, mx, be ? 1 : 0 }); rl = 0; hot = false; }
            }
            if (rl >= 9 && hot) res.Add(new[] { rs, rl, mx, be ? 1 : 0 });
        }
        return res;
    }
    static void FlushRun(SkiData sd, byte[] d, int runStart, int runLen, bool runHot, int vcp, bool be)
    {
        if (runLen < 9 || !runHot) return;
        // 对齐搜索 (自检发现): 段头零填充/杂字节长度非3倍数时整表错位 → 全部三角形顶点错连(细碎片)。
        // 0/1/2 三种对齐各数一遍有效三角形, 取最多者。
        int bestShift = 0, bestCnt = -1;
        for (int sh = 0; sh < 3; sh++)
        {
            int cnt = 0;
            for (int i = sh; i + 2 < runLen; i += 3)
            {
                int a = I16(d, runStart + 2 * i, be), b2 = I16(d, runStart + 2 * i + 2, be), c = I16(d, runStart + 2 * i + 4, be);
                if (a < vcp && b2 < vcp && c < vcp && a != b2 && b2 != c && a != c) cnt++;
            }
            if (cnt > bestCnt) { bestCnt = cnt; bestShift = sh; }
        }
        for (int i = bestShift; i + 2 < runLen; i += 3)
        {
            int a = I16(d, runStart + 2 * i, be), b2 = I16(d, runStart + 2 * i + 2, be), c = I16(d, runStart + 2 * i + 4, be);
            if (a < vcp && b2 < vcp && c < vcp && a != b2 && b2 != c && a != c)
            { sd.tris.Add(a); sd.tris.Add(b2); sd.tris.Add(c); }
        }
    }

    // 所有 CRT_Mtl* 块后的候选锚名 (wai_01 第一块引用头部贴图 → 全收集按序尝试)
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

    // ================================================================
    //            CRT_Actor 内嵌网格 (act_dissect v0.12 布局移植) — 武器/怪物
    // ================================================================
    class ActMeshData
    {
        public List<Vector3> pts = new List<Vector3>();
        public int nodes, meshes;
        public string fmt = "";
        public List<string> anchors = new List<string>();
    }

    static bool ActStr(byte[] d, int off, ref string s, ref int end, int maxn, int minn)
    {
        s = null; end = 0;
        if (off < 0 || off + 4 > d.Length) return false;
        int n = U32(d, off);
        if (n < minn || n > maxn || off + 4 + n > d.Length) return false;
        if (!Printable(d, off + 4, n)) return false;
        s = Str(d, off + 4, n); end = off + 4 + n;
        return true;
    }
    static float NiceRatio(byte[] d, int off, int nfloats)
    {
        int n = Math.Min(nfloats, 300); if (n <= 0) return 0f;
        int step = Math.Max(1, nfloats / n); int ok = 0, tot = 0;
        for (int i = 0; i < nfloats; i += step)
        {
            if (off + i * 4 + 4 > d.Length) break;
            float v = F32(d, off + i * 4);
            if (v == v && v > -1e7f && v < 1e7f) ok++;
            tot++;
        }
        return tot > 0 ? (float)ok / tot : 0f;
    }
    static int FindChunkAfter(byte[] d, int from)
    {
        int F = d.Length;
        for (int i = Math.Max(13, from); i + 4 < F - 4; i++)
        {
            if (d[i] != 0xFF || d[i + 1] != 0xFF) continue;
            int nl = U16(d, i + 2);
            if (nl < 4 || nl > 64 || i + 4 + nl > F) continue;
            if (!Printable(d, i + 4, nl)) continue;
            int c0 = d[i + 4];
            if (!((c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z'))) continue;
            return i;
        }
        return F;
    }
    static int FindNextNodeOrChunk(byte[] d, int from)
    {
        int F = d.Length;
        int hard = Math.Min(F, from + (F > 1048576 ? 65536 : 8192));
        int chunk = FindChunkAfter(d, from);
        if (chunk < hard) hard = chunk;
        for (int p = Math.Max(13, from); p < hard - 8; p++)
        {
            int x = U32(d, p);
            if (x < 1 || x > 4096) continue;
            string s1 = null, s2 = null; int e1 = 0, e2 = 0;
            if (!ActStr(d, p + 4, ref s1, ref e1, 64, 2)) continue;
            if (!ActStr(d, e1, ref s2, ref e2, 64, 2)) continue;
            return p;
        }
        return chunk < F ? chunk : F;
    }
    static List<string> ExtractAnchorsAct(byte[] d)
    {
        var res = new List<string>();
        string[] marks = { "CRT_MaterialLib", "CRT_Mtl", "CRT_VaSkin" };
        foreach (var mark in marks)
        {
            int from = 0;
            while (true)
            {
                int m = IndexOf(d, mark, from);
                if (m < 0) break;
                int s = m + mark.Length, hi = Math.Min(s + 300, d.Length);
                int i2 = s;
                while (i2 < hi)
                {
                    int j = i2;
                    while (j < hi && IsW(d[j])) j++;
                    if (j - i2 >= 4 && j - i2 <= 40)
                    {
                        string cand = Encoding.ASCII.GetString(d, i2, j - i2);
                        if (!cand.StartsWith("CRT") && !res.Contains(cand)) res.Add(cand);
                    }
                    i2 = (j > i2) ? j + 1 : i2 + 1;
                }
                from = m + mark.Length;
            }
        }
        return res;
    }
    static ActMeshData ParseActMesh(byte[] d)
    {
        if (d == null || d.Length < 64 || d[0] != 0xFF || d[1] != 0xFF || d[2] != 0x09 || d[3] != 0x00)
            throw new Exception("非CRT_Actor流");
        var am = new ActMeshData();
        int F = d.Length;
        // 节点表锚: [u32 1..4096][名][父名] (盲同步, 兼容变体头)
        // v4.8.1d 热修: 多候选重试(最多10个, root/Bip01/父NULL优先) —— 单锚猜错会误报"无网格节点"
        var cands = new List<int>();
        int lim = Math.Min(F - 12, 26 + 65536);
        int firstChunk = FindChunkAfter(d, 13);
        if (firstChunk < lim) lim = firstChunk;
        for (int p = 26; p < lim; p++)
        {
            int x = U32(d, p);
            if (x < 1 || x > 4096) continue;
            string s1 = null, s2 = null; int e1 = 0, e2 = 0;
            if (!ActStr(d, p + 4, ref s1, ref e1, 64, 2)) continue;
            if (!ActStr(d, e1, ref s2, ref e2, 64, 2)) continue;
            string l1 = s1.ToLower(), l2 = s2.ToLower();
            if (l1 == "root" || l1 == "bip01" || l2 == "null") cands.Insert(0, p);
            else cands.Add(p);
            if (cands.Count >= 10) break;
        }
        if (cands.Count == 0) throw new Exception("ACT节点表未定位");
        // ---- 全域网格签名扫描 (v4.8.1d, 真机指纹定案): 节点间有变换数据, 逐节点走查失效
        // (root后8KB内无[u32][名][名]模式)。改为全文件找自校验签名: [u32 V][V*3个合理浮点=位置]
        int nodeCount0 = U32(d, cands[0]);
        int p = Math.Max(13, cands[0]);
        int F2 = F;
        while (p + 16 <= F2)
        {
            int V = U32(d, p);
            if (V >= 24 && V <= 300000 && p + 4 + (long)V * 12 <= F2 && NiceRatio(d, p + 4, V * 3) >= 0.85f)
            {
                am.meshes++;
                int cap = Math.Min(V, 50000);
                for (int k = 0; k < cap; k++)
                    am.pts.Add(new Vector3(F32(d, p + 4 + k * 12), F32(d, p + 8 + k * 12), F32(d, p + 12 + k * 12)));
                p += 4 + V * 12;
                if (p + (long)V * 16 <= F2 && NiceRatio(d, p, V * 4) >= 0.6f) p += V * 16;  // 跳过uv/权重块
            }
            else p++;
        }
        if (am.meshes == 0) throw new Exception("无网格节点(纯动画/特效)");
        am.nodes = nodeCount0;
        am.fmt = string.Format("v4.8.2a ACT点云 节点表{0} 网格{1} 点{2}", nodeCount0, am.meshes, am.pts.Count);
        am.anchors = ExtractAnchorsAct(d);
        return am;
    }
    static void WalkActNodes(byte[] d, int o, int nodeCount, int F, ActMeshData am)
    {
        for (int i = 0; i < nodeCount && o < F - 8; i++)
        {
            string nm = null, pn = null; int e1 = 0, e2 = 0;
            if (!ActStr(d, o, ref nm, ref e1, 64, 2)) break;
            if (!ActStr(d, e1, ref pn, ref e2, 64, 2)) break;
            am.nodes++;
            int o3 = e2;
            int V = o3 + 4 <= F ? U32(d, o3) : 0;
            bool mesh = V >= 3 && V <= 300000 && o3 + 4 + (long)V * 12 <= F && NiceRatio(d, o3 + 4, V * 3) >= 0.7f;
            int next;
            if (mesh)
            {
                am.meshes++;
                int cap = Math.Min(V, 50000);
                for (int k = 0; k < cap; k++)
                    am.pts.Add(new Vector3(F32(d, o3 + 4 + k * 12), F32(d, o3 + 8 + k * 12), F32(d, o3 + 12 + k * 12)));
                int p4 = o3 + 4 + V * 12;
                if (p4 + (long)V * 16 <= F && NiceRatio(d, p4, V * 4) >= 0.6f) p4 += V * 16;
                next = FindNextNodeOrChunk(d, p4);
            }
            else next = FindNextNodeOrChunk(d, o3);
            if (next >= F - 8 || next <= o) break;
            o = next;
            if (am.pts.Count > 150000) break;
        }
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

    static void StartShow(string fileName)
    {
        string noExt = Path.GetFileNameWithoutExtension(fileName);
        line2 = noExt + "  <" + Cats[catI] + ">  [" + CurJob + "_" + CurSex + "]";
        _animSk = null; _animMesh = null; _animSd = null;
        if (hostPv != null && _animCo != null) { hostPv.StopCoroutine(_animCo); _animCo = null; }
        if (hostPv == null) return;
        if (_loadCo != null) hostPv.StopCoroutine(_loadCo);
        _loadCo = hostPv.StartCoroutine(LoadShowCo(fileName));
    }

    static System.Collections.IEnumerator LoadShowCo(string fileName)
    {
        byte[] d; string err = "";
        if (useAsset)
        {
            using (var wr = UnityWebRequest.Get(Application.streamingAssetsPath + "/creature/actor/" + fileName))
            {
                yield return wr.SendWebRequest();
                if (wr.result != UnityWebRequest.Result.Success) { d = null; err = wr.error; }
                else d = wr.downloadHandler.data;
            }
        }
        else
        {
            try { d = File.ReadAllBytes(Path.Combine(actorDir, fileName)); }
            catch (Exception e2) { d = null; err = e2.Message; }
        }
        string noExt = Path.GetFileNameWithoutExtension(fileName);
        if (fileName.EndsWith(".act", StringComparison.OrdinalIgnoreCase)) ShowActData(noExt, d, err);
        else ShowSkiData(noExt, d, err);
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

            int vc = sd.verts.Count;
            var vs = new Vector3[vc];
            for (int i = 0; i < vc; i++)
                vs[i] = (bind != null) ? BakeVertex(bind, sd.verts[i]) : sd.verts[i].pos0;
            var mesh = new Mesh();
            mesh.vertices = vs;
            mesh.uv = sd.uv.GetRange(0, Math.Min(sd.uv.Count, vc)).ToArray();
            mesh.triangles = sd.tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var b = mesh.bounds;
            float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            float s = ext > 1e-5f ? FitFor(name) * _zoom / ext : _zoom;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localScale = new Vector3(s, s, s);
            var bc = mesh.bounds.center;
            curModel.transform.localPosition = new Vector3(0f, 1.55f, 0f) - s * bc;
            curModel.transform.localRotation = Quaternion.Euler(ModelRot);
            curModel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = curModel.AddComponent<MeshRenderer>();

            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Diffuse");
            if (sh == null) sh = Shader.Find("Mobile/Diffuse");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) throw new Exception("无可用Shader(构建裁剪)");
            var mat = new Material(sh);
            mat.color = new Color(0.72f, 0.72f, 0.76f);
            mr.sharedMaterial = mat;

            line3 = string.Format("[v4.8.1d] {0} | 骨架={1} ski:{2}B | 贴图=...",
                sd.fmt, sk != null ? tag + ":" + sk.srcName : tag, d.Length);

            int anf = sk != null && sk.bones.Count > 0 ? sk.bones[0].nf : 0;
            if (sk != null && anf > 1)
            {
                _animSk = sk; _animSd = sd; _animMesh = mesh; _animBind = bind;
                _animCo = hostPv.StartCoroutine(PlayAnimCo());
                line3 += string.Format(" ▶动画{0}帧", anf);
            }

            if (sd.anchors != null && sd.anchors.Count >= 0 && hostPv != null)
                hostPv.StartCoroutine(LoadTexCo(name, sd.anchors, mat));
        }
        catch (Exception e)
        {
            line3 = "[v4.8.1d] " + name + " 解析失败: " + e.Message + " (◀▶手动翻页)";
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
        }
    }

    static void ShowActData(string name, byte[] d, string err)
    {
        try
        {
            if (d == null) throw new Exception("act 读取失败: " + err);
            var am = ParseActMesh(d);
            Mesh mesh = BuildPointMesh(am.pts);
            var b = mesh.bounds;
            float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            float s = ext > 1e-5f ? FitFor(name) * _zoom / ext : _zoom;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localScale = new Vector3(s, s, s);
            var bc = mesh.bounds.center;
            curModel.transform.localPosition = new Vector3(0f, 1.55f, 0f) - s * bc;
            curModel.transform.localRotation = Quaternion.Euler(ModelRot);
            curModel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = curModel.AddComponent<MeshRenderer>();

            Shader sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Legacy Shaders/VertexLit");
            if (sh == null) sh = Shader.Find("Mobile/VertexLit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Diffuse");
            if (sh == null) throw new Exception("无可用Shader(构建裁剪)");
            var mat = new Material(sh);
            mat.color = new Color(0.62f, 0.70f, 0.82f);
            mr.sharedMaterial = mat;

            line3 = "[v4.8.1d] " + am.fmt + " | 贴图=...";
            if (hostPv != null)
                hostPv.StartCoroutine(LoadTexCoAct(name, am.anchors, mat));
        }
        catch (Exception e)
        {
            line3 = "[v4.8.1d] " + name + " 点云失败: " + e.Message + " (◀▶手动翻页)";
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
        }
    }

    // 点云 → 每点一个小面向相机的双三角片
    static Mesh BuildPointMesh(List<Vector3> pts)
    {
        int N = Math.Min(pts.Count, 60000);
        var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < pts.Count; i++)
        {
            bmin = Vector3.Min(bmin, pts[i]);
            bmax = Vector3.Max(bmax, pts[i]);
        }
        float ext = Mathf.Max(bmax.x - bmin.x, Mathf.Max(bmax.y - bmin.y, bmax.z - bmin.z));
        float s = Mathf.Max(ext * 0.006f, 0.02f);
        var vs = new Vector3[N * 4];
        var tris = new int[N * 6];
        for (int i = 0; i < N; i++)
        {
            var p = pts[i];
            int b = i * 4;
            vs[b]     = p + new Vector3(-s, -s, 0);
            vs[b + 1] = p + new Vector3( s, -s, 0);
            vs[b + 2] = p + new Vector3( s,  s, 0);
            vs[b + 3] = p + new Vector3(-s,  s, 0);
            int t = i * 6;
            tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
            tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
        }
        var mesh = new Mesh();
        mesh.vertices = vs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    // ================================================================
    //                    贴图解析 v4.8.1d
    // ================================================================
    static readonly Dictionary<string, string> texResCache = new Dictionary<string, string>();

    static string TryTex(string c)
    {
        if (string.IsNullOrEmpty(c)) return null;
        if (TexExists(c + ".dds")) return c + ".dds";
        if (TexExists(c + ".png")) return c + ".png";
        return null;
    }
    static string StripDigitsTail(string s)
    {
        while (true)
        {
            int k = s.LastIndexOf('_');
            if (k < 0) return s;
            if (!IsDigits(s.Substring(k + 1))) return s;
            s = s.Substring(0, k);
        }
    }
    static string CleanAnchor(string a0)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in a0.ToLower())
            if (ch < 128 && IsW((byte)ch)) sb.Append(ch);
        string a = sb.ToString();
        if (a.Length < 4 || a.StartsWith("crt") || a == "standard" || a == "mu") return null;
        return a;
    }
    // ski 贴图: 池内同名优先(1:1) → 数字尾剥离 → 锚名清洗 → 家族兜底
    static string ResolveTex(string skiName, List<string> anchors)
    {
        string ski = (skiName ?? "").ToLower();
        if (ski.EndsWith(".ski")) ski = ski.Substring(0, ski.Length - 4);
        if (ski.EndsWith(".act")) ski = ski.Substring(0, ski.Length - 4);
        string p = TryTex(ski); if (p != null) return p;
        string b0 = ski.StartsWith("a_") ? ski.Substring(2) : ski;
        p = TryTex(b0); if (p != null) return p;
        string b2 = ski;
        while (true)
        {
            int k = b2.LastIndexOf('_');
            if (k < 0 || !IsDigits(b2.Substring(k + 1))) break;
            b2 = b2.Substring(0, k);
            p = TryTex(b2); if (p != null) return p;
            p = TryTex(b2.StartsWith("a_") ? b2.Substring(2) : "a_" + b2); if (p != null) return p;
        }
        if (anchors != null)
            foreach (var a0 in anchors)
            {
                string a = CleanAnchor(a0);
                if (a == null) continue;
                string cand = a;
                for (int guard = 0; guard < 3; guard++)
                {
                    p = TryTex(cand); if (p != null) return p;
                    p = TryTex(cand.StartsWith("a_") ? cand.Substring(2) : "a_" + cand); if (p != null) return p;
                    int k2 = cand.LastIndexOf('_');
                    if (k2 < 0) break;
                    cand = cand.Substring(0, k2);
                }
            }
        return null;
    }
    // act(武器/怪物) 贴图: 同名 → 锚名 → 层级+词根启发(5dao01→a5sword)
    static string ResolveTexAct(string name, List<string> anchors)
    {
        string p = TryTex(name); if (p != null) return p;
        if (anchors != null)
            foreach (var a0 in anchors)
            {
                string a = CleanAnchor(a0);
                if (a == null) continue;
                string cand = a;
                for (int guard = 0; guard < 3; guard++)
                {
                    p = TryTex(cand); if (p != null) return p;
                    p = TryTex(cand.StartsWith("a_") ? cand.Substring(2) : "a_" + cand); if (p != null) return p;
                    int k2 = cand.LastIndexOf('_');
                    if (k2 < 0) break;
                    cand = cand.Substring(0, k2);
                }
            }
        var m = Regex.Match(name, @"^(\d)(\d?)(dao|zhan|jian|feng|shu|gong|qiang)");
        if (m.Success)
        {
            string tier = m.Groups[1].Value;
            string fam = m.Groups[3].Value;
            string word = fam == "feng" ? "axe" : fam == "shu" ? "stick"
                        : fam == "gong" ? "bow" : fam == "qiang" ? "spear" : "sword";
            string[] cands = { "a" + tier + word, "a" + tier + word + "01", tier + word, tier + word + "01" };
            foreach (var c in cands) { p = TryTex(c); if (p != null) return p; }
        }
        return null;
    }

    static string ResolveTexCached(string skiName, List<string> anchors, bool isAct)
    {
        string key = (isAct ? "A|" : "S|") + skiName + "|" + (anchors != null && anchors.Count > 0 ? anchors[0] : "");
        string p;
        if (texResCache.TryGetValue(key, out p)) return p;
        p = isAct ? ResolveTexAct(skiName, anchors) : ResolveTex(skiName, anchors);
        texResCache[key] = p;
        return p;
    }

    static System.Collections.IEnumerator LoadTexCo(string skiName, List<string> anchors, Material mat)
    {
        string p = ResolveTexCached(skiName, anchors, false);
        yield return LoadTexCommon(p, mat, skiName);
    }
    static System.Collections.IEnumerator LoadTexCoAct(string skiName, List<string> anchors, Material mat)
    {
        string p = ResolveTexCached(skiName, anchors, true);
        yield return LoadTexCommon(p, mat, skiName);
    }
    static System.Collections.IEnumerator LoadTexCommon(string p, Material mat, string skiName)
    {
        if (p == null)
        {
            if (_texSample == null)
            {
                var ls = ListAssets("creature/texture");
                var tsb = new System.Text.StringBuilder();
                for (int i = 0; i < ls.Length && i < 6; i++) { if (i > 0) tsb.Append(','); tsb.Append(ls[i]); }
                _texSample = ls.Length == 0 ? "(目录空)" : tsb.ToString();
            }
            line3 += " ✗未命中(池样本[" + _texSample + "])";
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
            try { raw = ReadTexFile(p); } catch (Exception) { raw = null; }
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
    //                    UI (OnGUI, 1080 设计宽缩放; v4.8.1d 紧凑化)
    // ================================================================
    static GUIStyle box, label, small, btn, btnS, smallS;
    static void InitStyle()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box);
        label = new GUIStyle(GUI.skin.label); label.fontSize = 15;
        small = new GUIStyle(GUI.skin.label); small.fontSize = 12; small.wordWrap = true;
        btn = new GUIStyle(GUI.skin.button); btn.fontSize = 14; btn.alignment = TextAnchor.MiddleLeft;
        btnS = new GUIStyle(GUI.skin.button); btnS.fontSize = 11; btnS.alignment = TextAnchor.MiddleLeft;
        smallS = new GUIStyle(GUI.skin.label); smallS.fontSize = 11; smallS.alignment = TextAnchor.MiddleLeft;
    }

    void OnGUI() { DrawUI(); }
    static void DrawUI()
    {
        InitStyle();
        float sc = Screen.width / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sc, sc, 1f));
        float H = Screen.height / sc;   // 横屏约 508

        // 顶栏 (紧凑: 58px)
        GUI.Box(new Rect(0, 0, 1080, 58), GUIContent.none, box);
        GUI.Label(new Rect(10, 3, 760, 26), title, label);
        GUI.Label(new Rect(10, 29, 760, 26), line2, small);
        GUI.Label(new Rect(772, 3, 300, 52), line3, small);

        // 分类: 一竖排 (左列, 按槽位排序, 186×26)
        float cw = 186f, ch = 26f;
        GUI.Box(new Rect(0, 60, cw + 12, CatOrder.Length * (ch + 2) + 8), GUIContent.none, box);
        for (int di = 0; di < CatOrder.Length; di++)
        {
            int id = CatOrder[di];
            bool sel = id == catI;
            var old = GUI.backgroundColor;
            if (sel) GUI.backgroundColor = new Color(0.30f, 0.65f, 0.95f);
            if (GUI.Button(new Rect(6, 64 + di * (ch + 2), cw, ch), (sel ? "▶ " : "") + Cats[id], sel ? btnS : smallS))
            { catI = id; FilterCat(); ShowCurrent(); }
            GUI.backgroundColor = old;
        }

        // 职业按钮 (右上, 紧凑)
        if (GUI.Button(new Rect(1080 - 192, 64, 186, 34), "职业 " + CurJob + "_" + CurSex + " ▶", btn))
        {
            sexI++;
            if (sexI >= Sexes.Length) { sexI = 0; jobI = (jobI + 1) % Jobs.Length; }
            skelCache.Clear(); rotY = 0;
            RebuildCat(); ShowCurrent();
        }

        // 底部翻页条
        float yb = H - 56;
        GUI.Box(new Rect(0, yb - 6, 1080, 62), GUIContent.none, box);
        if (GUI.Button(new Rect(10, yb, 84, 48), "◀", btn))
        { curIdx = (curIdx - 1 + curSkiList.Count) % Math.Max(1, curSkiList.Count); ShowCurrent(); }
        if (GUI.Button(new Rect(102, yb, 84, 48), "▶", btn))
        { curIdx = (curIdx + 1) % Math.Max(1, curSkiList.Count); ShowCurrent(); }
        GUI.Label(new Rect(196, yb, 700, 48), curSkiList.Count == 0
            ? "(本职业无此部件) 点右上「职业」换人 / 点左侧其他分类"
            : string.Format("{0}/{1}  {2}", curIdx + 1, curSkiList.Count, Path.GetFileNameWithoutExtension(curSki)), small);
        if (GUI.Button(new Rect(916, yb, 60, 48), "＋", btn)) { _zoom = Mathf.Clamp(_zoom * 1.2f, 0.2f, 8f); ShowCurrent(); }
        if (GUI.Button(new Rect(982, yb, 60, 48), "－", btn)) { _zoom = Mathf.Clamp(_zoom / 1.2f, 0.2f, 8f); ShowCurrent(); }

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
//  DdsV4 — DDS 解码 (不变)
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
