using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ============================================================================
// CRT 资源查看器 v3 —— 「背剑的术士」部件预览台
//
// 逆向成果（全部经 591 个真实资源文件验证）：
//   .ski  CRT_SkelSkin  = 对象头(FFFF+类名) + 名字 + 子块(材质名/贴图名/面索引)
//                         + 材质库 + 顶点(法线/UV/骨骼引用{骨号,权重,骨空间位置,法线})
//   .act  CRT_Actor     = 对象头 + 版本串(tooth0708) + 骨骼树(名字/父名/位移键/旋转四元数)
//                         + 内嵌 CRT_VaSkin(武器网格，顶点为烘焙坐标)
//   .dds  DXT1/DXT3/DXT5 标准头，软件解码为 Texture2D
//
// v3 展示：武器(剑/杵，来自 act 内嵌 VaSkin + 原生 dds 贴图) + 身体各部件
//          (clo/glo/sho/wai/hea，来自 ski) + 触摸旋转 + 部件换装按钮
// v4 计划：拿到 role_wt_m_01.act 身体骨骼后，按 bind 矩阵整装拼合 + 动画
// ============================================================================

public class CrtViewer : MonoBehaviour
{
    // ======================= 二进制小工具 =======================
    static ushort U16(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }
    static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
    static float F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }

    static bool ClassAt(byte[] b, int o, string cls)
    {
        if (o + 2 > b.Length || b[o] != 0xFF || b[o + 1] != 0xFF) return false;
        int l = U16(b, o + 2);
        if (l != cls.Length || o + 4 + l > b.Length) return false;
        for (int i = 0; i < l; i++) if (b[o + 4 + i] != (byte)cls[i]) return false;
        return true;
    }

    static string ReadStr(byte[] b, ref int o)
    {
        uint n = U32(b, o); o += 4;
        string s = System.Text.Encoding.GetEncoding(28591).GetString(b, o, (int)n); o += (int)n;  // Latin-1 保字节(GBK 名字不损坏)
        return s;
    }

    static bool Printable(byte[] b, int o, int n)
    {
        if (o + n > b.Length) return false;
        for (int i = 0; i < n; i++) { byte c = b[o + i]; if (c < 0x20 || c == 0x7F) return false; }  // 允许 GBK 高位字节(武器中文名)
        return true;
    }

    // ======================= 数据结构 =======================
    struct BoneRef { public int bone; public float w; public Vector3 pos, nor; }

    class SkiSubset { public string mtl, tex; public ushort[] faces; }

    class SkiFile
    {
        public string name;
        public List<SkiSubset> subs = new List<SkiSubset>();
        public List<Vector3> verts = new List<Vector3>();   // 主权重骨骼空间位置(已按权重混合)
        public List<Vector3> nors = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
    }

    class ActSkin
    {
        public string cls, name;
        public List<SkiSubset> subs = new List<SkiSubset>();
        public List<Vector3> verts = new List<Vector3>();   // VaSkin 交错帧0: pos/nor/uv
        public List<Vector3> nors = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
    }

    class ActFile
    {
        public string ver;
        public List<string> bones = new List<string>();     // "名字:父名"
        public List<ActSkin> skins = new List<ActSkin>();
    }

    // ======================= .ski 解析 =======================
    static SkiFile LoadSki(byte[] b)
    {
        var f = new SkiFile();
        int o = 4 + U16(b, 2);              // 跳过对象头
        f.name = ReadStr(b, ref o);         // s_a_wt_m_clo_00
        o += 2;                             // 两个布尔
        int subCnt = (int)U32(b, o); o += 4;
        for (int s = 0; s < subCnt; s++)
        {
            var ss = new SkiSubset { mtl = ReadStr(b, ref o), tex = ReadStr(b, ref o) };
            int fc = (int)U32(b, o); o += 4;
            ss.faces = new ushort[fc];
            for (int i = 0; i < fc; i++) { ss.faces[i] = U16(b, o); o += 2; }
            o += 1;                         // 子块尾标记 0x01
            f.subs.Add(ss);
        }
        // 跳过材质库：直接扫顶点区 [u32 顶点数 >= 最大索引+1] 且整段可解析到文件尾
        int idxMax = 0;
        foreach (var ss in f.subs) foreach (ushort idx in ss.faces) if (idx > idxMax) idxMax = idx;

        for (int cand = o; cand < b.Length - 40; cand++)
        {
            int cnt = (int)U32(b, cand);
            if (cnt < idxMax + 1 || cnt > 4000) continue;
            int p = cand + 4; bool ok = true;
            var V = new List<Vector3>(cnt); var N = new List<Vector3>(cnt); var U = new List<Vector2>(cnt);
            for (int v = 0; v < cnt && ok; v++)
            {
                Vector3 nor = new Vector3(F32(b, p), F32(b, p + 4), F32(b, p + 8)); p += 12;
                Vector2 uv = new Vector2(F32(b, p), F32(b, p + 4)); p += 8;
                int rc = (int)U32(b, p); p += 4;
                if (rc < 1 || rc > 8) { ok = false; break; }
                Vector3 bestPos = Vector3.zero, bestNor = Vector3.zero; float bestW = -1;
                float wSum = 0; Vector3 pAcc = Vector3.zero, nAcc = Vector3.zero;
                for (int r = 0; r < rc; r++)
                {
                    int bone = b[p]; float w = F32(b, p + 1);
                    var rp = new Vector3(F32(b, p + 5), F32(b, p + 9), F32(b, p + 13));
                    var rn = new Vector3(F32(b, p + 17), F32(b, p + 21), F32(b, p + 25));
                    if (b[p + 29] != 1) { ok = false; break; }
                    p += 30;
                    if (w > bestW) { bestW = w; bestPos = rp; bestNor = rn; }
                    wSum += w; pAcc += rp * w; nAcc += rn * w;
                }
                if (!ok) break;
                // 主权重优先：多骨顶点按权重混合，单骨顶点取原值
                Vector3 P = (rc == 1 || wSum <= 0.001f) ? bestPos : pAcc / wSum;
                Vector3 Nn = (rc == 1 || wSum <= 0.001f) ? bestNor : nAcc / wSum;
                if (!Sanity(P) || !Sanity(Nn)) { ok = false; break; }
                V.Add(P); N.Add(Nn); U.Add(uv);
            }
            if (!ok) continue;
            int tail = b.Length - p;
            if (tail > 4) continue;
            bool tailOk = true;
            for (int t = p; t < b.Length; t++) if (b[t] != 1) { tailOk = false; break; }
            if (!tailOk) continue;

            f.verts = V; f.nors = N; f.uvs = U;
            return f;
        }
        throw new Exception("顶点区未定位: " + f.name);
    }

    static bool Sanity(Vector3 v) { return Mathf.Abs(v.x) < 1e5f && Mathf.Abs(v.y) < 1e5f && Mathf.Abs(v.z) < 1e5f; }

    // ======================= .act 解析 =======================
    static ActFile LoadAct(byte[] b)
    {
        var f = new ActFile();
        int o = 4 + U16(b, 2);              // CRT_Actor 对象头
        // 版本串变长(写入的是 strlen)：扫到第一个合理的速度浮点
        int p = o;
        while (p < o + 16 && p + 4 <= b.Length)
        {
            float sp = F32(b, p);
            if (sp > 0.0001f && sp < 1000f && Printable(b, o, p - o)) break;
            p++;
        }
        f.ver = Encoding.ASCII.GetString(b, o, p - o); o = p;
        o += 4 + 2;                         // 速度 + 两个布尔
        int boneCnt = (int)U32(b, o); o += 4;

        bool IsStr(int q, out string s, int max = 40)
        {
            s = null;
            if (q + 4 > b.Length) return false;
            int n = (int)U32(b, q);
            if (n < 1 || n > max || q + 4 + n > b.Length || !Printable(b, q + 4, n)) return false;
            s = Encoding.ASCII.GetString(b, q + 4, n); return true;
        }

        for (int bi = 0; bi < boneCnt; bi++)
        {
            // 向前扫下一个合法 [名字][父名] 骨骼头（尾部 ribbon/版本门数据跳过）
            int q = o; string nm = null, pn = null; int nameEnd = 0;
            while (q < b.Length - 20)
            {
                if (IsStr(q, out nm) && IsStr(q + 4 + nm.Length, out pn) &&
                    (pn == "NULL" || pn.Length >= 3))
                    { nameEnd = q + 4 + nm.Length + 4 + pn.Length; break; }
                q++;
            }
            if (nm == null) throw new Exception("骨骼头未找到 #" + bi);
            f.bones.Add(nm + ":" + pn);
            q = nameEnd;
            int tc = (int)U32(b, q); q += 4 + tc * 12;
            int rc = (int)U32(b, q); q += 4 + rc * 16;
            o = q;
        }

        // 全文件扫内嵌皮肤对象（武器网格 VaSkin 等）
        for (int i = o; i < b.Length - 20; i++)
        {
            string cls = null;
            if (b[i] == 0xFF && b[i + 1] == 0xFF)
            {
                int l = U16(b, i + 2);
                if (l == 10 && i + 14 <= b.Length && Encoding.ASCII.GetString(b, i + 4, 10) == "CRT_VaSkin") cls = "CRT_VaSkin";
                else if (l == 12 && i + 16 <= b.Length && Encoding.ASCII.GetString(b, i + 4, 12) == "CRT_SkelSkin") cls = "CRT_SkelSkin";
            }
            if (cls == null) continue;
            try
            {
                var sk = LoadEmbeddedSkin(b, i + 4 + U16(b, i + 2), cls);
                if (sk != null) f.skins.Add(sk);
            }
            catch { /* 内嵌对象解析失败则跳过 */ }
        }
        return f;
    }

    static ActSkin LoadEmbeddedSkin(byte[] b, int o, string cls)
    {
        var sk = new ActSkin { cls = cls };
        sk.name = ReadStr(b, ref o);
        o += 2;
        int subCnt = (int)U32(b, o); o += 4;
        for (int s = 0; s < subCnt; s++)
        {
            var ss = new SkiSubset { mtl = ReadStr(b, ref o), tex = ReadStr(b, ref o) };
            int fc = (int)U32(b, o); o += 4;
            ss.faces = new ushort[fc];
            for (int i = 0; i < fc; i++) { ss.faces[i] = U16(b, o); o += 2; }
            o += 1;
            sk.subs.Add(ss);
        }
        int idxMax = 0;
        foreach (var ss in sk.subs) foreach (ushort idx in ss.faces) if (idx > idxMax) idxMax = idx;
        if (idxMax < 1) return null;

        // 顶点区：扫描 [u32 顶点数] + 32B 交错(pos3f+nor3f+uv2f)，浮点全部合理
        for (int cand = o; cand < b.Length - 40; cand++)
        {
            int cnt = (int)U32(b, cand);
            if (cnt < idxMax + 1 || cnt > 5000) continue;
            int p = cand + 4; bool ok = true;
            var V = new List<Vector3>(cnt); var N = new List<Vector3>(cnt); var U = new List<Vector2>(cnt);
            for (int v = 0; v < cnt && ok; v++)
            {
                var pos = new Vector3(F32(b, p), F32(b, p + 4), F32(b, p + 8));
                var nor = new Vector3(F32(b, p + 12), F32(b, p + 16), F32(b, p + 20));
                var uv = new Vector2(F32(b, p + 24), F32(b, p + 28)); p += 32;
                if (!Sanity(pos) || !Sanity(nor) || !Sanity(uv)) ok = false;
                else { V.Add(pos); N.Add(nor); U.Add(uv); }
            }
            if (!ok || V.Count == 0) continue;
            sk.verts = V; sk.nors = N; sk.uvs = U;
            return sk;
        }
        return null;
    }

    // ======================= .dds 解码 (DXT1/3/5) =======================
    static Texture2D LoadDds(byte[] b)
    {
        if (b.Length < 128 || b[0] != 'D' || b[1] != 'D' || b[2] != 'S' || b[3] != ' ') return null;
        int h = (int)U32(b, 12), w = (int)U32(b, 16);
        string fourcc = Encoding.ASCII.GetString(b, 84, 4);
        int dataOff = 128;
        var px = new Color32[w * h];
        int bw = (w + 3) / 4, bh = (h + 3) / 4;

        if (fourcc == "DXT1")
        {
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int o = dataOff + (by * bw + bx) * 8;
                    if (o + 8 > b.Length) break;
                    DecodeColorBlock(b, o, px, bx * 4, by * 4, w, h, null);
                }
        }
        else if (fourcc == "DXT3" || fourcc == "DXT5")
        {
            bool dxt5 = fourcc == "DXT5";
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int o = dataOff + (by * bw + bx) * 16;
                    if (o + 16 > b.Length) break;
                    byte[] a = dxt5 ? DecodeAlphaDXT5(b, o) : DecodeAlphaDXT3(b, o);
                    DecodeColorBlock(b, o + 8, px, bx * 4, by * 4, w, h, a);
                }
        }
        else return null;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    static void DecodeColorBlock(byte[] b, int o, Color32[] px, int x0, int y0, int w, int h, byte[] alpha)
    {
        ushort c0 = U16(b, o), c1 = U16(b, o + 2);
        uint bits = U32(b, o + 4);
        var pal = new Color32[4];
        pal[0] = Rgb565(c0); pal[1] = Rgb565(c1);
        if (c0 > c1) { pal[2] = Lerp2(pal[0], pal[1], 1f / 3f); pal[3] = Lerp2(pal[0], pal[1], 2f / 3f); }
        else { pal[2] = Lerp2(pal[0], pal[1], 0.5f); pal[3] = new Color32(0, 0, 0, 0); }
        for (int i = 0; i < 16; i++)
        {
            int cx = x0 + i % 4, cy = y0 + i / 4;
            if (cx >= w || cy >= h) continue;
            int sel = (int)((bits >> (i * 2)) & 3);
            byte a = alpha != null ? alpha[i] : (byte)(c0 <= c1 && sel == 3 ? 0 : 255);
            var c = pal[sel]; px[cy * w + cx] = new Color32(c.r, c.g, c.b, a);
        }
    }

    static byte[] DecodeAlphaDXT3(byte[] b, int o)
    {
        var a = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            int v = b[o + i / 2];
            a[i] = (byte)((i % 2 == 0) ? (v & 0x0F) * 17 : (v >> 4) * 17);
        }
        return a;
    }

    static byte[] DecodeAlphaDXT5(byte[] b, int o)
    {
        int a0 = b[o], a1 = b[o + 1];
        var pal = new byte[8]; pal[0] = (byte)a0; pal[1] = (byte)a1;
        if (a0 > a1) { for (int i = 0; i < 6; i++) pal[2 + i] = (byte)((a0 * (5 - i) + a1 * (i + 1)) / 6); }
        else { for (int i = 0; i < 4; i++) pal[2 + i] = (byte)((a0 * (3 - i) + a1 * (i + 1)) / 4); pal[6] = 0; pal[7] = 255; }
        var a = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            int bit = i * 3, byteIdx = o + 2 + bit / 8, shift = bit % 8;
            int v = (b[byteIdx] | (byteIdx + 1 < b.Length ? b[byteIdx + 1] << 8 : 0)) >> shift;
            a[i] = pal[v & 7];
        }
        return a;
    }

    static Color32 Rgb565(ushort c)
    {
        return new Color32((byte)((c >> 11) * 255 / 31), (byte)(((c >> 5) & 63) * 255 / 63), (byte)((c & 31) * 255 / 31), 255);
    }

    static Color32 Lerp2(Color32 a, Color32 b, float t)
    {
        return new Color32((byte)Mathf.RoundToInt(Mathf.Lerp(a.r, b.r, t)), (byte)Mathf.RoundToInt(Mathf.Lerp(a.g, b.g, t)),
                           (byte)Mathf.RoundToInt(Mathf.Lerp(a.b, b.b, t)), 255);
    }

    // ======================= 展台 =======================
    public static bool ViewerActive;        // HelloWorld 看到 true 就让出屏幕

    class PartGroup { public string label; public string[] files; public bool isAct; public string dir; }

    static readonly PartGroup[] Groups = new PartGroup[]
    {
        new PartGroup{ label="武器·剑", dir="creature/actor/", isAct=true,
            files=new []{"w_wtj_m_01.act","w_wtj_m_02.act","w_wtj_m_03.act","w_wtj_m_04.act","w_wtj_m_05.act","w_wtj_m_06.act","w_wtj_m_07.act","w_wtj_m_08.act","w_wtj_m_09.act","w_wtj_m_10.act"}},
        new PartGroup{ label="武器·杵", dir="creature/actor/", isAct=true,
            files=new []{"w_wtc_m_01.act","w_wtc_m_02.act","w_wtc_m_03.act","w_wtc_m_04.act","w_wtc_m_05.act","w_wtc_m_06.act","w_wtc_m_07.act","w_wtc_m_08.act","w_wtc_m_09.act"}},
        new PartGroup{ label="服装", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_clo_00.ski","a_wt_m_clo_01.ski","a_wt_m_clo_02.ski","a_wt_m_clo_03.ski","a_wt_m_clo_04.ski","a_wt_m_clo_05.ski","a_wt_m_clo_06.ski","a_wt_m_clo_07.ski","a_wt_m_clo_08.ski","a_wt_m_clo_09.ski","a_wt_m_clo_fashion01.ski","a_wt_m_clo_fashion02.ski","a_wt_m_clo_suit02.ski","a_wt_m_clo_suit03.ski"}},
        new PartGroup{ label="头", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_hea_00.ski","a_wt_m_hea_01.ski","a_wt_m_hea_02.ski","a_wt_m_hea_03.ski","a_wt_m_hea_04.ski","a_wt_m_hea_05.ski"}},
        new PartGroup{ label="手", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_glo_00.ski","a_wt_m_glo_01.ski","a_wt_m_glo_02.ski","a_wt_m_glo_03.ski","a_wt_m_glo_04.ski","a_wt_m_glo_05.ski","a_wt_m_glo_06.ski","a_wt_m_glo_07.ski","a_wt_m_glo_08.ski","a_wt_m_glo_09.ski"}},
        new PartGroup{ label="脚", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_sho_00.ski","a_wt_m_sho_01.ski","a_wt_m_sho_02.ski","a_wt_m_sho_03.ski","a_wt_m_sho_04.ski","a_wt_m_sho_05.ski","a_wt_m_sho_06.ski","a_wt_m_sho_07.ski","a_wt_m_sho_08.ski","a_wt_m_sho_09.ski"}},
        new PartGroup{ label="外装", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_wai_01.ski","a_wt_m_wai_02.ski","a_wt_m_wai_03.ski","a_wt_m_wai_04.ski","a_wt_m_wai_05.ski","a_wt_m_wai_06.ski","a_wt_m_wai_07.ski","a_wt_m_wai_08.ski","a_wt_m_wai_09.ski"}},
    };

    Transform _pivot;                       // 当前模型挂点(自动旋转)
    int _group, _idx;
    string _status = "初始化…";
    string _info = "";
    bool _loading;
    float _yaw = 30f, _pitch = 10f;
    Vector2 _touchLast;
    readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();
    readonly Dictionary<string, GameObject> _modelCache = new Dictionary<string, GameObject>();

    void Awake()
    {
        ViewerActive = true;
        // 灯光
        var go = new GameObject("ViewerLight");
        var li = go.AddComponent<Light>();
        li.type = LightType.Directional; li.intensity = 1.1f;
        go.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        // 相机
        var cam = Camera.main;
        if (cam != null) { cam.transform.position = new Vector3(0, 0, -70f); cam.transform.rotation = Quaternion.identity; cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f); cam.fieldOfView = 42f; }
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.60f);
        // 挂点
        _pivot = new GameObject("Pivot").transform;
        Load(0, 0);
    }

    void Load(int g, int i)
    {
        if (_loading) return;
        _group = g; _idx = i;
        var grp = Groups[g];
        string file = grp.files[i];
        string path = grp.dir + file;
        string key = grp.label + "/" + file;
        StartCoroutine(LoadModel(key, path, grp.isAct));
    }

    IEnumerator LoadModel(string key, string path, bool isAct)
    {
        _loading = true;
        if (_modelCache.ContainsKey(key))
        {
            Show(key); _loading = false; yield break;
        }
        _status = "加载 " + path + " …";
        var req = UnityWebRequest.Get(SA(path));
        req.timeout = 20;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            _status = "读取失败: " + req.error; _loading = false; yield break;
        }
        byte[] d = req.downloadHandler.data;
        try
        {
            GameObject model;
            string texName;
            if (isAct)
            {
                var act = LoadAct(d);
                if (act.skins.Count == 0) throw new Exception("无内嵌网格");
                var sk = act.skins[0];
                texName = sk.subs[0].tex;
                model = BuildMeshObject(sk.verts, sk.nors, sk.uvs, sk.subs[0].faces, texName);
                _info = string.Format("{0}  骨骼{1}根  网格{2}顶点/{3}面  贴图={4}",
                    act.ver, act.bones.Count, sk.verts.Count, sk.subs[0].faces.Length / 3, texName);
            }
            else
            {
                var ski = LoadSki(d);
                texName = ski.subs[0].tex;
                model = BuildMeshObject(ski.verts, ski.nors, ski.uvs, ski.subs[0].faces, texName);
                _info = string.Format("{0}  {1}顶点/{2}面  贴图={3}",
                    ski.name, ski.verts.Count, ski.subs[0].faces.Length / 3, texName);
            }
            model.transform.SetParent(_pivot, false);
            _modelCache[key] = model;
            Show(key);
            _status = "";
        }
        catch (Exception e)
        {
            _status = "解析失败: " + e.Message;
        }
        _loading = false;
    }

    GameObject BuildMeshObject(List<Vector3> V, List<Vector3> N, List<Vector2> U, ushort[] faces, string texName)
    {
        // 归一化：让模型稳定占屏（各部件尺寸差异大）
        var bmin = V[0]; var bmax = V[0];
        foreach (var v in V) { bmin = Vector3.Min(bmin, v); bmax = Vector3.Max(bmax, v); }
        var center = (bmin + bmax) * 0.5f;
        float dim = Mathf.Max(bmax.x - bmin.x, Mathf.Max(bmax.y - bmin.y, bmax.z - bmin.z));
        float scale = dim > 0.001f ? 40f / dim : 1f;

        var verts = new Vector3[V.Count];
        for (int i = 0; i < V.Count; i++) verts[i] = (V[i] - center) * scale;
        var tris = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++) tris[i] = faces[i];

        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.normals = N.ToArray(); mesh.uv = U.ToArray();
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        var go = new GameObject("CRT_" + texName);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();

        var mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        var tex = GetTexture(texName);
        if (tex != null) mat.mainTexture = tex;
        else mat.color = new Color(0.72f, 0.70f, 0.66f);   // 无贴图时用灰金属色
        mr.sharedMaterial = mat;
        return go;
    }

    Texture2D GetTexture(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_texCache.TryGetValue(name, out var t)) return t;
        return null;    // 贴图异步补；先渲染几何
    }

    IEnumerator LoadTextureLater(string name, Material mat)
    {
        var tex = GetTexture(name);
        if (tex != null) yield break;
        var req = UnityWebRequest.Get(SA("creature/texture/" + name + ".dds"));
        req.timeout = 20;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;
        var t = LoadDds(req.downloadHandler.data);
        if (t != null) { _texCache[name] = t; if (mat != null) { mat.mainTexture = t; mat.color = Color.white; } }
    }

    void Show(string key)
    {
        foreach (var kv in _modelCache) kv.Value.SetActive(kv.Key == key);
        // 补贴图（解析时不知道贴图是否存在，展示时再拉）
        var go = _modelCache[key];
        var mr = go.GetComponent<MeshRenderer>();
        string texName = go.name.Substring(4);
        StartCoroutine(LoadTextureLater(texName, mr.sharedMaterial));
    }

    static string SA(string rel)
    {
        string b = Application.streamingAssetsPath;
        if (!b.EndsWith("/")) b += "/";
        if (!b.StartsWith("jar:") && !b.StartsWith("http")) b = "file://" + b;
        return b + rel;
    }

    void Update()
    {
        if (_pivot == null) return;
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved) { _yaw += t.deltaPosition.x * 0.4f; _pitch = Mathf.Clamp(_pitch - t.deltaPosition.y * 0.3f, -80f, 80f); }
        }
        _yaw += Time.unscaledDeltaTime * 12f;
        _pivot.rotation = Quaternion.Euler(_pitch, _yaw, 0);
    }

    void OnGUI()
    {
        float h = Screen.height, w = Screen.width;
        GUI.backgroundColor = new Color(0.1f, 0.1f, 0.14f, 0.85f);

        var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, (int)(h * 0.030f)), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.35f, 1f, 0.45f) } };
        GUI.Label(new Rect(12, 8, w - 24, h * 0.05f), "v3 角色预览台 — CRT 格式直读 (ski/act/dds)", title);

        var info = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, (int)(h * 0.017f)), normal = { textColor = Color.white } };
        GUI.Label(new Rect(12, h * 0.052f, w - 24, h * 0.05f), _status.Length > 0 ? _status : _info, info);

        // 部件组按钮
        int n = Groups.Length;
        float bw = (w - 24) / n;
        for (int g = 0; g < n; g++)
        {
            var bs = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(11, (int)(h * 0.018f)) };
            bool cur = g == _group;
            GUI.backgroundColor = cur ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.16f, 0.17f, 0.22f);
            if (GUI.Button(new Rect(12 + g * bw, h * 0.62f, bw - 4, h * 0.055f), Groups[g].label, bs) && !_loading)
                Load(g, 0);
        }

        // 当前组内 ◀ 上一件 / 下一件 ▶
        var grp = Groups[_group];
        GUI.backgroundColor = new Color(0.16f, 0.17f, 0.22f);
        var big = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(18, (int)(h * 0.030f)) };
        if (GUI.Button(new Rect(12, h * 0.69f, w * 0.16f, h * 0.07f), "◀", big) && !_loading)
            Load(_group, (_idx - 1 + grp.files.Length) % grp.files.Length);
        if (GUI.Button(new Rect(w - 12 - w * 0.16f, h * 0.69f, w * 0.16f, h * 0.07f), "▶", big) && !_loading)
            Load(_group, (_idx + 1) % grp.files.Length);

        var mid = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, (int)(h * 0.020f)), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.4f) }, wordWrap = true };
        GUI.Label(new Rect(w * 0.18f, h * 0.70f, w * 0.64f, h * 0.06f),
            string.Format("{0}  [{1}/{2}]\n{3}", grp.label, _idx + 1, grp.files.Length, grp.files[_idx]), mid);

        var tip = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, (int)(h * 0.015f)), alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(0.75f, 0.78f, 0.85f) }, wordWrap = true };
        GUI.Label(new Rect(12, h * 0.80f, w - 24, h * 0.19f),
            "触摸滑动=旋转展台  ·  v3 只展示单件部件(骨骼局部坐标)\n" +
            "整装术士(身体拼合+骨骼动画)待 v4：需从游戏包提取 role_wt_m_01.act\n" +
            "Swap test: 服装/手套/鞋/外装/头/武器 已可实时切换", tip);
    }
}
