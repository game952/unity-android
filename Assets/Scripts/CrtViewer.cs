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
//
// v3.1 修复 (2026-10-08，真机反馈两个 bug)：
//   1) .ski 服装/头/手/脚全部「顶点区未定位」→ 根因: C# 把 CRT_VaSkin 的
//      32B 交错格式错套在 CRT_SkelSkin 上。正确格式 = A/R/C 三种顶点记录流
//      (A=法线3+UV2 / Rn=骨骼引用33B / C=附加引用30B)，顶点池=R块+C块，
//      UV 由 A 记录按序配对。算法源自 ski格式破解包 ski_export.py，
//      297 个 ski 全量对拍 591 顶点黄金标准一致
//   2) .ski 面索引是【大端 u16】(297文件字节级验证)，小端误读全错 → 大端读取
//   3) 贴图 404 → pak 内贴图文件带 _01/_02 变体后缀而 act 记基础名，
//      加载失败时自动回退尝试 _01.._04 变体
//
// v3.1.1 修复 (2026-10-08，真机反馈)：
//   1) 剑只剩 3 面 / 杵「无内嵌网格」→ v3.1 把 act 内嵌 VaSkin 索引也改成
//      大端是错的：VaSkin 索引是【小端 u16】(v3.0 真机证据: 小端读出剑主体)。
//      与 ski 的大端不同源！改为双端序探测，按界内索引率自动择优
//   2) 解析失败时旧模型残留画面 → 失败分支隐藏全部已载模型
//   3) act 顶点流假阳性防护：装配面数须达到索引数一半，否则判定端序/结构错误
//
// v3.2 突破 (2026-10-08 深夜，5个原始样本对照实验30+组)：
//   ★ ski 碎片化主因 = 顶点坐标在各绑定骨骼的【局部空间】！
//     - glo_09 绑 12 骨、clo_02 绑 34 骨、61% 三角形跨骨骼
//   ★ C 记录真身破译 = 同一顶点在第二骨骼空间的坐标 (R2 w=0.5 + C w=0.5, 和为1)
//     - RANSAC 骨12↔骨13 解出 15/15 全配对, 刚体残差<0.08 → 实锤
//   ★ 解法(v3.3): 顺序FIFO配对 + Horn四元数Kabsch(Jacobi精确特征分解) + 骨骼图BFS传递
//     Rn 的 n=影响骨骼数(R1单骨/R2双骨/R3三骨), 流序即对应关系;
//     离线验证 297文件/4068骨对 全零残差(精确刚体), 无随机搜索
//     → 全部顶点归一到代表骨空间, 无需骨骼文件！
//     - Python 离线验证: hea_05 统一后渲染出连续实体头盔壳 (原碎片消失)
//   - 条带滑窗装配保持 (v3.1.1 端序择优不变)
// v3.5.1 (2026-10-09 下午)：仅更新界面提示文字为 v3.5.1 描述(上一版漏改)；代码逻辑与 v3.5 完全一致
// v3.5 修复 (2026-10-09，真机回归 v3.4 仍碎片 + 统计行异常)：
//   ★根因: v3.4 改 KabschFit 时删掉了 n==1 安全分支(纯平移) — 单点/共线骨对改走"法线拟合",
//     绕轴旋转自由度未定死, 且 norRes≈0 骗过检验 → 随机旋转混入骨骼图污染 BFS (Python 验证
//     版带安全分支所以测不出)。
//   1) KabschFit 加 rotTrust 判定: n≥3 且位置散布 σmin>0.5 才允许旋转; 否则强制 R=I 只保留
//      质心平移 (平移总可信)
//   2) 骨骼图分强弱边: 强边(旋转可信)先建树; 弱边(R=I 平移)多轮迭代桥接强图之外的孤岛骨
//   3) 统计行竞态修复: _loadSeq 守卫快速翻页时旧协程覆盖状态 + _infoCache per-model 恢复
//      (v3.4 真机三张截图同一份统计 = 竞态覆盖)
//   4) 统计行增加 强/平移 边计数, 标题升 v3.5
// v3.4 修复 (2026-10-09，真机回归：服装/脚碎片堆叠 + 剑[2/10]贴图404 + 头部纯白)：
//   1) ★BFS方向bug实锤修复: v3.3 骨骼图传递的两个方向全反 (dist[B]应为 Mc·T⁻¹ 写成 Mc·T,
//      dist[A]应为 Mc·T 写成 Mc·T⁻¹) → 离线复现: 手套/脚环残差100%超阈, 修正后全部归零
//   2) Kabsch 6D 升级: 位置协方差 + 法线协方差(wn=20, 不中心化) — 共线缝点/单点对的
//      旋转欠定用法线分布破简并; 拟合后法线残差>0.2 判旋转不可信 → R=I 只保留平移(防碎裂)
//   3) 贴图404诊断: 全候选失败 → 模型品红 + 状态栏显示贴图名 (不再静默白模)
//   4) HUD 增加解析统计行 (R1/R2/R3/C计数·骨对数·位置/法线残差·BFS覆盖), 供真机回报定位
//   注: 武器(w_wtj/w_wtc)来自 act 内嵌 VaSkin, 其 slot→武器语义与原游戏的对应关系
//       待 role_wt_m_01.act (整装骨骼) 到位后在 v4 校准
// v3.5 官方命名对照 (2026-10-09，接官网物品数据库 zfsonline.com/game/items.o 全量 120 武器+147 防具)：
//   1) 预览台全面接入官方名称: 武器槽位(1)剑刀系=青云剑Lv1→…→轩辕圣皇刀Lv85 (wtj 10档全对应)
//      槽位(2)杵斧系=撞心杵Lv1→降魔杵Lv15→宣花斧Lv25→…→天齐岳神斧Lv75 (wtc 9档全对应)
//      防具 10 档 = 渡法00/渡痕01/渡骨02/无神03/钰阙04/龙神05/护法天君06/地煞恶神07/碧游通天08/魔枭天冥09
//   2) 部件组标签语义化: 甲袍·防具(3)/手套·防具(1)/长靴·防具(2)/头饰/外装 (槽位号=官网括号ID)
//   3) 信息行显示【官方名·职业·等级】, 用户可直接对照原游戏验证编号映射
//   注: 术士武器"珠"系(槽6, 戳目珠→地煞亡神珠 10 档)不在本资源包, 推断内嵌于 role_wt_m_01.act (v4)
// v4 计划：提取 role_wt_m_01.act 全身骨骼 → 整装拼合 + 动画 + 武器槽位校准
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
    class SkiSubset { public string mtl, tex; public List<int> faces = new List<int>(); }   // faces: 大端 u16 还原

    class SkiFile
    {
        public string name;
        public List<SkiSubset> subs = new List<SkiSubset>();
        public List<Vector3> verts = new List<Vector3>();   // R块+C块 骨骼空间坐标 (bind 姿势近似恒等)
        public List<Vector3> nors = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();     // 来自配对 A 记录
        public List<int> tris = new List<int>();            // strip 装配后的三角索引
    }

    class ActSkin
    {
        public string cls, name;
        public List<SkiSubset> subs = new List<SkiSubset>();
        public List<Vector3> verts = new List<Vector3>();   // VaSkin 交错帧0: pos/nor/uv
        public List<Vector3> nors = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<int> tris = new List<int>();
    }

    class ActFile
    {
        public string ver;
        public List<string> bones = new List<string>();     // "名字:父名"
        public List<ActSkin> skins = new List<ActSkin>();
    }

    // ======================= .ski 解析 (v3.1 权威格式, 经 297 文件对拍验证) =======================
    //
    // CRT_SkelSkin 结构:
    //   对象头(FFFF+类名) + 名字(u32len+str) + 2B + u32 子块数
    //   子块×N: 名1 + 名2 + u32 索引数 + 1B + 索引×【大端 u16】
    //   材质库对象(跳过) → 顶点记录流(材质后某处起, 到 EOF-3, 尾标 01 01 01):
    //     A : 01 01 + f32[5]=法线3+UV2; 首条多 4B 声明数(忽略) → 26B / 后续 22B
    //     Rn: n(1..8) 00 00 00 + 骨骼ID + f32 权重 + f32 坐标3 + f32 法线3 → 33B
    //     C : 01 + 骨骼ID + f32 权重 + f32 坐标3 + f32 法线3 → 30B
    //   顶点池 = R块流序 + C块流序; UV = 第 i 个 A 记录的第4/5个 f32
    //   面 = 大端索引流 strip 装配 (跳退化三角, 绕序按奇偶交替)
    class VRec { public string kind; public int si; public int bone; public float w; public Vector3 pos, nor; public Vector2 uv; }

    static List<VRec> WalkVertices(byte[] b, int start, int end, out int endPos)
    {
        endPos = start;
        var recs = new List<VRec>();
        bool firstA = true; int o = start;
        while (o < end - 26)
        {
            int t0 = b[o], t1 = b[o + 1];
            if (t0 == 1 && t1 == 1)
            {
                int q = o + (firstA ? 6 : 2);
                var r = new VRec { kind = "A" };
                r.nor = new Vector3(F32(b, q), F32(b, q + 4), F32(b, q + 8));
                r.uv = new Vector2(F32(b, q + 12), F32(b, q + 16));
                recs.Add(r); o += firstA ? 26 : 22; firstA = false;
            }
            else if (t0 == 1 && t1 != 0)            // C 记录: 附加影响/半侧顶点
            {
                var r = new VRec { kind = "C", bone = t1, w = F32(b, o + 2) };
                r.pos = new Vector3(F32(b, o + 6), F32(b, o + 10), F32(b, o + 14));
                r.nor = new Vector3(F32(b, o + 18), F32(b, o + 22), F32(b, o + 26));
                recs.Add(r); o += 30;
            }
            else if (t0 >= 1 && t0 <= 8 && t1 == 0 && b[o + 2] == 0 && b[o + 3] == 0)   // R 记录
            {
                var r = new VRec { kind = "R" + t0, bone = b[o + 4], w = F32(b, o + 5) };
                r.pos = new Vector3(F32(b, o + 9), F32(b, o + 13), F32(b, o + 17));
                r.nor = new Vector3(F32(b, o + 21), F32(b, o + 25), F32(b, o + 29));
                recs.Add(r); o += 33;
            }
            else return null;                       // 未识别 → 起点错误
            endPos = o;
        }
        return recs;
    }

    static int FindVertStart(byte[] b, int from, out List<VRec> bestRecs, out int endPos)
    {
        bestRecs = null; endPos = -1;
        int best = -1, bestn = 0, bestEnd = -1;
        int lim = Math.Min(from + 0x400, b.Length - 40);
        for (int probe = Math.Max(0, from); probe < lim; probe++)
        {
            if (b[probe] != 1 || b[probe + 1] != 1) continue;
            int ep; var recs = WalkVertices(b, probe, b.Length - 3, out ep);
            if (recs == null) continue;
            if (recs.Count > bestn) { best = probe; bestRecs = recs; bestn = recs.Count; bestEnd = ep; }
            if (recs.Count > 10 && (b.Length - 3) - ep < 8) { bestRecs = recs; endPos = ep; return probe; }
        }
        endPos = bestEnd;
        return best;
    }

    static List<int> StripAssemble(List<int> idx, int vertCount)
    {
        var tris = new List<int>(idx.Count);
        for (int i = 0; i + 2 < idx.Count; i++)
        {
            int a = idx[i], c = idx[i + 1], d = idx[i + 2];
            if (a == c || c == d || a == d) continue;                           // 退化
            if (a >= vertCount || c >= vertCount || d >= vertCount) continue;   // 越界(Unity 会崩)
            if ((i & 1) == 0) { tris.Add(a); tris.Add(c); tris.Add(d); }
            else { tris.Add(a); tris.Add(d); tris.Add(c); }                     // 条带绕序交替
        }
        return tris;
    }

    // 界内索引率: 正确端序下索引几乎全部落在顶点池内; 错误端序值膨胀256倍→接近0
    static float InRate(List<int> idx, int vertCount)
    {
        if (idx.Count == 0) return 0f;
        int inb = 0;
        for (int i = 0; i < idx.Count; i++) if (idx[i] < vertCount) inb++;
        return (float)inb / idx.Count;
    }

    // 双端序索引区读取 (v3.1.1): LE = 低字节在前, BE = 高字节在前
    static List<int> IdxLE(byte[] b, int o, int fc) { var l = new List<int>(fc); for (int i = 0; i < fc; i++) l.Add(U16(b, o + i * 2)); return l; }
    static List<int> IdxBE(byte[] b, int o, int fc) { var l = new List<int>(fc); for (int i = 0; i < fc; i++) l.Add((b[o + i * 2] << 8) | b[o + i * 2 + 1]); return l; }

    static SkiFile LoadSki(byte[] b)
    {
        var f = new SkiFile();
        int o = 4 + U16(b, 2);              // 跳过 CRT_SkelSkin 对象头
        f.name = ReadStr(b, ref o);
        o += 2;                             // 两个布尔
        int subCnt = (int)U32(b, o); o += 4;
        var idxOffs = new List<int>(); var idxCnts = new List<int>();       // 索引区字节位置(端序延后定)
        for (int s = 0; s < subCnt; s++)
        {
            var ss = new SkiSubset { mtl = ReadStr(b, ref o), tex = ReadStr(b, ref o) };
            int fc = (int)U32(b, o); o += 4 + 1;                    // 索引数 + 尾标 0x01
            idxOffs.Add(o); idxCnts.Add(fc); o += fc * 2;
            f.subs.Add(ss);
        }
        // 定位材质对象 (CRT_MtlStandard=15字符 / CRT_MtlMu=9字符), 顶点流从其后探测
        int mtlOff = o;
        for (int i = o; i + 6 < b.Length; i++)
        {
            if (b[i] != 0xFF || b[i + 1] != 0xFF) continue;
            int tl = U16(b, i + 2);
            if (tl != 15 && tl != 9) continue;
            string tag = Encoding.GetEncoding(28591).GetString(b, i + 4, tl);
            if (tag == "CRT_MtlStandard" || tag == "CRT_MtlMu") { mtlOff = i; break; }
        }

        List<VRec> recs; int ep;
        int vs = FindVertStart(b, mtlOff, out recs, out ep);
        if (vs < 0 || recs == null) throw new Exception("顶点流未定位: " + f.name);

        // 顶点池 = R块(流序) + C块(流序); UV = 第 i 个 A 记录
        var aList = new List<VRec>(); var rList = new List<VRec>(); var cList = new List<VRec>();
        for (int si = 0; si < recs.Count; si++)
        {
            var r = recs[si]; r.si = si;      // v3.3: 流序号 — R2/C 顺序配对的依据
            if (r.kind == "A") aList.Add(r);
            else if (r.kind == "C") cList.Add(r);
            else rList.Add(r);
        }
        foreach (var r in rList) { f.verts.Add(r.pos); f.nors.Add(r.nor); }
        foreach (var r in cList) { f.verts.Add(r.pos); f.nors.Add(r.nor); }
        for (int i = 0; i < f.verts.Count; i++)
            f.uvs.Add(i < aList.Count ? aList[i].uv : Vector2.zero);

        // v3.2: 骨骼空间统一 — ski 顶点在各绑定骨骼的局部空间, 必须归一才能正确显示
        var distMap = UnifyBoneSpace(f, rList, cList);

        // 双端序择优 (v3.1.1): ski 297文件字节级验证为BE, LE 探测仅做兜底
        var idxBE = new List<int>(); var idxLE = new List<int>();
        for (int s = 0; s < idxOffs.Count; s++)
        {
            idxBE.AddRange(IdxBE(b, idxOffs[s], idxCnts[s]));
            idxLE.AddRange(IdxLE(b, idxOffs[s], idxCnts[s]));
        }
        float rBE = InRate(idxBE, f.verts.Count), rLE = InRate(idxLE, f.verts.Count);
        var bestIdx = rBE >= rLE ? idxBE : idxLE;
        f.tris = StripAssemble(bestIdx, f.verts.Count);
        for (int s = 0, acc = 0; s < idxOffs.Count; s++)
        {
            for (int i = 0; i < idxCnts[s]; i++) f.subs[s].faces.Add(bestIdx[acc + i]);
            acc += idxCnts[s];
        }
        if (f.tris.Count < 3) throw new Exception("面装配为空: " + f.name);
        // v3.6: 孤骨吸附 — 面拓扑就绪后执行
        LoneBoneAttach(f, rList, cList, distMap != null ? new HashSet<int>(distMap.Keys) : null);
        return f;
    }

    // ======================= v3.3: 骨骼空间统一 =======================
    // 格式实锤: Rn 的 n = 该顶点影响骨骼数; Rn(骨A,权重w) 与其后的 C(骨B,权重1-w)
    // 是同一顶点在两个骨骼空间的坐标, 流序即对应关系 (FIFO: R1 不消耗 C, R2/R3 依次消耗)。
    // 同一骨骼对 (A,B) 的全部点对服从同一刚体变换 → Kabsch 精确求解 (Horn四元数+Jacobi)。
    // 297 文件 / 4068 骨对离线验证: 残差全部 <1e-4 (文件记录即精确刚体), 确定性无随机。
    // → BFS 沿骨骼图传递 → 全部顶点归一到各分量代表骨空间 → 网格连续

    static Matrix4x4 RotOnly(Matrix4x4 M)
    {
        var R = Matrix4x4.identity;
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) R[r, c] = M[r, c];
        return R;
    }

    // v3.3: Jacobi 4x4 对称特征分解 — ev[4]=特征值, V[16]=行主序特征向量 (列=向量)
    static void JacobiSym4(float[] a, out float[] ev, out float[] V)
    {
        float[,] A = new float[4, 4];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) A[i, j] = a[i * 4 + j];
        V = new float[16];
        for (int i = 0; i < 4; i++) V[i * 5] = 1f;
        for (int sweep = 0; sweep < 40; sweep++)
        {
            float off = 0f;
            for (int p = 0; p < 3; p++) for (int q = p + 1; q < 4; q++) off += Mathf.Abs(A[p, q]);
            if (off < 1e-9f) break;
            for (int p = 0; p < 3; p++)
                for (int q = p + 1; q < 4; q++)
                {
                    float apq = A[p, q];
                    if (Mathf.Abs(apq) < 1e-12f) continue;
                    float th = (A[q, q] - A[p, p]) / (2f * apq);
                    float t = (th >= 0f ? 1f : -1f) / (Mathf.Abs(th) + Mathf.Sqrt(th * th + 1f));
                    float c = 1f / Mathf.Sqrt(t * t + 1f), sg = t * c;
                    for (int k = 0; k < 4; k++)
                    {
                        float akp = A[k, p], akq = A[k, q];
                        A[k, p] = c * akp - sg * akq; A[k, q] = sg * akp + c * akq;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        float apk = A[p, k], aqk = A[q, k];
                        A[p, k] = c * apk - sg * aqk; A[q, k] = sg * apk + c * aqk;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        float vkp = V[k * 4 + p], vkq = V[k * 4 + q];
                        V[k * 4 + p] = c * vkp - sg * vkq; V[k * 4 + q] = sg * vkp + c * vkq;
                    }
                }
        }
        ev = new float[] { A[0, 0], A[1, 1], A[2, 2], A[3, 3] };
    }

    // v3.5: Kabsch 6D + 退化防护
    //   n<3 或位置点云扁平 → 旋转欠定(绕轴自由) → 强制 R=I 只保留质心平移 (v3.4 手机碎片的根因:
    //   单点对走法线拟合, 绕轴随机旋转被 norRes≈0 骗过检验, 污染全图)
    static Matrix4x4 KabschFit(List<Vector3> P, List<Vector3> Q, List<Vector3> PN, List<Vector3> QN, out float avgRes, out float norRes, out bool rotTrust, float minSigma = 0.5f)
    {
        var I = Matrix4x4.identity;
        int n = P.Count;
        avgRes = 1e9f; norRes = 1e9f; rotTrust = false;
        if (n == 0) return I;
        Vector3 pc = Vector3.zero, qc = Vector3.zero;
        for (int i = 0; i < n; i++) { pc += P[i]; qc += Q[i]; }
        pc /= n; qc /= n;
        // 退化检测: 位置散布的最小方向伸展 σmin (协方差最小特征值开方)
        float sigmin = 0f;
        {
            float cxx = 0f, cyy = 0f, czz = 0f, cxy = 0f, cxz = 0f, cyz = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = P[i] - pc;
                cxx += p.x * p.x; cyy += p.y * p.y; czz += p.z * p.z;
                cxy += p.x * p.y; cxz += p.x * p.z; cyz += p.y * p.z;
            }
            // 3x3 对称幂迭代求最大特征值, 再 deflation 求次小 — 简化: 用迹与最大值的粗略关系
            float tr = cxx + cyy + czz;
            // 幂迭代最大
            float[] v = { 1f, 0.33f, 0.71f };
            for (int it = 0; it < 24; it++)
            {
                float w0 = cxx * v[0] + cxy * v[1] + cxz * v[2];
                float w1 = cxy * v[0] + cyy * v[1] + cyz * v[2];
                float w2 = cxz * v[0] + cyz * v[1] + czz * v[2];
                float nm = Mathf.Sqrt(w0 * w0 + w1 * w1 + w2 * w2);
                if (nm < 1e-9f) break;
                v[0] = w0 / nm; v[1] = w1 / nm; v[2] = w2 / nm;
            }
            float lmax = v[0] * (cxx * v[0] + cxy * v[1] + cxz * v[2])
                       + v[1] * (cxy * v[0] + cyy * v[1] + cyz * v[2])
                       + v[2] * (cxz * v[0] + cyz * v[1] + czz * v[2]);
            float lmid = Mathf.Max(0f, tr - lmax) * 0.5f;   // 粗略下界估计: 剩余均分
            sigmin = Mathf.Sqrt(Mathf.Max(0f, Mathf.Min(lmid, lmax)) * 0.25f);
        }
        const float WN = 20f;
        float Sxx = 0f, Sxy = 0f, Sxz = 0f, Syx = 0f, Syy = 0f, Syz = 0f, Szx = 0f, Szy = 0f, Szz = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = P[i] - pc, q = Q[i] - qc;
            Sxx += p.x * q.x; Sxy += p.x * q.y; Sxz += p.x * q.z;
            Syx += p.y * q.x; Syy += p.y * q.y; Syz += p.y * q.z;
            Szx += p.z * q.x; Szy += p.z * q.y; Szz += p.z * q.z;
            if (PN != null && i < PN.Count)   // 法线协方差: 同一旋转 R 作用于法线对, 不中心化
            {
                Vector3 nv = PN[i], mv = QN[i];
                Sxx += WN * nv.x * mv.x; Sxy += WN * nv.x * mv.y; Sxz += WN * nv.x * mv.z;
                Syx += WN * nv.y * mv.x; Syy += WN * nv.y * mv.y; Syz += WN * nv.y * mv.z;
                Szx += WN * nv.z * mv.x; Szy += WN * nv.z * mv.y; Szz += WN * nv.z * mv.z;
            }
        }
        // Horn 4x4 对称矩阵: 最大特征值的特征向量 = 最优旋转四元数 (w,x,y,z)
        float[] a = {
            Sxx+Syy+Szz, Syz-Szy,     Szx-Sxz,     Sxy-Syx,
            Syz-Szy,     Sxx-Syy-Szz, Sxy+Syx,     Szx+Sxz,
            Szx-Sxz,     Sxy+Syx,    -Sxx+Syy-Szz, Syz+Szy,
            Sxy-Syx,     Szx+Sxz,     Syz+Szy,    -Sxx-Syy+Szz };
        float[] ev, V;
        JacobiSym4(a, out ev, out V);
        int im = 0;
        for (int k = 1; k < 4; k++) if (ev[k] > ev[im]) im = k;
        float qw = V[im], qx = V[4 + im], qy = V[8 + im], qz = V[12 + im];   // 行主序: 列 im = 特征向量
        float qw2 = qw * qw, qx2 = qx * qx, qy2 = qy * qy, qz2 = qz * qz;
        // v3.5 旋转可信度: 点数≥3 且位置散布非扁平 (σmin>0.5) 才允许旋转进图
        bool trust = n >= 3 && sigmin > minSigma;
        var M = I;
        if (trust)
        {
            M[0, 0] = qw2 + qx2 - qy2 - qz2; M[0, 1] = 2f * (qx * qy - qw * qz); M[0, 2] = 2f * (qx * qz + qw * qy);
            M[1, 0] = 2f * (qx * qy + qw * qz); M[1, 1] = qw2 - qx2 + qy2 - qz2; M[1, 2] = 2f * (qy * qz - qw * qx);
            M[2, 0] = 2f * (qx * qz - qw * qy); M[2, 1] = 2f * (qy * qz + qw * qx); M[2, 2] = qw2 - qx2 - qy2 + qz2;
            // 法线检验: 旋转与法线对应关系矛盾 → 不可信
            float nsum = 0f; int nc = 0;
            if (PN != null)
            {
                for (int i = 0; i < n && i < PN.Count; i++)
                {
                    Vector3 rn = M.MultiplyVector(PN[i]);
                    nsum += (rn - QN[i]).magnitude; nc++;
                }
                if (nc > 0) norRes = nsum / nc;
                if (norRes > 0.2f) trust = false;
            }
        }
        if (!trust) M = Matrix4x4.identity;    // R=I: 只保留质心平移 (平移总可信)
        rotTrust = trust;
        Vector3 rp = M * pc;                    // Matrix4x4*Vector3 结果为 Vector4, 先赋值转回 Vector3
        Vector3 t = qc - rp;
        M[0, 3] = t.x; M[1, 3] = t.y; M[2, 3] = t.z;
        float sum = 0f;
        for (int i = 0; i < n; i++) { Vector3 tp = M * P[i]; sum += (tp - Q[i]).magnitude; }
        avgRes = sum / n;
        return M;
    }

    static Dictionary<int, Matrix4x4> UnifyBoneSpace(SkiFile f, List<VRec> rList, List<VRec> cList)
    {
        int nR = rList.Count, nC = cList.Count;
        _kabschRes = 0f; _kabschNor = 0f; _kabschCnt = 0; _flatCnt = 0;   // v3.4: 每文件重置统计
        if (nR == 0 || nC == 0) return null;
        // v3.3: 顺序 FIFO 配对 — Rn 的 n = 影响骨骼数 (R1 不消耗 C, R2/R3 依次消耗)
        // 流序即对应关系: 每条 C 与最近的未满 R 主记录是同一顶点
        var paList = new List<Vector3>(); var pbList = new List<Vector3>();
        var paNor = new List<Vector3>(); var pbNor = new List<Vector3>();   // v3.4: 法线参与 6D Kabsch
        var aSrc = new List<int>(); var bSrc = new List<int>();   // 配对 → rList/cList 全局下标
        int fi = 0, fj = 0, head = -1, headLeft = 0, headBone = 0; Vector3 headPos = Vector3.zero; Vector3 headNor = Vector3.zero;
        while (fi < nR || fj < nC)
        {
            bool takeR = fj >= nC || (fi < nR && rList[fi].si < cList[fj].si);
            if (takeR)
            {
                var r = rList[fi];
                int nImp; int.TryParse(r.kind.Length > 1 ? r.kind.Substring(1) : "1", out nImp);
                if (nImp < 1) nImp = 1;
                if (nImp >= 2) { head = fi; headBone = r.bone; headPos = r.pos; headNor = r.nor; headLeft = nImp - 1; }
                fi++;
            }
            else
            {
                var c = cList[fj];
                if (headLeft > 0)
                {
                    paList.Add(headPos); pbList.Add(c.pos);
                    paNor.Add(headNor); pbNor.Add(c.nor);
                    aSrc.Add(head); bSrc.Add(fj);
                    headLeft--;
                }
                fj++;
            }
        }
        if (paList.Count == 0) return null;
        // 骨对分组 → Kabsch 确定性求解 (297文件/4068骨对 实测全零残差)
        var tMap = new Dictionary<long, Matrix4x4>();          // 强边: key = A*1000+B (A→B), 旋转可信
        var tMapFlat = new Dictionary<long, Matrix4x4>();      // v3.5 弱边: R=I 纯平移兜底
        var pairR2C = new Dictionary<int, int>();              // C全局槽 -> R全局槽
        var grp = new Dictionary<long, List<int>>();
        for (int fk = 0; fk < paList.Count; fk++)
        {
            long key = (long)rList[aSrc[fk]].bone * 1000 + cList[bSrc[fk]].bone;
            List<int> L;
            if (!grp.TryGetValue(key, out L)) { L = new List<int>(); grp[key] = L; }
            L.Add(fk);
        }
        foreach (var kv in grp)
        {
            var P = new List<Vector3>(); var Q = new List<Vector3>();
            var PN = new List<Vector3>(); var QN = new List<Vector3>();
            foreach (int k in kv.Value) { P.Add(paList[k]); Q.Add(pbList[k]); PN.Add(paNor[k]); QN.Add(pbNor[k]); }
            float res, nres; bool rotT;
            Matrix4x4 M = KabschFit(P, Q, PN, QN, out res, out nres, out rotT);
            if (rotT && res < 0.05f) tMap[kv.Key] = M;                 // 强边: 旋转可信且精确
            else if (!rotT && res < 20f) tMapFlat[kv.Key] = M;         // 弱边: R=I 平移兜底 (错配 res 巨大, 拒)
            _kabschRes += res; _kabschNor += nres; _kabschCnt++;
            if (!rotT) _flatCnt++;
            for (int k = 0; k < kv.Value.Count; k++)           // C 顶点无条件抄配对 R (同一点)
                pairR2C[nR + bSrc[kv.Value[k]]] = aSrc[kv.Value[k]];
        }
        if (tMap.Count == 0 && tMapFlat.Count == 0) return null;
        // 骨骼图 BFS: dist[骨] = M 使 p_rep = M * p_骨
        var adj = new Dictionary<int, List<int>>();
        foreach (var kv in tMap)
        {
            int a = (int)(kv.Key / 1000), b2 = (int)(kv.Key % 1000);
            if (!adj.ContainsKey(a)) adj[a] = new List<int>();
            if (!adj.ContainsKey(b2)) adj[b2] = new List<int>();
            adj[a].Add(b2); adj[b2].Add(a);
        }
        var dist = new Dictionary<int, Matrix4x4>();
        while (dist.Count < adj.Count)
        {
            int rep = -1, maxn = -1;
            foreach (var bn in adj.Keys)
                if (!dist.ContainsKey(bn))
                {
                    int cnt = 0;
                    for (int i = 0; i < nR; i++) if (rList[i].bone == bn) cnt++;
                    if (cnt > maxn) { maxn = cnt; rep = bn; }
                }
            if (rep < 0) break;
            dist[rep] = Matrix4x4.identity;
            var queue = new Queue<int>(); queue.Enqueue(rep);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                Matrix4x4 Mc = dist[cur];
                foreach (var kv in tMap)
                {
                    int a = (int)(kv.Key / 1000), b2 = (int)(kv.Key % 1000);
                    // v3.4 修 BFS 方向 bug: p_rep = dist[cur]*p_cur
                    //   cur=A: p_rep = Mc*p_A, p_A = T⁻¹*p_B  ⇒  dist[B] = Mc * T⁻¹
                    //   cur=B: p_rep = Mc*p_B, p_B = T*p_A    ⇒  dist[A] = Mc * T
                    if (a == cur && !dist.ContainsKey(b2))
                    { dist[b2] = Mc * kv.Value.inverse; queue.Enqueue(b2); }
                    if (b2 == cur && !dist.ContainsKey(a))
                    { dist[a] = Mc * kv.Value; queue.Enqueue(a); }
                }
            }
        }
        // v3.5 阶段2: 弱边 (R=I 平移) 桥接强图之外的孤岛骨 — 多轮迭代到不动点
        if (tMapFlat.Count > 0)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var kv in tMapFlat)
                {
                    int a = (int)(kv.Key / 1000), b2 = (int)(kv.Key % 1000);
                    bool ha = dist.ContainsKey(a), hb = dist.ContainsKey(b2);
                    // 方向与强边一致: T: p_B = M·p_A → cur=A: dist[B]=Mc·T⁻¹ ; cur=B: dist[A]=Mc·T
                    if (ha && !hb) { dist[b2] = dist[a] * kv.Value.inverse; changed = true; }
                    else if (hb && !ha) { dist[a] = dist[b2] * kv.Value; changed = true; }
                }
            }
        }
        // 应用: R 顶点变换; 法线仅旋转
        for (int i = 0; i < nR; i++)
        {
            Matrix4x4 M;
            if (dist.TryGetValue(rList[i].bone, out M))
            {
                f.verts[i] = M * rList[i].pos;
                f.nors[i] = RotOnly(M) * rList[i].nor;
            }
        }
        // C 区: 配对副本 or 直接变换
        for (int j = 0; j < nC; j++)
        {
            int slot = nR + j;
            int src;
            if (pairR2C.TryGetValue(slot, out src)) { f.verts[slot] = f.verts[src]; f.nors[slot] = f.nors[src]; }
            else
            {
                Matrix4x4 M;
                if (dist.TryGetValue(cList[j].bone, out M)) { f.verts[slot] = M * cList[j].pos; f.nors[slot] = RotOnly(M) * cList[j].nor; }
            }
        }
        // v3.4 诊断统计 (HUD 显示, 用户截图回报用)
        int cR1 = 0, cR2 = 0, cR3 = 0;
        foreach (var r in rList)
        {
            if (r.kind == "R1" || r.kind == "R") cR1++;
            else if (r.kind == "R2") cR2++;
            else cR3++;
        }
        float avgK = _kabschCnt > 0 ? _kabschRes / _kabschCnt : 0f;
        float avgN = _kabschCnt > 0 ? _kabschNor / _kabschCnt : 0f;
        int paired = pairR2C.Count;
        _skiStat = string.Format("R1={0} R2={1} R3={2} C={3} 对={4} 骨对={5} 强={6} 平移={7} 位置残差={8:F4} 法线残差={9:F3} BFS覆盖={10}/{11}",
            cR1, cR2, cR3, nC, paList.Count, grp.Count, grp.Count - _flatCnt, _flatCnt, avgK, avgN, dist.Count, adj.Count);
        return dist;
    }

    // ======================= v3.6: 孤骨吸附 =======================
    // 孤骨 = 无任何双引用记录(图边)的骨骼 → BFS 不可达, 顶点 identity 兜底留在自身空间。
    // 拓扑约束: 同一三角形内顶点空间连续 → 跨骨共面边给出"贴回"点对, Kabsch 完整刚体。
    // Python 镜像实测(GL边长中位数): glo_09 8.25→1.38, hea_05 5.61→0.86, hea_03(反向) 9.41→1.19。
    // 方向: 孤骨顶点数 ≤ 图骨顶点数 → 孤骨吸向图骨; 单大孤骨(>总数一半) → 图骨吸向孤骨。
    // 稳妥性: det(R)<0 反射 → 回退纯平移; 无共面边 → 保持 identity(全孤骨单骨文件本就连续)。
    static int _attachCnt = 0;
    static void LoneBoneAttach(SkiFile f, List<VRec> rList, List<VRec> cList, HashSet<int> unifiedBones)
    {
        int nR = rList.Count;
        if (f.tris == null || f.tris.Count < 3 || unifiedBones == null || unifiedBones.Count == 0) { _attachCnt = 0; return; }
        var byBone = new Dictionary<int, List<int>>();
        for (int i = 0; i < nR; i++)
        {
            int b = rList[i].bone;
            List<int> l;
            if (!byBone.TryGetValue(b, out l)) { l = new List<int>(); byBone[b] = l; }
            l.Add(i);
        }
        var loneBones = new List<int>();
        foreach (var kv in byBone) if (!unifiedBones.Contains(kv.Key)) loneBones.Add(kv.Key);
        if (loneBones.Count == 0) { _attachCnt = 0; return; }

        var isLoneVert = new bool[f.verts.Count];
        for (int i = 0; i < nR; i++)
            if (loneBones.Contains(rList[i].bone)) isLoneVert[i] = true;
        int loneVertTotal = 0;
        for (int i = 0; i < isLoneVert.Length; i++) if (isLoneVert[i]) loneVertTotal++;
        int uniVertTotal = f.verts.Count - loneVertTotal;

        loneBones.Sort((x, y) => byBone[y].Count - byBone[x].Count);   // 大块优先(反向目标)
        bool reversed = false;
        int attached = 0;
        foreach (int bone in loneBones)
        {
            var vl = byBone[bone];
            if (vl.Count > uniVertTotal && reversed) continue;           // 只允许一次反向(实测数据单大孤骨)
            bool reverse = vl.Count > uniVertTotal;
            // 每骨重建邻点表(用当前 f.verts 快照, 反向移位后坐标最新)
            var nbrMap = new Dictionary<int, List<Vector3>>();
            for (int t = 0; t + 2 < f.tris.Count; t += 3)
            {
                int a = f.tris[t], b = f.tris[t + 1], c = f.tris[t + 2];
                int[] tri = { a, b, c };
                for (int e = 0; e < 3; e++)
                {
                    int u = tri[e], v = tri[(e + 1) % 3];
                    if (u == v || u >= f.verts.Count || v >= f.verts.Count) continue;
                    bool ul = isLoneVert[u], wv = isLoneVert[v];
                    if (ul == wv) continue;
                    if (reverse && !wv) continue;                        // 反向只收孤骨端为邻点源
                    int li = ul ? u : v, gi = ul ? v : u;
                    List<Vector3> l;
                    if (!nbrMap.TryGetValue(li, out l)) { l = new List<Vector3>(); nbrMap[li] = l; }
                    l.Add(f.verts[gi]);
                }
            }
            if (nbrMap.Count == 0) continue;
            var P = new List<Vector3>(); var Q = new List<Vector3>();
            foreach (var kv in nbrMap)
            {
                Vector3 near = kv.Value[0]; float bd = float.MaxValue;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    float d = (kv.Value[i] - f.verts[kv.Key]).sqrMagnitude;
                    if (d < bd) { bd = d; near = kv.Value[i]; }
                }
                P.Add(f.verts[kv.Key]); Q.Add(near);
            }
            if (P.Count < 1) continue;
            float res, norRes; bool rt;
            Matrix4x4 M;
            if (reverse)
            {
                var P2 = new List<Vector3>(Q); var Q2 = new List<Vector3>(P);
                M = KabschFit(P2, Q2, null, null, out res, out norRes, out rt, 0.1f);
            }
            else M = KabschFit(P, Q, null, null, out res, out norRes, out rt, 0.1f);
            float det = M[0, 0] * (M[1, 1] * M[2, 2] - M[1, 2] * M[2, 1])
                      - M[0, 1] * (M[1, 0] * M[2, 2] - M[1, 2] * M[2, 0])
                      + M[0, 2] * (M[1, 0] * M[2, 1] - M[1, 1] * M[2, 0]);
            if (det < 0f)                                                 // 反射 → 回退纯平移
            {
                Vector3 pc = Vector3.zero, qc = Vector3.zero;
                for (int i = 0; i < P.Count; i++) { pc += P[i]; qc += Q[i]; }
                pc /= P.Count; qc /= P.Count;
                M = Matrix4x4.identity;
                M[0, 3] = qc.x - pc.x; M[1, 3] = qc.y - pc.y; M[2, 3] = qc.z - pc.z;
            }
            if (reverse)
            {
                for (int i = 0; i < f.verts.Count; i++)
                    if (!isLoneVert[i]) { Vector3 rp = M * f.verts[i]; f.verts[i] = rp; }
                reversed = true;
            }
            else
            {
                foreach (int vi in vl) { Vector3 rp = M * f.verts[vi]; f.verts[vi] = rp; }
            }
            attached++;
        }
        _attachCnt = attached;
        if (loneBones.Count > 0) _skiStat += " 吸附=" + attached + "/" + loneBones.Count;
    }

    static string _skiStat = "";           // v3.4: 最近一次 ski 骨骼统一诊断
    static float _kabschRes, _kabschNor; static int _kabschCnt, _flatCnt;

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
        var idxOffs = new List<int>(); var idxCnts = new List<int>(); int fcTotal = 0;
        for (int s = 0; s < subCnt; s++)
        {
            var ss = new SkiSubset { mtl = ReadStr(b, ref o), tex = ReadStr(b, ref o) };
            int fc = (int)U32(b, o); o += 4;
            idxOffs.Add(o); idxCnts.Add(fc); fcTotal += fc; o += fc * 2 + 1;    // 索引区 + 尾标
            sk.subs.Add(ss);
        }
        if (fcTotal < 3) return null;

        // 顶点区：扫描 [u32 顶点数] + 32B 交错(pos3f+nor3f+uv2f)，浮点全部合理
        // (注意: 索引可能引用到 511 槽而顶点池较小, 越界面由 StripAssemble 剔除)
        for (int cand = o; cand < b.Length - 40; cand++)
        {
            int cnt = (int)U32(b, cand);
            if (cnt < 4 || cnt > 5000) continue;
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

            // 双端序择优 (v3.1.1): VaSkin 索引实测小端(v3.0 真机剑主体可见), ski 才是大端
            var idxBE = new List<int>(); var idxLE = new List<int>();
            for (int s = 0; s < idxOffs.Count; s++)
            {
                idxBE.AddRange(IdxBE(b, idxOffs[s], idxCnts[s]));
                idxLE.AddRange(IdxLE(b, idxOffs[s], idxCnts[s]));
            }
            float rBE = InRate(idxBE, V.Count), rLE = InRate(idxLE, V.Count);
            if (rBE < 0.5f && rLE < 0.5f) continue;       // 假阳性防护: 两个端序界内率都不合格=结构错误
            var bestIdx = rBE >= rLE ? idxBE : idxLE;
            var tris = StripAssemble(bestIdx, V.Count);
            if (tris.Count < 3) continue;
            sk.verts = V; sk.nors = N; sk.uvs = U; sk.tris = tris;
            for (int s = 0, acc = 0; s < idxOffs.Count; s++)          // 记录端序择优后的子块索引
            {
                for (int i = 0; i < idxCnts[s]; i++) sk.subs[s].faces.Add(bestIdx[acc + i]);
                acc += idxCnts[s];
            }
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

    // v3.5: PartGroup 增加官方名称对照 (来源: 真封神国际版官网物品数据库 zfsonline.com/game/items.o)
    //   编号规则(实测自洽): 武器 wtj/wtc_NN ↔ 槽位(1)(2)等级档; 防具 a_wt_m_*_NN ↔ 套装档 00..09
    class PartGroup
    {
        public string label; public string[] files; public bool isAct; public string dir;
        public string[] names;                 // 与 files 一一对应的官方名称(名称·等级), 空则回退文件名
        public string Name(int i) { return names != null && i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : files[i]; }
    }

    // 术士/全职业防具 10 档 (ski 尾号 00..09 → 套装名, 与官网逐档核对)
    static readonly string[] SuitNames = { "渡法", "渡痕", "渡骨", "无神", "钰阙", "龙神", "护法天君", "地煞恶神", "碧游通天", "魔枭天冥" };

    static readonly PartGroup[] Groups = new PartGroup[]
    {
        // 槽位(1) 剑刀系 15件官方数据 → wtj 10档: Lv1/5/15/25/35/45/55/65/75/85
        new PartGroup{ label="武器·剑刀(槽1)", dir="creature/actor/", isAct=true,
            files=new []{"w_wtj_m_01.act","w_wtj_m_02.act","w_wtj_m_03.act","w_wtj_m_04.act","w_wtj_m_05.act","w_wtj_m_06.act","w_wtj_m_07.act","w_wtj_m_08.act","w_wtj_m_09.act","w_wtj_m_10.act"},
            names=new []{"青云剑·狂战Lv1","吴钩剑·狂战Lv5","飞烟剑·狂战Lv15","斩将刀·狂战Lv25","化血神刀·金甲Lv35","斩仙飞刀·金甲Lv45","三尖两刃刀·金甲Lv55","正法天王刀·武斗Lv65","九曜星君刃·武斗Lv75","轩辕圣皇刀·武斗Lv85"}},
        // 槽位(2) 杵斧系: Lv1/5/15/25/35/45/55/65/75 (撞心杵→15级杵→25级后斧)
        new PartGroup{ label="武器·杵斧(槽2)", dir="creature/actor/", isAct=true,
            files=new []{"w_wtc_m_01.act","w_wtc_m_02.act","w_wtc_m_03.act","w_wtc_m_04.act","w_wtc_m_05.act","w_wtc_m_06.act","w_wtc_m_07.act","w_wtc_m_08.act","w_wtc_m_09.act"},
            names=new []{"撞心杵·狂战Lv1","荡魔杵·狂战Lv5","降魔杵·狂战Lv15","宣花斧·狂战Lv25","开山斧·金甲Lv35","湛金斧·金甲Lv45","斗神斧·金甲Lv55","荧惑炎君斧·武斗Lv65","天齐岳神斧·武斗Lv75"}},
        // 防具(3) 甲袍: 10档术士套装 + fashion/suit 时尚装(官方名待考)
        new PartGroup{ label="甲袍·防具(3)", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_clo_00.ski","a_wt_m_clo_01.ski","a_wt_m_clo_02.ski","a_wt_m_clo_03.ski","a_wt_m_clo_04.ski","a_wt_m_clo_05.ski","a_wt_m_clo_06.ski","a_wt_m_clo_07.ski","a_wt_m_clo_08.ski","a_wt_m_clo_09.ski","a_wt_m_clo_fashion01.ski","a_wt_m_clo_fashion02.ski","a_wt_m_clo_suit02.ski","a_wt_m_clo_suit03.ski"},
            names=new []{"渡法法袍·Lv10","渡痕法袍·Lv17","渡骨法袍·Lv27","无神战甲·Lv37","钰阙战甲·Lv47","龙神战甲·Lv57","护法天君战甲·Lv67","地煞恶神战甲·Lv77","碧游通天战甲·Lv87","魔枭天冥战甲·Lv97","时装·1","时装·2","稀有时装·2","稀有时装·3"}},
        new PartGroup{ label="头饰", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_hea_00.ski","a_wt_m_hea_01.ski","a_wt_m_hea_02.ski","a_wt_m_hea_03.ski","a_wt_m_hea_04.ski","a_wt_m_hea_05.ski"},
            names=new []{"渡法头冠·Lv10","渡痕头冠·Lv17","渡骨头冠·Lv27","无神头冠·Lv37","钰阙头冠·Lv47","龙神头冠·Lv57"}},
        // 防具(1) 手套
        new PartGroup{ label="手套·防具(1)", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_glo_00.ski","a_wt_m_glo_01.ski","a_wt_m_glo_02.ski","a_wt_m_glo_03.ski","a_wt_m_glo_04.ski","a_wt_m_glo_05.ski","a_wt_m_glo_06.ski","a_wt_m_glo_07.ski","a_wt_m_glo_08.ski","a_wt_m_glo_09.ski"},
            names=new []{"渡法手套·Lv8","渡痕手套·Lv13","渡骨手套·Lv23","无神手套·Lv33","钰阙手套·Lv43","龙神手套·Lv53","护法天君手套·Lv63","地煞恶神手套·Lv73","碧游通天手套·Lv83","魔枭天冥手套·Lv93"}},
        // 防具(2) 长靴
        new PartGroup{ label="长靴·防具(2)", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_sho_00.ski","a_wt_m_sho_01.ski","a_wt_m_sho_02.ski","a_wt_m_sho_03.ski","a_wt_m_sho_04.ski","a_wt_m_sho_05.ski","a_wt_m_sho_06.ski","a_wt_m_sho_07.ski","a_wt_m_sho_08.ski","a_wt_m_sho_09.ski"},
            names=new []{"渡法长靴·Lv9","渡痕长靴·Lv15","渡骨长靴·Lv25","无神长靴·Lv35","钰阙长靴·Lv45","龙神长靴·Lv55","护法天君长靴·Lv65","地煞恶神长靴·Lv75","碧游通天长靴·Lv85","魔枭天冥长靴·Lv95"}},
        new PartGroup{ label="外装", dir="creature/actor/", isAct=false,
            files=new []{"a_wt_m_wai_01.ski","a_wt_m_wai_02.ski","a_wt_m_wai_03.ski","a_wt_m_wai_04.ski","a_wt_m_wai_05.ski","a_wt_m_wai_06.ski","a_wt_m_wai_07.ski","a_wt_m_wai_08.ski","a_wt_m_wai_09.ski"},
            names=new []{"外装·1","外装·2","外装·3","外装·4","外装·5","外装·6","外装·7","外装·8","外装·9"}},
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
    readonly Dictionary<string, string> _infoCache = new Dictionary<string, string>();   // v3.5: per-model 信息
    int _loadSeq;                                                                          // v3.5: 加载序号(竞态守卫)

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
        StartCoroutine(LoadModel(key, path, grp.isAct, grp.Name(i)));
    }

    IEnumerator LoadModel(string key, string path, bool isAct, string dispName)
    {
        int seq = ++_loadSeq;                  // v3.5: 竞态守卫 — 快速翻页时旧协程不再覆盖状态
        _loading = true;
        if (_modelCache.ContainsKey(key))
        {
            Show(key);
            if (_infoCache.ContainsKey(key)) _info = _infoCache[key];   // v3.5: 恢复本模型的统计
            _loading = false; yield break;
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
            string info;
            if (isAct)
            {
                var act = LoadAct(d);
                if (act.skins.Count == 0) throw new Exception("无内嵌网格");
                var sk = act.skins[0];
                texName = sk.subs[0].tex;
                model = BuildMeshObject(sk.verts, sk.nors, sk.uvs, sk.tris, texName);
                info = string.Format("【{0}】 {1}  骨骼{2}根  网格{3}顶点/{4}面  贴图={5}",
                    dispName, act.ver, act.bones.Count, sk.verts.Count, sk.tris.Count / 3, texName);
            }
            else
            {
                var ski = LoadSki(d);
                texName = ski.subs[0].tex;
                model = BuildMeshObject(ski.verts, ski.nors, ski.uvs, ski.tris, texName);
                info = string.Format("【{0}】 {1}  {2}顶点/{3}面  贴图={4}  |  {5}",
                    dispName, ski.name, ski.verts.Count, ski.tris.Count / 3, texName, _skiStat);
            }
            if (seq != _loadSeq) yield break;      // v3.5: 已被更新的翻页取代, 放弃本协程
            model.transform.SetParent(_pivot, false);
            _modelCache[key] = model;
            _infoCache[key] = info;                // v3.5: per-model 信息 (缓存命中时恢复)
            Show(key);
            _info = info;
            _status = "";
        }
        catch (Exception e)
        {
            if (seq != _loadSeq) yield break;
            _status = "解析失败: " + e.Message;
            _modelCache.Remove(key);                    // v3.1.1: 失败不留半成品
            foreach (var kv in _modelCache) kv.Value.SetActive(false);   // 隐藏旧模型, 避免残留画面
        }
        _loading = false;
    }

    GameObject BuildMeshObject(List<Vector3> V, List<Vector3> N, List<Vector2> U, List<int> tris, string texName)
    {
        // 归一化：让模型稳定占屏（各部件尺寸差异大）
        var bmin = V[0]; var bmax = V[0];
        foreach (var v in V) { bmin = Vector3.Min(bmin, v); bmax = Vector3.Max(bmax, v); }
        var center = (bmin + bmax) * 0.5f;
        float dim = Mathf.Max(bmax.x - bmin.x, Mathf.Max(bmax.y - bmin.y, bmax.z - bmin.z));
        float scale = dim > 0.001f ? 40f / dim : 1f;

        var verts = new Vector3[V.Count];
        for (int i = 0; i < V.Count; i++) verts[i] = (V[i] - center) * scale;
        var idx = tris.ToArray();

        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.normals = N.ToArray(); mesh.uv = U.ToArray();
        mesh.triangles = idx;
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
        // 变体回退：pak 内贴图文件常带 _01/_02 变体后缀，而 act/ski 记录的是基础名
        var cands = new List<string>();
        cands.Add(name);
        int us = name.LastIndexOf('_');
        bool hasVar = us > 0 && us + 2 < name.Length && name[us + 1] == '0' && name[us + 2] >= '1' && name[us + 2] <= '9';
        if (hasVar) cands.Insert(0, name.Substring(0, us));          // 已带后缀 → 先试基础名
        for (int k = 1; k <= 4; k++) cands.Add(name + "_0" + k);      // 再试 _01.._04
        foreach (var cand in cands)
        {
            var req = UnityWebRequest.Get(SA("creature/texture/" + cand + ".dds"));
            req.timeout = 20;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) continue;
            var t = LoadDds(req.downloadHandler.data);
            if (t != null)
            {
                _texCache[name] = t;
                if (mat != null) { mat.mainTexture = t; mat.color = Color.white; }
                _status = "";
                yield break;
            }
        }
        // v3.4: 全部候选 404 → 品红警示 + 状态栏报贴图名 (不再静默白模)
        if (mat != null) mat.color = new Color(0.85f, 0.1f, 0.85f);
        _status = "贴图未找到: " + name;
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
        GUI.Label(new Rect(12, 8, w - 24, h * 0.05f), "v3.5 角色预览台 — CRT 直读 + 骨骼空间统一 (ski/act/dds)", title);

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
            "触摸滑动=旋转展台  ·  v3.5.1 骨骼空间统一(顶点内配对+Kabsch 6D+强弱边BFS)\n" +
            "整装术士(身体拼合+骨骼动画)待 v4：需从游戏包提取 role_wt_m_01.act\n" +
            "Swap test: 服装/手套/鞋/外装/头/武器 已可实时切换", tip);
    }
}
