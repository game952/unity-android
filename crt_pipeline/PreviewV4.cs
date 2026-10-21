// PreviewV4.cs — v4.8.1d 整装拼合预览台 (2026-10-13 晚热修)
// ============================================================
// v4.8.9 ACT蒙皮地基 + 顶点池定位 + 贴图跨包反查 (2026-10-21 深夜, 依据装机11图+5样本逆向):
//  ① P0 皱缩团根因定案(渲染实证: Python 复刻渲染=装机截图逐位吻合):
//     容器路径 pos0(首骨局部坐标)直接当顶点 → CRT_SkelSkin 蒙皮件全部塌成团(1dao01/1zhan01/1feng01/2shu2);
//     装机"正确"的 6dao01 实为四足怪物 mon172 的巧合剪影(烘焙后出腿/须/尾全形)。
//  ② 新逆向定案 — 武器类 ACT 节点表(历史"标记01"未解格式, 三样本 109/61/72 骨闭环):
//     [u32 nl][名][u32 pl][父名][u32 nf][f32 trans×nf×3][u32 nf2==nf][f32 quat×nf×4 (w,x,y,z)][尾10B]
//     静态节点 nf=0 → 双名锚点跳过; 四元数模长命中率 1.00(off=4,stride=16) + 自相关周期16 双证。
//     注意: ski 路径 quat 序 (x,y,z,w) 不变; 武器类为 (w,x,y,z) — 独立解码 BoneLocalW, 不碰 ski。
//  ③ 蒙皮配方: p'=Σw·(W_b·pos_b), W_b=父链组合世界矩阵(帧0) — 与 ski 的"纯直接"不同(文件类不同):
//     本地渲染实证 6dao01→四足怪/0_chibang01_01→立体翼/1dao01→长刃; 三级回落: skelRef外部架→自架→静态pos0;
//     烘焙门=尺度感知边长(边<max(4, 对角×0.45)), 失败回落静态(装机既有行为零回退)。
//  ④ 顶点池全文件兜底扫描: 64K窗口最长链<vNeed 时全文件找链(1dao01 fashion03a 954/955,
//     6dao01 mon172_01_b 2127/2128 差1条 → 补齐); 廉价预筛(首记录合法)控耗时。
//  ⑤ 贴图三态诊断+跨包反查: 装机8个"✗解码失败"全部无fourcc = 文件未读到(非格式问题);
//     新 TexIndex 懒建全盘文件名→路径索引(pak_out 根×4层深, .dds/.png), TryTex 未中→索引兜底;
//     状态行区分: ✗未找到(索引N) / ✗解码失败[fourcc] / ✗过短[len B] — 下一轮装机可直读定性。
//  ⑥ 回落诊断: ParseActContainer 各放弃点写 _actDiag; 点云回落状态行显示原因; iw*dao/gong 类标"武器(坐骑挂载)"。
//  验证: scripts/verify_container.py 同步移植(自骨架+烘焙+全文件扫描), 5样本回归; ski/点云路径零改动。
// ============================================================
// v4.8.7 本地全量导入 (2026-10-20 晚, 用户定调: bundle 只装文档代码, 资源留手机本地直读):
//  ① 病灶: 装机只吃 StreamingAssets 注入子集(测试部分) — 池里武器/怪物/翅膀有, 甲胄贴图缺族;
//     pak 不在游戏目录散放(ls/find 双空), 全量资源在手机本地 pak_out
//  ② 新架构「双源分层」: ProbeLocal() 探测 5 个候选路径(游戏端/pak_out, Game/pak_out,
//     pak_out2, persistentDataPath×2) → localActorDir/localTexDir;
//     ReadActorFile/ReadTexFile/TexExists 一律【本地优先, SA兜底】(每读try-catch, 本地缺失不炸);
//     Scan() SA分支合并本地骨架进 actPool; RebuildCat() 合并本地 ski/act 进浏览池(HashSet去重);
//     TexPoolNames() 合并贴图池(本地+SA排序去重) → v4.8.6 的 TexFamNote 自动适配本地真身
//  ③ 诊断: 标题行显示 探测结果[本地✓/×] + 本地 ski/骨 增量计数 — 装机一眼看清双源状态
//  ④ 解析层零改动 (verify 判据不受影响); 权限依据: BootGuard 曾成功直写 /storage/emulated/0/Download
// ============================================================
// v4.8.6 定案配套小修 (2026-10-20, 会话 git 归档后):
//  ① 定案: hj/mj/ss 甲胄贴图✗未命中 = 源包无此族贴图 — manifest.csv(awt包) texture/ 共149个dds
//     全为 a_wt_*; 装机池样本[a5sword,a_6_chibang,a_wt_*] 与 a_hj/a_mj/a_ss 字典序应在场而缺席, 双证一致。
//     真身贴图应在游戏目录其余门派 pak (ahj/amj/ass?) → 待用户解包回传, 非代码缺陷
//  ② UI: 「本职业无此部件」占位时清掉旧网格与旧状态行(此前残留上一件模型, 误读为渲染异常)
//  ③ 诊断: 贴图✗未命中行追加族缺失提示 — "✗未命中(池N个[样本],缺a_hj族(待解该族pak))",
//     池全量名一次IO缓存(_texPool), 不逐件重扫
// ============================================================
// v4.8.5 装机目检修 (2026-10-14 深夜, 依据 v4.8.4 装机 10 图):
//  ① P1 ACT点云族取景全灭(武器2zhan2 ext=947248/斧钺w_hjg ext=131356/怪物iw199_04 ext=32768 → 空屏;
//     翅膀xx_1_chibang_1 ext=464 → 贴脸穿模整屏翅面) + mesh小件(手套glo_00 106顶点 → 屏顶小碎块):
//     v4.8.4 的 d50×3 截断有两个盲区 — (a)脏点≥50%时中位数落进脏簇, 截断错切真身;
//     (b)薄密legit云(翅膀膜)d50过小, 过截→ext塌缩→相机入膜.
//     新 RobustFrame = 「最密格定心 + 间距断崖截尾」:
//       a) 每轴p2~p98粗盒 → 4×4×4网格 → 最密格质心定心(真身集中, 弥散脏簇夺不了主);
//       b) 全点到心距离排序, 相邻比值>5的最大断崖=分离垃圾组, 从断崖截尾
//          (连续legit云相邻比值~1.x无断崖→不截→细长翅膀不再塌缩); 深度≤2, 仅影响取景不改解析计数
//       c) 定心用生长区【逐轴中位数】(粗格混2%垃圾时质心被拽偏5270); 并列格偏向全局中位
//          (防0x8000双垃圾簇夺主); 冠军格区域生长BFS(≥35%冠军)后再取中位(均匀体单格偏半身)
//       仿真验证 6/6 场景通过(scripts/verify_v485_frame.py), 甲胄守护与旧算法一致(84.0 vs 83.6)
//  ② P1 BuildPointMesh 点片大小原用 raw ext(垃圾点撑爆94万→点片5683单位→单quad糊屏):
//     改用稳健 ext, 夹紧 [0.02, 50]
//  ③ 诊断: ACT 状态行 ext 显示 raw→robust 双值, 装机可直读截尾效果
// ============================================================
// v4.8.4 装机目检修 (2026-10-14 晚, 依据 14 张真机截图 + 清单互证):
//  ① P1 脸部又小又偏上(hea_00): ski 取景改为只统计【面表引用】的顶点 (FrameReferenced) —
//     ski 内未被面引用的锚点/辅助顶点(hea 类约1/3)把中位中心+p90跨度一起带偏;
//     引用顶点<8 时退回全顶点。靴/甲不受影响(其顶点几乎全被引用)
//  ② P1 点云空屏(4feng01 ext=859126 / iw02_04 ext=525472 / chibang05_03 ext=98940):
//     RobustFrame 加中位距离锚定的离群剔除 — d>max(d50×3, 0.02) 的点不参与取景
//     (仅影响取景, 不改解析计数; 剔后保留<8 点则回退原样; 每层严格减点, 递归有界)
//  ③ P1 武器贴图✗未命中(6zhan01/4feng01/iw0401dao): 武器词根匹配不再锚定行首
//     (iw0401dao 可命中 dao→sword), 且加跨阶兜底 a1..a9 (6zhan01→a5sword)
//  ④ P0 时装家族炸形(fashion04 碎片) — 双样本字节级定案(clo_00对照):
//     CRT_SkelSkin 头 = [skelRef][u16垫][u32 meshCount] ×mc:[网格名][材质名][面u16数][面表][1B标志]
//     面表用【全局顶点索引】; 顶点区 = 一条全局数组(nbk 可达 6, 六骨权重 0.25×6=1.5 合法)
//     病根×2: ChainVtx nbk≤4 在 326 条处撞 nbk=6 斩断数组(vcp 759→326) +
//             面表段"≥最长50%"把子网格1(1101u16=49.5%)拒收 → 741面×vcp326 大量越界 = 碎片
//     修复: nbk≤8 + 权重和≤2.5 + ParseMeshFaces 头部直读(面表按序拼接, vcpHint=最大索引+1,
//           顶点不足时链尾续拼); 校验链: 名字全 IsW + 面数%3==0 + 尾部 CRT_MaterialLib 魔数,
//           任何存疑 → null 回退旧启发式(bpf10 等异构头不受影响)
//     验收: clo_00 413/660 不变✓; fashion04 326→759 顶点 / 741→1108 面, 索引0越界✓,
//           本地网格渲染成片(与已知好文件同特征)✓
//  解析层接口不变, 计数器口径不变 — verify_v481g 回归不受影响
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
    static Vector3 ModelRot = new Vector3(-90f, 0f, 0f); // v4.8.3: 源数据=Z-up(真机甲胄/头/靴/手套整体横躺实证), -90°X立起; 可用"转"按钮校准
    static int _rotIdx;
    static readonly float[] _rotAng = { -90f, 0f, 90f, 180f };
    const int FrameStand = 477; // v4.8.3: frame_scan 全帧联合自洽最优站立帧(score 565)
    const float FitSize = 3.4f;
    static float _zoom = 1f;
    static string _texSample = null;
    static string[] _texPool = null;                // v4.8.6: 贴图池全量名(一次IO缓存)
    // v4.8.7: 合并贴图池 — 本地 pak_out/creature/texture 优先, SA/外部目录补齐, 排序去重
    static string[] TexPoolNames()
    {
        if (_texPool != null) return _texPool;
        var l = new List<string>();
        if (useAsset)
        {
            var ls = ListAssets("creature/texture");
            if (ls != null) l.AddRange(ls);
        }
        else if (texDir != null && Directory.Exists(texDir))
        {
            try { foreach (var f in Directory.GetFiles(texDir)) l.Add(Path.GetFileName(f)); } catch (Exception) { }
        }
        if (localTexDir != null)
        {
            var seen = new HashSet<string>(l);
            try { foreach (var f in Directory.GetFiles(localTexDir)) { var n = Path.GetFileName(f); if (seen.Add(n)) l.Add(n); } }
            catch (Exception) { }
        }
        l.Sort();
        _texPool = l.ToArray();
        return _texPool;
    }
    // v4.8.6: 未命中时的族缺失诊断 — manifest定案pak仅含a_wt族甲胄贴图,
    // hj/mj/ss 在池内必然缺席 → 提示"待解该族pak", 避免误判为代码缺陷
    static string TexFamNote(string skiName)
    {
        if (_texPool == null || string.IsNullOrEmpty(skiName)) return "";
        if (!skiName.StartsWith("a_") || skiName.Length < 4) return "";
        string fam = skiName.Substring(0, 4);       // a_hj / a_mj / a_ss / a_wt
        for (int i = 0; i < _texPool.Length; i++) if (_texPool[i].StartsWith(fam)) return "";
        return ",缺" + fam + "族(待解该族pak)";
    }
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
    static GameObject pivot, curModel;              // v4.8.3: 声明保留(原位)
    static float _curS0 = 1f;                       // v4.8.3: 基准尺度(不含zoom)
    static Vector3 _curBC = Vector3.zero;           // v4.8.3: 稳健中心
    static Vector2 _pan = Vector2.zero;             // v4.8.3: 双指平移
    static float _pinchD = 0f; static Vector2 _pinchC = Vector2.zero;

    static void ApplyFrame()                        // v4.8.3: 缩放/平移即时生效, 不重载文件
    {
        if (curModel == null) return;
        float s = _curS0 * _zoom;
        curModel.transform.localScale = new Vector3(s, s, s);
        curModel.transform.localPosition = new Vector3(0f, 1.55f, 0f) - s * _curBC + new Vector3(_pan.x, _pan.y, 0f);
    }
    void Update()                                   // v4.8.3: 双指捏合缩放 + 双指平移
    {
        if (Input.touchCount == 2)
        {
            var t0 = Input.GetTouch(0); var t1 = Input.GetTouch(1);
            var c = (t0.position + t1.position) * 0.5f;
            float nd = (t0.position - t1.position).magnitude;
            if (_pinchD > 1f)
            {
                _zoom = Mathf.Clamp(_zoom * nd / _pinchD, 0.2f, 12f);
                var dc = (c - _pinchC) * (6f / Mathf.Max(1, Screen.height));
                _pan = new Vector2(Mathf.Clamp(_pan.x + dc.x, -3f, 3f), Mathf.Clamp(_pan.y + dc.y, -3f, 3f));
            }
            _pinchD = nd; _pinchC = c;
        }
        else _pinchD = 0f;
        ApplyFrame();
    }
    static string title = "", line2 = "", line3 = "";
    static readonly Dictionary<string, ActSkel> skelCache = new Dictionary<string, ActSkel>();
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    static List<string> actPool;   // role_*.act 骨架池

    static readonly string[] Cats = {
        "武器·刀剑(槽1)", "武器·斧钺(槽2)", "甲胄·防具(3)", "头部(脸)",
        "手套·防具(1)", "长靴·防具(2)", "外装", "其他部件", "怪物/坐骑", "翅膀·外观" };   // v4.8.3: 翅膀独立分类
    // 左列显示顺序(按槽位): 甲胄→外装→头部→手套→长靴→刀剑→斧钺→其他→怪物
    static readonly int[] CatOrder = { 2, 6, 3, 4, 5, 0, 1, 7, 8, 9 };   // v4.8.3: +翅膀
    static readonly Regex RegexWeaponAct = new Regex(
        @"^\d+(dao|zhan|jian|feng|shu|gong|qiang|axe|sword|spear|bow|stick|gun)",
        RegexOptions.Compiled);
    static List<string> catFiles = new List<string>();   // 含扩展名的文件名

    // ================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        try { SetupCam(); KillOthers(); Scan(); RebuildCat(); ShowCurrent(); }
        catch (Exception e) { line3 = "[v4.8.7] 启动失败: " + e.Message; }
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
    // v4.8.7: 本地全量层 — pak_out 直读(双源合并: 本地优先, SA 兜底)
    // 依据: ①装机池只有注入子集(测试部分), 手机本地 pak_out 才是全量 ②BootGuard 已证可写 /storage/emulated/0
    static string localActorDir = null, localTexDir = null;
    static string localProbe = "";                  // 探测诊断: "pak_out✓ pak_out2× …"
    static string _actDiag = "";                    // v4.8.9: 容器解析放弃点诊断 (每次解析重置)
    static void ProbeLocal()
    {
        localActorDir = null; localTexDir = null; localProbe = "";
        string pd = Application.persistentDataPath;
        string[] cands = {
            "/storage/emulated/0/Game/游戏端/pak_out/creature/actor",
            "/storage/emulated/0/Game/pak_out/creature/actor",
            "/storage/emulated/0/Game/游戏端/pak_out2/creature/actor",
            pd + "/pak_out/creature/actor",
            pd + "/creature/actor",
        };
        foreach (var c in cands)
        {
            bool ex = Directory.Exists(c);
            string tag = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(c)));
            localProbe += (localProbe.Length > 0 ? " " : "") + tag + (ex ? "✓" : "×");
            if (!ex) continue;
            localActorDir = c;
            string t = Path.GetDirectoryName(c) + "/texture";
            localTexDir = Directory.Exists(t) ? t : c;
            return;
        }
    }
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
        // v4.8.7: 本地全量层优先 — 本地没有该文件才回落 SA/外部目录
        if (localActorDir != null)
        {
            try { var p = Path.Combine(localActorDir, fn); if (File.Exists(p)) return File.ReadAllBytes(p); } catch (Exception) { }
        }
        return useAsset ? ReadAsset("creature/actor/" + fn)
                        : File.ReadAllBytes(Path.Combine(actorDir, fn));
    }
    static byte[] ReadTexFile(string fn)
    {
        // v4.8.7: 本地全量层优先 — pak_out 的贴图不经过注入, 直接读盘
        if (localTexDir != null)
        {
            try { var p = Path.Combine(localTexDir, fn); if (File.Exists(p)) return File.ReadAllBytes(p); } catch (Exception) { }
        }
        return useAsset ? ReadAsset("creature/texture/" + fn)
                        : File.ReadAllBytes(Path.Combine(texDir, fn));
    }
    static bool TexExists(string fn)
    {
        // v4.8.7: 本地优先, SA/外部目录兜底
        if (localTexDir != null)
        {
            try { if (File.Exists(Path.Combine(localTexDir, fn))) return true; } catch (Exception) { }
        }
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
        // 0) v4.8.7: 本地全量层探测 (无论走哪个分支都要做 — 手机 pak_out 是全量真身)
        ProbeLocal();
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
            // v4.8.7: 合并本地 pak_out (骨架补进actPool; ski计数并入标题; RebuildCat 再做浏览层合并)
            int nSkiLoc = 0, nActLoc = 0;
            if (localActorDir != null)
            {
                try
                {
                    var seen = new HashSet<string>(pool);
                    foreach (var f in Directory.GetFiles(localActorDir))
                    {
                        string n = Path.GetFileName(f), e = Path.GetExtension(f).ToLower();
                        if (e == ".ski") { nSkiLoc++; continue; }
                        if (e == ".act" && n.StartsWith("role_") && seen.Add(n)) { pool.Add(n); nActLoc++; }
                    }
                }
                catch (Exception) { }
                pool.Sort();
            }
            actPool = pool; useAsset = true;
            actorDir = "asset:creature/actor"; texDir = "asset:creature/texture";
            title = string.Format("v4.8.7 整装拼合 · APK注入{0}骨/{1}ski{2}",
                pool.Count, nSki + nSkiLoc,
                localActorDir != null
                    ? string.Format(" +本地(含{0}ski/{1}骨) ✓[{2}]", nSkiLoc, nActLoc, localProbe)
                    : " 本地×[" + localProbe + "]");
            return;
        }
        // 2) 外部目录兜底
        {
            string pd = Application.persistentDataPath;
            string[] cands = {
                "/storage/emulated/0/Game/pak_out/creature/actor",
                "/storage/emulated/0/Game/游戏端/pak_out/creature/actor",
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
                localActorDir = c;                                 // v4.8.7: 外部模式下本地层=同目录, 分层读统一
                string t = Path.GetDirectoryName(c) + "/texture";
                texDir = Directory.Exists(t) ? t : c;
                localTexDir = Directory.Exists(t) ? t : c;
                actPool = new List<string>();
                foreach (var a in acts) actPool.Add(a);
                actPool.Sort();
                title = string.Format("v4.8.7 整装拼合 · 外部目录 ({0} 骨架/{1} ski)", actPool.Count, skis.Length);
                return;
            }
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
        // v4.8.7: 本地 pak_out 合并进浏览池(去重) — 全量模型不经注入直接可用
        if (localActorDir != null)
        {
            try
            {
                var seen = new HashSet<string>();
                foreach (var x in all) seen.Add(x);
                foreach (var f in Directory.GetFiles(localActorDir))
                {
                    string e = Path.GetExtension(f).ToLower();
                    if (e != ".ski" && e != ".act") continue;
                    var n = Path.GetFileName(f);
                    if (seen.Add(n)) all.Add(n);
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
            else if (CatOf(n) >= 8) take = true;                       // 怪物/坐骑/翅膀(v4.8.3含翅膀分类)
            if (take && !n.EndsWith("_atk")) catFiles.Add(f);      // v4.8.3: _atk 过滤(与CatOf双保险)
        }
        catFiles.Sort();
        FilterCat();
    }

    static int CatOf(string n)   // n 不带扩展名; -1 = 不收录
    {
        if (n.StartsWith("w_"))
        {
            if (n.EndsWith("_atk")) return -1;                     // v4.8.3: 攻击态同模不进列表
            int u = n.IndexOf('_', 2);
            string seg = u > 2 ? n.Substring(2, u - 2) : n.Substring(2);
            char wl = seg.Length > 0 ? seg[seg.Length - 1] : '?';
            if (wl == 'c' || wl == 'q' || wl == 'z' || wl == 'g' || wl == 'h') return 1;  // c/q/z/g/h型→斧钺槽
            return 0;                                              // j/l/数字型→刀剑槽
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
        if (n.Contains("chibang")) return 9;           // v4.8.3: 翅膀从怪物/坐骑独立出来
        if (n.Contains("iwgong") || n.StartsWith("mon") || n.StartsWith("s_mon")
            || n.StartsWith("sn") || n.StartsWith("iw") || n.StartsWith("s_iw")
            || n.StartsWith("wi") || n.StartsWith("wheel")
            || (n.StartsWith("z") && n.Length > 1 && char.IsLetter(n[1]))
            || n.StartsWith("boss") || n.Contains("ride") || n.Contains("zuoqi")
            || n.Contains("horse") || n.StartsWith("pet") || n.StartsWith("gua"))
            return 8;   // v4.8.3: +sn/iw/s_iw/wi/wheel/z* 怪物坐骑召唤兽族
        return -1;   // epf/eph/ept/epn_skill/ewar/wq* 等特效动画不进列表
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

    // v4.8.3: 武器族前缀 (w_mjq_f_01_03 → w_mjq_f); 非武器原样返回
    static string FamPrefix(string noExt)
    {
        if (!noExt.StartsWith("w_")) return noExt;
        var p = noExt.Split('_');
        return (p.Length >= 4 && p[3].Length > 0 && char.IsDigit(p[3][0]))
            ? p[0] + "_" + p[1] + "_" + p[2] : noExt;
    }
    static void JumpNextFam()   // 名字条点击 → 跳到下一族首件
    {
        if (curSkiList.Count == 0) return;
        string cur = FamPrefix(Path.GetFileNameWithoutExtension(curSkiList[curIdx]));
        int i = curIdx;
        while (i < curSkiList.Count && FamPrefix(Path.GetFileNameWithoutExtension(curSkiList[i])) == cur) i++;
        curIdx = (i >= curSkiList.Count) ? 0 : i;
        ShowCurrent();
    }

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
        // v4.8.3 定案: act 每骨 quat/trans 即蒙皮空间变换, 绑定块 pos=骨局部坐标 → 纯直接用
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
            if (nbk < 1 || nbk > 8) break;   // v4.8.4: 4→8 — fashion04 实证 nbk=6(六骨权重)记录存在, ≤4 会把全局顶点数组拦腰斩断
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
            if (nbk > 1 && (tw < 0.5f || tw > 2.5f)) return n;  // v4.8.4: 1.6→2.5 — nbk=6 全 0.25 权重 tw=1.5 合法
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

        // ---- A2. v4.8.4: 头部驱动多子网格面表直读 (字节级定案: clo_00=1网格/fashion04=2网格):
        // [skelRef][u16垫][u32 meshCount] ×mc:[u32 len][网格名][u32 len][材质名][u32面u16数][面表][1B标志=01]
        // 面表用【全局顶点索引】(子网格共享接缝顶点, fashion04 实测: 子网格0=0..562, 子网格1引用0..758)
        int vcpHint = -1, mcHit = 0;
        var hdrTris = ParseMeshFaces(d, F, out vcpHint, out mcHit);

        // ---- B. 顶点流定位: 自16起全长扫描, 最长自校验链者胜 (ski_obj.py 实测格式) ----
        int best = -1, bestN = 0;
        for (int p = 16; p < F - 60; p++)
        {
            int n = ChainVtx(d, p, F);
            if (n > bestN) { bestN = n; best = p; }
            if (bestN > 4000) break;
        }
        if (bestN < 8) throw new Exception("顶点流未定位(最长链" + bestN + ")");
        int p2 = WalkVerts(sd, d, best, F, int.MaxValue);
        int vcp = sd.verts.Count;
        // v4.8.4: 面表驱动补齐 — 极少数文件顶点数组间有插入块时, 最长链<vcpHint → 从链尾顺序续拼
        if (hdrTris != null && vcpHint > vcp && vcpHint <= 60000)
        {
            int sp = p2;
            while (sd.verts.Count < vcpHint && sp < F - 60)
            {
                int cn = ChainVtx(d, sp, F);
                if (cn >= 8) { p2 = WalkVerts(sd, d, sp, F, vcpHint); sp = p2; }
                else sp++;
            }
            vcp = sd.verts.Count;
        }
        if (vcp < 3) throw new Exception("0 顶点");

        // ---- C. 面表: 小端 u16 (v4.7 误用大端 = 碎片化根因) ----
        bool faceAfter = false, beUsed = false; int candCnt = 0;
        if (hdrTris != null)
        {
            // v4.8.4: 头部直读 = 全部子网格面表按序拼接, 全局索引对全局顶点数组 — 不再启发式段收集
            sd.tris = hdrTris;
        }
        else
        {
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
        candCnt = cands.Count;
        cands.Sort((x, y) => y[1].CompareTo(x[1]));
        int maxLen = cands.Count > 0 ? cands[0][1] : 0;
        var taken = new List<int[]>();   // 已产出段 [start, end)
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
        }
        if (sd.tris.Count == 0) throw new Exception("0 面表(候选" + candCnt + "段)");

        // ---- D. 材质锚名 ----
        sd.anchors = ExtractAnchors(d);

        sd.fmt = string.Format("v4.8.7 顶点{0} 面{1} 流@0x{2:x}{3}{4}{5}{6}",
            vcp, sd.tris.Count / 3, best,
            sd.anchors.Count > 0 ? " 锚" + sd.anchors.Count : "",
            beUsed ? " 面[大端]" : "", faceAfter ? " 面在流后" : "",
            mcHit > 1 ? " 子网格" + mcHit : "");
        return sd;
    }

    // v4.8.4: 顶点记录走查(自 p 起, 逐条解析并入 sd, 到 limit 条/链断/EOF 止) — 返回结束偏移
    static int WalkVerts(SkiData sd, byte[] d, int p, int F, int limit)
    {
        int p2 = p;
        while (p2 < F - 8 && sd.verts.Count < limit)
        {
            if (p2 + 4 > F) break;
            int nbk = U32(d, p2);
            if (nbk < 1 || nbk > 8) break;   // v4.8.4: 4→8 同 ChainVtx
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
        return p2;
    }

    // v4.8.4: 头部驱动多子网格面表直读 — 布局字节级定案(clo_00/fashion04 双样本互证):
    // [FF FF 0C CRT_SkelSkin][u32 len][skelRef][u16垫][u32 meshCount]
    //   ×mc: [u32 len][网格名][u32 len][材质名][u32 面u16数][面表(LE u16)][1B 标志=01]
    // [ff ff][u16=15]["CRT_MaterialLib"]...
    // 成功=全部子网格面表拼接(全局顶点索引); 任何存疑返回 null → 回退启发式段收集(bpf10 等异构头)
    static List<int> ParseMeshFaces(byte[] d, int F, out int vcpHint, out int mc)
    {
        vcpHint = -1; mc = 0;
        try
        {
            int o = 16;
            int nl = (int)U32(d, o);
            if (nl < 1 || nl > 64 || o + 4 + nl + 2 > F) return null;
            for (int i = 0; i < nl; i++) if (!IsW(d[o + 4 + i])) return null;
            o += 4 + nl + 2;                          // skelRef + u16垫
            mc = (int)U32(d, o); o += 4;
            if (mc < 1 || mc > 8) return null;   // v4.8.4: ≤8 — 校验链足够强, 覆盖多件套时装
            var tris = new List<int>(); int mx = 0;
            for (int m = 0; m < mc; m++)
            {
                for (int k = 0; k < 2; k++)           // 网格名 + 材质名
                {
                    int sn = (int)U32(d, o);
                    if (sn < 2 || sn > 48 || o + 4 + sn > F) return null;
                    for (int i = 0; i < sn; i++) if (!IsW(d[o + 4 + i])) return null;
                    o += 4 + sn;
                }
                int fc = (int)U32(d, o); o += 4;
                if (fc < 3 || fc > 262144 || fc % 3 != 0 || o + 2 * fc > F) return null;
                for (int j = 0; j < fc; j++)
                {
                    int a = (int)U16(d, o + 2 * j);
                    tris.Add(a);
                    if (a > mx) mx = a;
                }
                o += 2 * fc + 1;                      // 面表 + 1B标志
            }
            if (o + 2 > F || d[o] != 0xFF || d[o + 1] != 0xFF) return null;
            if (o + 19 > F || Str(d, o + 4, 15) != "CRT_MaterialLib") return null;
            if (mx + 1 > 60000) return null;
            vcpHint = mx + 1;
            return tris;
        }
        catch { return null; }
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
        // v4.8.8: 容器真网格 (CRT_SkelSkin/CRT_VaSkin 块解析成功时填充)
        public List<Vector3> rverts = new List<Vector3>();
        public List<Vector2> ruvs = new List<Vector2>();
        public List<int> rtris = new List<int>();
        public bool real { get { return rverts != null && rverts.Count > 2 && rtris != null && rtris.Count >= 3; } }
        // v4.8.9: 自文件节点表(武器类 ver2 布局) + 蒙皮/池统计 + 回落诊断
        public ActSkel self;
        public int bakedSubs, poolSubs, staticSubs;
        public string rigTag = "";
    }

    // ================================================================
    //  v4.8.8: CRT_Actor 容器解析 — 武器/坐骑/翅膀真网格 (替换点云误读)
    //  布局逆向定案 (样本闭环: iwgong01/iw0401huan_04/1dao01/6dao01/0_chibang01_01):
    //  · 块流 = [FF FF][u16 块名长][块名]...: CRT_Actor/CRT_SkelSkin/CRT_VaSkin/CRT_MaterialLib/CRT_Mtl*/
    //    CRT_EffectEmitter
    //  · CRT_SkelSkin 块 = [u32 len][skelRef][u16垫][u32 mc] ×mc:[u32 len][网格名][u32 len][材质名]
    //    [u32 面数][面表 LE u16][01标志] — 与 .ski 的 ParseMeshFaces 结构一致, 面表后紧跟本块顶点链
    //  · CRT_VaSkin 块 = [u32 len][名][标志字节][u32 len][名][u32 len][名][u32 面数][面表 LE u16]
    //  · 顶点: ski 式自校验记录链 (nbk+骨id/权重/每骨pos 变长记录, ChainVtx/WalkVerts 直接复用);
    //    或 VaSkin 32B定长池 [pos3][nrm3][uv2] 全LE (前缀 u16 顶点数)
    //  ================================================================
    class ActSub { public string mesh, mat, skel; public List<int> idx = new List<int>(); public int faceEnd; }

    static bool ActNameAt(byte[] d, int q, int maxn)
    {
        if (q + 4 > d.Length) return false;
        int n = U32(d, q);
        if (n < 1 || n > maxn || q + 4 + n > d.Length) return false;
        return Printable(d, q + 4, n);
    }
    static string ActNameGet(byte[] d, int q, out int end)
    {
        int n = U32(d, q);
        end = q + 4 + n;
        return Str(d, q + 4, n);
    }

    static ActSub ParseSkelChunkFaces(byte[] d, int o, int F)
    {
        try
        {
            int p = o + 4 + U16(d, o + 2);                     // 块名后 (FF FF + u16长 + 名字)
            if (!ActNameAt(d, p, 64)) return null;
            int skEnd;                                         // skelRef
            string skelName = ActNameGet(d, p, out skEnd);
            p = skEnd;
            if (p + 6 > F) return null;
            p += 2;                                            // u16 垫
            int mc = U32(d, p); p += 4;
            if (mc < 1 || mc > 8) return null;
            var s = new ActSub();
            s.skel = skelName;
            for (int m = 0; m < mc; m++)
            {
                if (!ActNameAt(d, p, 48)) return null;
                string mesh = ActNameGet(d, p, out p);
                if (!ActNameAt(d, p, 48)) return null;
                string mat = ActNameGet(d, p, out p);
                int fc = U32(d, p); p += 4;
                if (fc < 3 || fc > 262144 || fc % 3 != 0 || p + 2 * fc + 1 > F) return null;
                for (int j = 0; j < fc; j++)
                {
                    int a = U16(d, p + 2 * j);
                    if (a > 60000) return null;
                    s.idx.Add(a);
                }
                p += 2 * fc + 1;                               // 面表 + 01标志
                if (m == 0) { s.mesh = mesh; s.mat = mat; }
            }
            s.faceEnd = p;
            return s;
        }
        catch (Exception) { return null; }
    }
    static int SkipName(byte[] d, int p) { return p + 4 + U32(d, p); }

    static ActSub ParseVaChunkFaces(byte[] d, int o, int F)
    {
        try
        {
            int p = o + 4 + U16(d, o + 2);                     // 块名后
            int names = 0;
            while (names < 3)                                  // 观测: 恰好3个名字字段(中间夹标志字节)
            {
                bool got = false;
                for (int skip = 0; skip <= 8 && p + skip + 4 <= F; skip++)
                {
                    if (ActNameAt(d, p + skip, 64)) { p = SkipName(d, p + skip); names++; got = true; break; }
                }
                if (!got) break;
            }
            if (names < 2) return null;
            int cnt = U32(d, p); p += 4;
            if (cnt < 3 || cnt > 300000 || p + 2 * cnt > F) return null;
            var s = new ActSub();
            int mx = 0;
            for (int k = 0; k < cnt; k++)
            {
                int a = U16(d, p + 2 * k);
                if (a > 60000) return null;
                s.idx.Add(a); if (a > mx) mx = a;
            }
            p += 2 * cnt;
            // 面表尾应贴近下一块头 (0~4字节垫) 或文件尾
            bool okEnd = p + 4 >= F;
            for (int e = 0; e <= 4 && !okEnd && p + 1 + e < F; e++)
                if (d[p + e] == 0xFF && d[p + e + 1] == 0xFF) okEnd = true;
            if (!okEnd) return null;
            s.faceEnd = p;
            return s;
        }
        catch (Exception) { return null; }
    }

    // 三角形抽样边长 — 顶点/面表配对质量判据 (真配对边短, 错配/垃圾边超长或全零)
    static float ActEdgeCheck(List<Vector3> vs, List<int> idx)
    {
        int nt = idx.Count / 3;
        if (nt == 0) return 0f;
        int n = Math.Min(24, nt);
        float tot = 0f; int cnt = 0;
        for (int k = 0; k < n; k++)
        {
            int t = k * nt / n;
            int a = idx[t * 3], b = idx[t * 3 + 1], c = idx[t * 3 + 2];
            if (a >= vs.Count || b >= vs.Count || c >= vs.Count) return 1e9f;
            tot += Vector3.Distance(vs[a], vs[b]) + Vector3.Distance(vs[b], vs[c]) + Vector3.Distance(vs[c], vs[a]);
            cnt += 3;
        }
        return cnt > 0 ? tot / cnt : 1e9f;
    }

    // ================================================================
    //  v4.8.9: ACT 自文件节点表 (武器类 ver2 布局 — 历史"标记01"格式破解)
    //  [u32 nl][名][u32 pl][父名][u32 nf][f32 trans×nf×3][u32 nf2==nf][f32 quat×nf×4 (w,x,y,z)][尾10B]
    //  静态节点 nf=0 → 双名锚点跳过; 证据: 1dao01(109骨)/6dao01(61)/0_chibang01_01(72) 全走查 tail=10 恒定;
    //  四元数模长命中率 1.00(off=4,stride=16) + 字节自相关周期16 双重实证。
    //  ================================================================
    static ActSkel ParseSelfNodes(byte[] d)
    {
        try
        {
            int F = d.Length;
            if (F < 64 || d[0] != 0xFF || d[1] != 0xFF) return null;
            int tl = U16(d, 2);
            int o = FindPattern(d, tl + 4, new byte[] { 0x00, 0x00, 0x80, 0x3f });   // 1.0f 头
            if (o < 0) return null;
            o += 6;
            if (o + 4 > F) return null;
            int cnt = U32(d, o); o += 4;
            if (cnt < 1 || cnt > 512) return null;
            var sk = new ActSkel(); sk.srcName = "self";
            for (int i = 0; i < cnt; i++)
            {
                if (!ActNameAt(d, o, 24)) return null;
                string nm = ActNameGet(d, o, out o);
                if (!ActNameAt(d, o, 24)) return null;
                string par = ActNameGet(d, o, out o);
                if (o + 4 > F) return null;
                int nf = U32(d, o); o += 4;
                var b = new Bone(); b.name = nm; b.parent = par;
                if (nf >= 1 && nf <= 60000 && o + (long)nf * 12 + 4 <= F)
                {
                    b.nf = nf;
                    b.trans = new float[nf * 3];
                    for (int k = 0; k < nf * 3; k++) { b.trans[k] = F32(d, o); o += 4; }
                    if (o + 4 > F) return null;
                    int nf2 = U32(d, o); o += 4;
                    if (nf2 != nf || o + (long)nf * 16 > F) return null;   // 布局失配 → 整表放弃
                    b.quats = new float[nf * 4];
                    for (int k = 0; k < nf * 4; k++) { b.quats[k] = F32(d, o); o += 4; }
                    o += 10;                                              // 恒定尾 10B (三样本走查定案)
                }
                else
                {
                    b.nf = 0;                                             // 静态节点 → 双名锚点跳
                    int nxt = FindNodeAnchor(d, o, F);
                    if (nxt >= F) { sk.bones.Add(b); break; }
                    o = nxt;
                }
                sk.bones.Add(b);
            }
            return sk.bones.Count > 0 ? sk : null;
        }
        catch (Exception) { return null; }
    }
    static int FindNodeAnchor(byte[] d, int o, int F)
    {
        for (int p = o; p + 12 < F; p++)
        {
            if (ActNameAt(d, p, 24))
            {
                int e1 = SkipName(d, p);
                if (ActNameAt(d, e1, 24)) return p;
            }
        }
        return F;
    }

    // v4.8.9: 武器类蒙皮局部变换 — quat 存 (w,x,y,z), 与 ski 路径 BoneLocal 的 (x,y,z,w) 不同
    static FramePose BoneLocalW(Bone b, int frame)
    {
        var fp = new FramePose();
        if (b.nf > 0 && b.quats != null && b.trans != null)
        {
            int f = frame >= b.nf ? b.nf - 1 : frame; if (f < 0) f = 0;
            float w = b.quats[f * 4], x = b.quats[f * 4 + 1], y = b.quats[f * 4 + 2], z = b.quats[f * 4 + 3];
            float n = x * x + y * y + z * z + w * w;
            if (n < 1e-12f) { fp.R[0] = fp.R[4] = fp.R[8] = 1f; }
            else
            {
                float s = 2f / n;
                fp.R[0] = 1 - s * (y * y + z * z); fp.R[1] = s * (x * y - z * w); fp.R[2] = s * (x * z + y * w);
                fp.R[3] = s * (x * y + z * w);     fp.R[4] = 1 - s * (x * x + z * z); fp.R[5] = s * (y * z - x * w);
                fp.R[6] = s * (x * z - y * w);     fp.R[7] = s * (y * z + x * w); fp.R[8] = 1 - s * (y * y + x * x);
            }
            fp.tx = b.trans[f * 3]; fp.ty = b.trans[f * 3 + 1]; fp.tz = b.trans[f * 3 + 2];
        }
        else { fp.R[0] = fp.R[4] = fp.R[8] = 1f; }
        return fp;
    }

    // v4.8.9: 父链组合世界矩阵 W_b = W_parent·(R,t) — 武器类 ACT 存每骨局部变换, 须组合;
    // (ski 装备的 role 骨架存已组合变换 → 纯直接, 两条路径互不干涉)
    static List<FramePose> WorldMats(ActSkel sk, int frame)
    {
        int n = sk.bones.Count;
        var nameIdx = new Dictionary<string, int>();
        for (int i = 0; i < n; i++) if (!nameIdx.ContainsKey(sk.bones[i].name)) nameIdx[sk.bones[i].name] = i;
        var W = new List<FramePose>(n);
        for (int i = 0; i < n; i++) W.Add(null);
        var done = new bool[n];
        for (int i = 0; i < n; i++) WMResolve(sk, frame, i, W, done, nameIdx);
        return W;
    }
    static void WMResolve(ActSkel sk, int frame, int i, List<FramePose> W, bool[] done, Dictionary<string, int> nameIdx)
    {
        if (done[i]) return;
        done[i] = true;
        var b = sk.bones[i];
        var fp = BoneLocalW(b, frame);
        int pid;
        if (b.parent != null && nameIdx.TryGetValue(b.parent, out pid) && pid != i)
        {
            if (!done[pid]) WMResolve(sk, frame, pid, W, done, nameIdx);
            var pp = W[pid];
            if (pp != null)
            {
                float[] R = new float[9];
                R[0] = pp.R[0] * fp.R[0] + pp.R[1] * fp.R[3] + pp.R[2] * fp.R[6];
                R[1] = pp.R[0] * fp.R[1] + pp.R[1] * fp.R[4] + pp.R[2] * fp.R[7];
                R[2] = pp.R[0] * fp.R[2] + pp.R[1] * fp.R[5] + pp.R[2] * fp.R[8];
                R[3] = pp.R[3] * fp.R[0] + pp.R[4] * fp.R[3] + pp.R[5] * fp.R[6];
                R[4] = pp.R[3] * fp.R[1] + pp.R[4] * fp.R[4] + pp.R[5] * fp.R[7];
                R[5] = pp.R[3] * fp.R[2] + pp.R[4] * fp.R[5] + pp.R[5] * fp.R[8];
                R[6] = pp.R[6] * fp.R[0] + pp.R[7] * fp.R[3] + pp.R[8] * fp.R[6];
                R[7] = pp.R[6] * fp.R[1] + pp.R[7] * fp.R[4] + pp.R[8] * fp.R[7];
                R[8] = pp.R[6] * fp.R[2] + pp.R[7] * fp.R[5] + pp.R[8] * fp.R[8];
                float tx = pp.R[0] * fp.tx + pp.R[1] * fp.ty + pp.R[2] * fp.tz + pp.tx;
                float ty = pp.R[3] * fp.tx + pp.R[4] * fp.ty + pp.R[5] * fp.tz + pp.ty;
                float tz = pp.R[6] * fp.tx + pp.R[7] * fp.ty + pp.R[8] * fp.tz + pp.tz;
                fp.R = R; fp.tx = tx; fp.ty = ty; fp.tz = tz;
            }
        }
        W[i] = fp;
    }

    // v4.8.9: 顶点组对角线 — 烘焙门的尺度感知基准
    static float BoundsDiag(List<Vector3> vs)
    {
        if (vs == null || vs.Count == 0) return 1f;
        Vector3 mn = vs[0], mx = vs[0];
        for (int i = 1; i < vs.Count; i++)
        {
            if (vs[i].x < mn.x) mn.x = vs[i].x; if (vs[i].x > mx.x) mx.x = vs[i].x;
            if (vs[i].y < mn.y) mn.y = vs[i].y; if (vs[i].y > mx.y) mx.y = vs[i].y;
            if (vs[i].z < mn.z) mn.z = vs[i].z; if (vs[i].z > mx.z) mx.z = vs[i].z;
        }
        return (mx - mn).magnitude;
    }

    // v4.8.9: 全文件链扫描兜底 — 64K窗口最长链不足 vNeed 时用 (首记录合法廉价预筛控耗时);
    // 实证: 1dao01 fashion03a 954/955, 6dao01 mon172_01_b 2127/2128 — 真池在窗口边界外差1条
    static int ScanWholePool(byte[] d, int F, int vNeed)
    {
        int bestP = -1, bestN = 0;
        for (int p = 0; p < F - 60; p++)
        {
            int nb = U32(d, p);
            if (nb < 1 || nb > 8) continue;
            if (d[p + 4] > 127) continue;
            float w0 = F32(d, p + 5);
            if (w0 < -0.01f || w0 > 1.001f) continue;
            int n = ChainVtx(d, p, F);
            if (n >= vNeed) return p;
            if (n > bestN) { bestN = n; bestP = p; }
        }
        return bestP;
    }

    // 从块面表尾提取顶点: A) ski记录链(续拼) B) VaSkin 32B定长池; 成功返回 true
    static bool ExtractActVerts(ActMeshData am, ActSub s, byte[] d, int F)
    {
        int vNeed = 0;
        for (int i = 0; i < s.idx.Count; i++) if (s.idx[i] + 1 > vNeed) vNeed = s.idx[i] + 1;
        if (vNeed < 3 || vNeed > 60000) return false;
        int winLo = s.faceEnd, winHi = Math.Min(F, s.faceEnd + 65536);

        // ---- A. ski 记录链 (武器/怪物/翅膀) — v4.8.9: 64K窗口→全文件兜底, 命中后烘焙/静态双尝试 ----
        int bestP = -1, bestN = 0;
        for (int p = winLo; p < winHi - 60; p++)
        {
            int n = ChainVtx(d, p, F);
            if (n > bestN) { bestN = n; bestP = p; }
            if (bestN >= vNeed) break;
        }
        if (bestN < vNeed)
        {
            int gP = ScanWholePool(d, F, vNeed);
            if (gP >= 0) { bestP = gP; bestN = vNeed; }
        }
        if (bestP >= 0 && bestN >= Math.Min(8, vNeed))
        {
            var tmp = new SkiData();
            int sp = bestP, guard = 0;
            int scanHi = Math.Min(F, bestP + vNeed * 8 * 30 + 4096);
            while (tmp.verts.Count < vNeed && sp < scanHi && guard < 4096)
            {
                int p2 = WalkVerts(tmp, d, sp, F, vNeed - tmp.verts.Count);
                if (p2 <= sp) { sp++; guard++; continue; }
                sp = p2;
            }
            if (tmp.verts.Count >= vNeed)
            {
                // v4.8.9 蒙皮: 自文件节点表(帧0) 父链组合烘焙; 失败回落 pos0 静态 (装机行为零回退)
                if (am.self != null)
                {
                    var W = WorldMats(am.self, 0);
                    int vb = am.rverts.Count;
                    for (int i = 0; i < vNeed; i++)
                    {
                        am.rverts.Add(BakeVertex(W, tmp.verts[i]));
                        am.ruvs.Add(i < tmp.uv.Count ? tmp.uv[i] : Vector2.zero);
                    }
                    var probe = new List<Vector3>(vNeed);
                    for (int i = 0; i < vNeed; i++) probe.Add(am.rverts[vb + i]);
                    float ed = ActEdgeCheck(probe, s.idx);
                    float dg = BoundsDiag(probe);
                    if (ed < Mathf.Max(4f, dg * 0.45f) && dg > 1e-4f)
                    {
                        am.bakedSubs++;
                        if (am.rigTag.Length == 0) am.rigTag = "烘[自架" + am.self.bones.Count + "骨帧0]";
                        AppendSubTris(am, s, vb);
                        return true;
                    }
                    am.rverts.RemoveRange(vb, vNeed);
                    am.ruvs.RemoveRange(vb, vNeed);
                }
                int vb0 = am.rverts.Count;
                for (int i = 0; i < vNeed; i++)
                {
                    am.rverts.Add(tmp.verts[i].pos0);
                    am.ruvs.Add(i < tmp.uv.Count ? tmp.uv[i] : Vector2.zero);
                }
                var probe0 = new List<Vector3>(vNeed);
                for (int i = 0; i < vNeed; i++) probe0.Add(am.rverts[vb0 + i]);
                if (ActEdgeCheck(probe0, s.idx) < 4.0f)
                {
                    am.staticSubs++;
                    AppendSubTris(am, s, vb0);
                    return true;
                }
                am.rverts.RemoveRange(vb0, vNeed);
                am.ruvs.RemoveRange(vb0, vNeed);
            }
        }

        // ---- B. VaSkin 32B定长池 [pos3][nrm3][uv2] 全LE, 前缀u16/u32 == vNeed (坐骑/静态) ----
        for (int pre = 2; pre <= 4 && winLo + pre + vNeed * 32 <= F; pre += 2)
        {
            for (int s0 = winLo; s0 + pre + vNeed * 32 <= Math.Min(winHi, F); s0++)
            {
                int c = pre == 2 ? U16(d, s0) : U32(d, s0);
                if (c != vNeed) continue;
                int ps = s0 + pre;
                int nok = 0, uok = 0, tot0 = 0;
                for (int v = 0; v < vNeed; v++)
                {
                    int o = ps + v * 32;
                    float x = F32(d, o), y = F32(d, o + 4), z = F32(d, o + 8);
                    float nx = F32(d, o + 12), ny = F32(d, o + 16), nz = F32(d, o + 20);
                    float u = F32(d, o + 24), w = F32(d, o + 28);
                    if (x != x || y != y || z != z || Mathf.Abs(x) > 1e5f || Mathf.Abs(y) > 1e5f || Mathf.Abs(z) > 1e5f) break;
                    if (x == 0f && y == 0f && z == 0f && nx == 0f && ny == 0f && nz == 0f) break;
                    float nl = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (Mathf.Abs(nl - 1f) < 0.2f) nok++;
                    if (u > -0.05f && u < 1.05f && w > -0.05f && w < 1.05f) uok++;
                    tot0++;
                }
                if (tot0 < vNeed || nok * 100 / vNeed < 55 || uok * 100 / vNeed < 55) continue;
                int vb2 = am.rverts.Count;
                for (int v = 0; v < vNeed; v++)
                {
                    int o = ps + v * 32;
                    am.rverts.Add(new Vector3(F32(d, o), F32(d, o + 4), F32(d, o + 8)));
                    am.ruvs.Add(new Vector2(F32(d, o + 24), F32(d, o + 28)));
                }
                var probe2 = new List<Vector3>(vNeed);
                for (int i = 0; i < vNeed; i++) probe2.Add(am.rverts[vb2 + i]);
                if (ActEdgeCheck(probe2, s.idx) < 4.0f)
                {
                    am.poolSubs++;
                    AppendSubTris(am, s, vb2);
                    return true;
                }
                am.rverts.RemoveRange(vb2, vNeed);
                am.ruvs.RemoveRange(vb2, vNeed);
            }
        }
        return false;
    }
    static void AppendSubTris(ActMeshData am, ActSub s, int vb)
    {
        for (int k = 0; k + 2 < s.idx.Count; k += 3)
        {
            int a = s.idx[k] + vb, b = s.idx[k + 1] + vb, c = s.idx[k + 2] + vb;
            if (a == b || b == c || a == c) continue;
            am.rtris.Add(a); am.rtris.Add(b); am.rtris.Add(c);
        }
    }

    // 容器解析主入口: 遍历块流 → 面表 + 顶点 → 真网格; 未命中返回 null
    static ActMeshData ParseActContainer(byte[] d)
    {
        int F = d.Length;
        if (F < 64 || d[0] != 0xFF || d[1] != 0xFF) return null;
        if (IndexOf(d, "CRT_Actor", 0) < 0 && IndexOf(d, "CRT_SkelSkin", 0) < 0 && IndexOf(d, "CRT_VaSkin", 0) < 0) return null;
        var subs = new List<ActSub>();
        var matNames = new List<string>();
        int o = 4, nodes = 0;
        while (o < F - 8)
        {
            if (d[o] == 0xFF && d[o + 1] == 0xFF)
            {
                int nl = U16(d, o + 2);
                if (nl >= 4 && nl <= 32 && o + 4 + nl <= F && Printable(d, o + 4, nl))
                {
                    string nm = Str(d, o + 4, nl);
                    if (nm == "CRT_SkelSkin")
                    {
                        var s = ParseSkelChunkFaces(d, o, F);
                        if (s != null) { subs.Add(s); o = s.faceEnd; continue; }
                    }
                    else if (nm == "CRT_VaSkin")
                    {
                        var s = ParseVaChunkFaces(d, o, F);
                        if (s != null) { subs.Add(s); o = s.faceEnd; continue; }
                    }
                    o += 4 + nl;
                    continue;
                }
            }
            o++;
        }
        if (subs.Count == 0) { _actDiag = "无容器网格块"; return null; }
        var am = new ActMeshData();
        am.nodes = nodes; am.meshes = 0;
        am.self = ParseSelfNodes(d);                       // v4.8.9: 自文件节点表(蒙皮地基), 失败=静态回落
        foreach (var s in subs)
        {
            if (ExtractActVerts(am, s, d, F))
            {
                am.meshes++;
                if (!string.IsNullOrEmpty(s.mat) && !matNames.Contains(s.mat)) matNames.Add(s.mat);
            }
        }
        if (!am.real) { _actDiag = "容器块" + subs.Count + "个但顶点提取全败"; return null; }
        // 材质名优先作贴图锚 (块内解析的可信度高于扫描); 兼容 _m 去除变体
        var anchors = new List<string>();
        foreach (var mn in matNames)
        {
            anchors.Add(mn);
            int u = mn.LastIndexOf('_');
            if (u > 0 && mn.Length - u <= 3) anchors.Add(mn.Substring(0, u));
        }
        foreach (var a in ExtractAnchorsAct(d)) if (!anchors.Contains(a)) anchors.Add(a);
        am.anchors = anchors;
        am.fmt = string.Format("容器 顶点{0} 面{1} 子网格{2}/{3}", am.rverts.Count, am.rtris.Count / 3, am.meshes, subs.Count);
        if (am.self != null) am.fmt += " 自架" + am.self.bones.Count + "骨";
        if (am.bakedSubs > 0) am.fmt += " 烘" + am.bakedSubs;
        if (am.poolSubs > 0) am.fmt += " 池" + am.poolSubs;
        if (am.staticSubs > 0) am.fmt += " 静" + am.staticSubs;
        return am;
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
        for (int sp = 26; sp < lim; sp++)
        {
            int x = U32(d, sp);
            if (x < 1 || x > 4096) continue;
            string s1 = null, s2 = null; int e1 = 0, e2 = 0;
            if (!ActStr(d, sp + 4, ref s1, ref e1, 64, 2)) continue;
            if (!ActStr(d, e1, ref s2, ref e2, 64, 2)) continue;
            string l1 = s1.ToLower(), l2 = s2.ToLower();
            if (l1 == "root" || l1 == "bip01" || l2 == "null") cands.Insert(0, sp);
            else cands.Add(sp);
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
                {
                    var v = new Vector3(F32(d, p + 4 + k * 12), F32(d, p + 8 + k * 12), F32(d, p + 12 + k * 12));
                    if (v.x > -1e6f && v.x < 1e6f && v.y > -1e6f && v.y < 1e6f && v.z > -1e6f && v.z < 1e6f) am.pts.Add(v);  // v4.8.3: 滤垃圾浮点(NaN比较为false自动滤), 否则包围盒中心=NaN→视锥剔除全隐
                }
                p += 4 + V * 12;
                if (p + (long)V * 16 <= F2 && NiceRatio(d, p, V * 4) >= 0.6f) p += V * 16;  // 跳过uv/权重块
            }
            else p++;
        }
        if (am.meshes == 0) throw new Exception("无网格节点(纯动画/特效)");
        // v4.8.3 救援: 签名扫描仅抓到1个块(疑似误报)时, 用节点表走查再试, 取网格数多者
        if (am.meshes <= 1)
        {
            var am2 = new ActMeshData();
            try { WalkActNodes(d, cands[0] + 4, nodeCount0, F, am2); } catch (Exception) { }
            if (am2.meshes > am.meshes) { am.meshes = am2.meshes; am.pts = am2.pts; }
        }
        am.nodes = nodeCount0;
        am.fmt = string.Format("v4.8.7 ACT点云 节点表{0} 网格{1} 点{2}", nodeCount0, am.meshes, am.pts.Count);
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
                {
                    var v = new Vector3(F32(d, o3 + 4 + k * 12), F32(d, o3 + 8 + k * 12), F32(d, o3 + 12 + k * 12));
                    if (v.x > -1e6f && v.x < 1e6f && v.y > -1e6f && v.y < 1e6f && v.z > -1e6f && v.z < 1e6f) am.pts.Add(v);  // v4.8.3: 同上滤垃圾
                }
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
        if (curSkiList.Count == 0)
        {
            line2 = "(本分类当前职业无模型) 池内: " + poolStat;
            line3 = "";                                            // v4.8.6: 清旧状态行
            if (hostPv != null && _animCo != null) { hostPv.StopCoroutine(_animCo); _animCo = null; }
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }  // v4.8.6: 清旧网格, 此前残留上一件
            return;
        }
        if (curIdx >= curSkiList.Count) curIdx = 0;
        curSki = curSkiList[curIdx];
        StartShow(curSki);
    }

    static void StartShow(string fileName)
    {
        string noExt = Path.GetFileNameWithoutExtension(fileName);
        _pan = Vector2.zero;                           // v4.8.3: 换模型复位平移
        line2 = noExt + "  <" + Cats[catI] + ">  [" + CurJob + "_" + CurSex + "]";
        _animSk = null; _animMesh = null; _animSd = null;
        if (hostPv != null && _animCo != null) { hostPv.StopCoroutine(_animCo); _animCo = null; }
        if (hostPv == null) return;
        if (_loadCo != null) hostPv.StopCoroutine(_loadCo);
        _loadCo = hostPv.StartCoroutine(LoadShowCo(fileName));
    }

    // v4.8.5: 稳健取景 — 「最密格定心 + 间距断崖截尾」(替换 v4.8.3 逐轴中位 + v4.8.4 d50×3 截断)
    // v4.8.4 d50×3 截断两盲区(装机10图实锤): ①脏点≥50%(武器/怪物特效帧)→中位数落进脏簇→错切真身→空屏;
    // ②薄密legit云(翅膀膜)→d50过小→过截→ext塌缩→相机贴脸. 新法:
    //   a) 每轴 p2~p98 粗盒 → 4×4×4 网格 → 最密格质心定心(真身集中, 弥散脏簇夺不了主);
    //   b) 全点到心距离排序 → 相邻比值>5 的最大断崖 = 分离的垃圾远组 → 从断崖截尾
    //      (连续 legit 云相邻比值~1.x, 无断崖 → 不截 → 细长翅膀不再塌缩);
    //   c) ext = 存活点 p90×2.2. 仅影响取景不改解析计数; 深度≤2 递归有界.
    static void RobustFrame(Vector3[] vs, ref Vector3 bc, ref float ext, int depth = 0)
    {
        int n = vs.Length;
        if (n == 0) { bc = Vector3.zero; ext = 1f; return; }
        var xs = new float[n]; var ys = new float[n]; var zs = new float[n];
        for (int i = 0; i < n; i++) { xs[i] = vs[i].x; ys[i] = vs[i].y; zs[i] = vs[i].z; }
        System.Array.Sort(xs); System.Array.Sort(ys); System.Array.Sort(zs);
        var med = new Vector3(xs[n / 2], ys[n / 2], zs[n / 2]);
        Vector3 cen = med;
        if (n >= 24 && depth < 2)
        {
            // a) p2~p98 粗盒 + 4×4×4 最密格定心
            var bmin = new Vector3(xs[n * 2 / 100], ys[n * 2 / 100], zs[n * 2 / 100]);
            int h = Mathf.Max(n * 2 / 100, n * 98 / 100 - 1);
            var bmax = new Vector3(xs[h], ys[h], zs[h]);
            var span = bmax - bmin;
            float sp = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
            if (sp > 1e-5f)
            {
                var cell = new Vector3(Mathf.Max(span.x, sp * 0.01f), Mathf.Max(span.y, sp * 0.01f), Mathf.Max(span.z, sp * 0.01f)) / 4f;
                var cnt = new int[64];
                for (int i = 0; i < n; i++)
                {
                    int cx = Mathf.Clamp((int)((vs[i].x - bmin.x) / cell.x), 0, 3);
                    int cy = Mathf.Clamp((int)((vs[i].y - bmin.y) / cell.y), 0, 3);
                    int cz = Mathf.Clamp((int)((vs[i].z - bmin.z) / cell.z), 0, 3);
                    cnt[(cy * 4 + cz) * 4 + cx]++;
                }
                int bi = 0; for (int i = 1; i < 64; i++) if (cnt[i] > cnt[bi]) bi = i;
                // v4.8.5b: 并列偏向 — 计数≥70%冠军的格里取离全局中位最近的
                // (防 0x8000 型双垃圾紧凑簇与真身打平时夺主, 实测怪物 iw199)
                int maxc = cnt[bi];
                float bd = float.MaxValue;
                for (int i = 0; i < 64; i++)
                {
                    if (cnt[i] < Mathf.Max(maxc * 7 / 10, 8)) continue;
                    int ix = i % 4, iz = (i / 4) % 4, iy = i / 16;
                    var cc = bmin + new Vector3((ix + 0.5f) * cell.x, (iy + 0.5f) * cell.y, (iz + 0.5f) * cell.z);
                    float dd = (cc - med).sqrMagnitude;
                    if (dd < bd) { bd = dd; bi = i; }
                }
                // v4.8.5c: 区域生长 — 从冠军格 BFS 吸收相邻格(≥35%冠军计数)再取中位,
                // 均匀体(甲胄)无主格, 单格中位心会偏半身(实测偏差33); 长到全身后中位数≈体心
                int maxc2 = cnt[bi];
                var take = new bool[64];
                var stk = new List<int>(); stk.Add(bi); take[bi] = true;
                while (stk.Count > 0)
                {
                    int c = stk[stk.Count - 1]; stk.RemoveAt(stk.Count - 1);
                    int ix = c % 4, iz = (c / 4) % 4, iy = c / 16;
                    int n0 = ix > 0 ? c - 1 : -1, n1 = ix < 3 ? c + 1 : -1;
                    int n2 = iy > 0 ? c - 16 : -1, n3 = iy < 3 ? c + 16 : -1;
                    int n4 = iz > 0 ? c - 4 : -1, n5 = iz < 3 ? c + 4 : -1;
                    if (n0 >= 0 && !take[n0] && cnt[n0] >= maxc2 * 35 / 100) { take[n0] = true; stk.Add(n0); }
                    if (n1 >= 0 && !take[n1] && cnt[n1] >= maxc2 * 35 / 100) { take[n1] = true; stk.Add(n1); }
                    if (n2 >= 0 && !take[n2] && cnt[n2] >= maxc2 * 35 / 100) { take[n2] = true; stk.Add(n2); }
                    if (n3 >= 0 && !take[n3] && cnt[n3] >= maxc2 * 35 / 100) { take[n3] = true; stk.Add(n3); }
                    if (n4 >= 0 && !take[n4] && cnt[n4] >= maxc2 * 35 / 100) { take[n4] = true; stk.Add(n4); }
                    if (n5 >= 0 && !take[n5] && cnt[n5] >= maxc2 * 35 / 100) { take[n5] = true; stk.Add(n5); }
                }
                // v4.8.5b: 定心用生长区点的【逐轴中位数】而非质心 —
                // 粗格(跨度/4, 可达22万单位)会混入少量格内垃圾(实测2%), 质心被拽偏5270;
                // 区内真身占绝对多数时中位数天然落在真身上
                var cxs = new List<float>(); var cys = new List<float>(); var czs = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    int cx = Mathf.Clamp((int)((vs[i].x - bmin.x) / cell.x), 0, 3);
                    int cy = Mathf.Clamp((int)((vs[i].y - bmin.y) / cell.y), 0, 3);
                    int cz = Mathf.Clamp((int)((vs[i].z - bmin.z) / cell.z), 0, 3);
                    if (take[(cy * 4 + cz) * 4 + cx])
                    { cxs.Add(vs[i].x); cys.Add(vs[i].y); czs.Add(vs[i].z); }
                }
                int m = cxs.Count;
                if (m >= 8)
                {
                    cxs.Sort(); cys.Sort(); czs.Sort();
                    cen = new Vector3(cxs[m / 2], cys[m / 2], czs[m / 2]);   // 生长区中位心 = 真身锚心
                }
            }
        }
        // b) 距离断崖截尾
        var ds = new float[n];
        for (int i = 0; i < n; i++) ds[i] = (vs[i] - cen).magnitude;
        System.Array.Sort(ds);
        if (n >= 24 && depth < 2)
        {
            float best = 5f;                       // 相邻比值>5 = 断崖(连续云约1.0~1.5)
            int lo = Mathf.Max(n / 20, 7), hi = n - 2;
            int cut = -1;
            for (int k = lo; k < hi; k++)
            {
                float ratio = ds[k + 1] / Mathf.Max(ds[k], 1e-5f);
                if (ratio > best) { best = ratio; cut = k; }
            }
            if (cut >= lo)
            {
                var vs2 = new List<Vector3>(cut + 1);
                for (int i = 0; i < n; i++)
                    if ((vs[i] - cen).magnitude <= ds[cut]) vs2.Add(vs[i]);
                if (vs2.Count >= 8 && vs2.Count < n) { RobustFrame(vs2.ToArray(), ref bc, ref ext, depth + 1); return; }
            }
        }
        float r90 = ds[Mathf.Min(n - 1, n * 9 / 10)];
        bc = cen;
        ext = Mathf.Max(r90 * 2.2f, 1e-3f);
    }

    // v4.8.4: 只用【被面表引用】的顶点取景 — ski 里未被面引用的锚点/辅助顶点
    // (hea_00 实测约1/3顶点离群)会把中位中心与p90一起带偏 → "脸又小又偏上";
    // 引用顶点<8 时退回全顶点(纯点云/空面表场景)
    static void FrameReferenced(Vector3[] vs, List<int> tris, ref Vector3 bc, ref float ext)
    {
        var mark = new bool[vs.Length];
        int used = 0;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            if (a >= 0 && a < vs.Length && !mark[a]) { mark[a] = true; used++; }
            if (b >= 0 && b < vs.Length && !mark[b]) { mark[b] = true; used++; }
            if (c >= 0 && c < vs.Length && !mark[c]) { mark[c] = true; used++; }
        }
        if (used >= 8)
        {
            var rv = new Vector3[used]; int k = 0;
            for (int i = 0; i < vs.Length; i++) if (mark[i]) rv[k++] = vs[i];
            RobustFrame(rv, ref bc, ref ext);
        }
        else RobustFrame(vs, ref bc, ref ext);
    }

    static System.Collections.IEnumerator LoadShowCo(string fileName)
    {
        byte[] d; string err = "";
        // v4.8.7: 本地全量层优先 — 本地 pak_out 直读(同步), 缺失才回落 SA/外部目录
        d = null;
        if (localActorDir != null)
        {
            try
            {
                var lp = Path.Combine(localActorDir, fileName);
                if (File.Exists(lp)) d = File.ReadAllBytes(lp);
            }
            catch (Exception) { }
        }
        if (d == null && useAsset)
        {
            using (var wr = UnityWebRequest.Get(Application.streamingAssetsPath + "/creature/actor/" + fileName))
            {
                yield return wr.SendWebRequest();
                if (wr.result != UnityWebRequest.Result.Success) { d = null; err = wr.error; }
                else d = wr.downloadHandler.data;
            }
        }
        else if (d == null)
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

            Vector3 bc = Vector3.zero; float ext = 1f;
            FrameReferenced(vs, sd.tris, ref bc, ref ext); // v4.8.4: 面表引用顶点取景, 锚点顶点不再劫持
            _curS0 = ext > 1e-5f ? FitFor(name) / ext : 1f;
            _curBC = bc;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localRotation = Quaternion.Euler(ModelRot);
            ApplyFrame();
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

            line3 = string.Format("[v4.8.7] {0} 转{1:F0} | 骨架={2} ski:{3}B | 贴图=...",
                sd.fmt, ModelRot.x, sk != null ? tag + ":" + sk.srcName : tag, d.Length);

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
            line3 = "[v4.8.7] " + name + " 解析失败: " + e.Message + " (◀▶手动翻页)";
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
        }
    }

    static void ShowActData(string name, byte[] d, string err)
    {
        try
        {
            if (d == null) throw new Exception("act 读取失败: " + err);
            // v4.8.8: CRT容器真网格优先, 旧点云扫描兜底; v4.8.9: 回落原因诊断
            _actDiag = "";
            ActMeshData am = null;
            try { am = ParseActContainer(d); } catch (Exception) { am = null; }
            bool real = am != null && am.real;
            if (!real)
            {
                string why = _actDiag;
                try { am = ParseActMesh(d); } catch (Exception) { am = null; }
                if (am != null && !string.IsNullOrEmpty(why)) am.fmt = "v4.8.7 ACT点云[容器败:" + why + "]";
            }
            if (am == null) throw new Exception("act 解析失败(容器/点云均未命中)");

            if (real)
            {
                // ---- v4.8.8 真网格渲染 (与 ski 路径同管线) ----
                var vs = am.rverts.ToArray();
                if (vs.Length > 65534) throw new Exception("顶点超16位索引上限 " + vs.Length);
                var mesh = new Mesh();
                mesh.vertices = vs;
                mesh.uv = am.ruvs.Count >= vs.Length ? am.ruvs.ToArray() : new Vector2[vs.Length];
                mesh.triangles = am.rtris.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                Vector3 mn = vs[0], mx = vs[0];
                for (int i = 1; i < vs.Length; i++)
                {
                    if (vs[i].x < mn.x) mn.x = vs[i].x; if (vs[i].x > mx.x) mx.x = vs[i].x;
                    if (vs[i].y < mn.y) mn.y = vs[i].y; if (vs[i].y > mx.y) mx.y = vs[i].y;
                    if (vs[i].z < mn.z) mn.z = vs[i].z; if (vs[i].z > mx.z) mx.z = vs[i].z;
                }
                Vector3 bc = (mn + mx) * 0.5f;
                float ext = Mathf.Max(mx.x - mn.x, Mathf.Max(mx.y - mn.y, mx.z - mn.z));
                if (ext < 1e-4f) ext = 1f;
                _curS0 = FitFor(name) / ext;
                _curBC = bc;

                if (curModel != null) UnityEngine.Object.Destroy(curModel);
                curModel = new GameObject(name);
                curModel.transform.SetParent(pivot.transform, false);
                curModel.transform.localRotation = Quaternion.Euler(ModelRot);
                ApplyFrame();
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

                line3 = "[v4.8.9] " + am.fmt + " 转" + ModelRot.x.ToString("F0") + " | 贴图=...";
                string lw2 = name.ToLower();
                if (lw2.StartsWith("iw") && (lw2.Contains("dao") || lw2.Contains("gong") || lw2.Contains("jian") ||
                    lw2.Contains("qiang") || lw2.Contains("fu") || lw2.Contains("bang")))
                    line3 += " [武器(坐骑挂载)]";
                if (hostPv != null)
                    hostPv.StartCoroutine(LoadTexCoAct(name, am.anchors, mat));
                return;
            }

            // ---- 旧点云路径 (兜底) ----
            float extRaw = 0f;                             // v4.8.3: 原始最大坐标(诊断用)
            foreach (var pv in am.pts) extRaw = Mathf.Max(extRaw, Mathf.Max(Mathf.Abs(pv.x), Mathf.Max(Mathf.Abs(pv.y), Mathf.Abs(pv.z))));
            Vector3 bc2 = Vector3.zero; float ext2 = 2f;
            var vsA = am.pts.ToArray();
            RobustFrame(vsA, ref bc2, ref ext2);           // v4.8.5: 最密格定心+断崖截尾(先取景后建网格)
            Mesh pmesh = BuildPointMesh(am.pts, ext2);     // v4.8.5: 点片大小用稳健ext
            _curS0 = ext2 > 1e-5f ? FitFor(name) / ext2 : 1f;
            _curBC = bc2;

            if (curModel != null) UnityEngine.Object.Destroy(curModel);
            curModel = new GameObject(name);
            curModel.transform.SetParent(pivot.transform, false);
            curModel.transform.localRotation = Quaternion.Euler(ModelRot);
            ApplyFrame();
            curModel.AddComponent<MeshFilter>().sharedMesh = pmesh;
            var mr2 = curModel.AddComponent<MeshRenderer>();

            Shader sh2 = Shader.Find("Unlit/Color");
            if (sh2 == null) sh2 = Shader.Find("Legacy Shaders/VertexLit");
            if (sh2 == null) sh2 = Shader.Find("Mobile/VertexLit");
            if (sh2 == null) sh2 = Shader.Find("Legacy Shaders/Diffuse");
            if (sh2 == null) throw new Exception("无可用Shader(构建裁剪)");
            var mat2 = new Material(sh2);
            mat2.color = new Color(0.62f, 0.70f, 0.82f);
            mr2.sharedMaterial = mat2;

            line3 = "[v4.8.9] " + am.fmt + " 转" + ModelRot.x.ToString("F0") + " | 贴图=...";
            // v4.8.9: 怪物/坐骑包里的武器模型标注 (用户实测线索: iw0401dao_04/iwgong01 类)
            string lw = name.ToLower();
            if (lw.StartsWith("iw") && (lw.Contains("dao") || lw.Contains("gong") || lw.Contains("jian") ||
                lw.Contains("qiang") || lw.Contains("fu") || lw.Contains("bang") || lw.Contains("qiang")))
                line3 += " [武器(坐骑挂载)]";
            if (hostPv != null)
                hostPv.StartCoroutine(LoadTexCoAct(name, am.anchors, mat2));
        }
        catch (Exception e)
        {
            line3 = "[v4.8.9] " + name + " 解析失败: " + e.Message + " (◀▶手动翻页)";
            if (curModel != null) { UnityEngine.Object.Destroy(curModel); curModel = null; }
        }
    }

    // 点云 → 每点一个小面向相机的双三角片
    static Mesh BuildPointMesh(List<Vector3> pts, float extR)   // v4.8.5: 点片大小改用稳健ext
    {
        // v4.8.3: 封顶16383点 → 65532顶点, 硬性低于16位索引65535上限(只用真机已验证的老API)
        int N = Math.Min(pts.Count, 16383);
        // v4.8.5: 原来用 raw 包围盒(垃圾点撑到94万 → 点片5683单位 → 单quad糊屏);
        // 改用稳健 ext 定片大小, 并夹紧上限
        float s = Mathf.Clamp(extR * 0.006f, 0.02f, 50f);
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
        // v4.8.9: 池未命中 → 全盘索引兜底 (武器/怪物引用他包贴图的实测缺口)
        if (TexIndexLookup(c + ".dds") != null) return c + ".dds";
        if (TexIndexLookup(c + ".png") != null) return c + ".png";
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
        // v4.8.4: 不锚定行首 — iw0401dao 等怪物武器名也能命中 dao→sword 词根
        var m = Regex.Match(name, @"(\d)(\d?)(dao|zhan|jian|feng|shu|gong|qiang)");
        if (m.Success)
        {
            string tier = m.Groups[1].Value;
            string fam = m.Groups[3].Value;
            string word = fam == "feng" ? "axe" : fam == "shu" ? "stick"
                        : fam == "gong" ? "bow" : fam == "qiang" ? "spear" : "sword";
            // v4.8.4: 跨阶兜底 — 贴图库常缺高阶同名(6zhan01 无 a6sword 但有 a5sword);
            // 先本阶, 再 a1..a9 全阶扫描
            var tiers = new List<string> { tier };
            for (int t = 1; t <= 9; t++) { string ts = t.ToString(); if (ts != tier) tiers.Add(ts); }
            foreach (var t2 in tiers)
            {
                string[] cands = { "a" + t2 + word, "a" + t2 + word + "01", t2 + word, t2 + word + "01" };
                foreach (var c in cands) { p = TryTex(c); if (p != null) return p; }
            }
        }
        if (name.Contains("chibang"))                  // v4.8.3: 翅膀贴图兜底(0_chibang05→a_6_chibang)
        {
            string[] wch = { "a_6_chibang", "a6_chibang", "a_1_chibang", "a1_chibang", "chibang", "a_6_chibang01" };
            foreach (var wc in wch) { p = TryTex(wc); if (p != null) return p; }
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
            if (_texPool == null) _texPool = TexPoolNames();   // v4.8.7: 合并池(本地+SA)一次IO缓存
            if (_texSample == null)
            {
                var tsb = new System.Text.StringBuilder();
                for (int i = 0; i < _texPool.Length && i < 6; i++) { if (i > 0) tsb.Append(','); tsb.Append(_texPool[i]); }
                _texSample = _texPool.Length == 0 ? "(目录空)" : tsb.ToString();
            }
            line3 += " ✗未命中(池" + _texPool.Length + "个[" + _texSample + "]" + TexFamNote(skiName) + ")";   // v4.8.6
            yield break;
        }
        if (texCache.TryGetValue(p, out var tc) && tc != null)
        {
            if (mat != null) { mat.mainTexture = tc; mat.color = Color.white; }
            line3 += " ✓(缓存)";
            yield break;
        }
        byte[] raw = null;
        // v4.8.7: 本地全量层优先 — pak_out 直读(同步IO, 快于 UnityWebRequest; 每读try-catch不炸)
        if (localTexDir != null && !string.IsNullOrEmpty(p))
        {
            try { var lp = Path.Combine(localTexDir, p); if (File.Exists(lp)) raw = File.ReadAllBytes(lp); } catch (Exception) { }
        }
        if (raw == null && useAsset)
        {
            using (var wr = UnityWebRequest.Get(Application.streamingAssetsPath + "/creature/texture/" + p))
            {
                yield return wr.SendWebRequest();
                if (wr.result == UnityWebRequest.Result.Success) raw = wr.downloadHandler.data;
            }
        }
        else if (raw == null)
        {
            try { raw = ReadTexFile(p); } catch (Exception) { raw = null; }
        }
        // v4.8.9: 跨包反查 — 常规目录未命中 → TexIndex 全盘文件名→路径索引
        string idxPath = null;
        if (raw == null && !string.IsNullOrEmpty(p))
        {
            idxPath = TexIndexLookup(p);
            if (idxPath != null)
            {
                try { raw = File.ReadAllBytes(idxPath); } catch (Exception) { raw = null; idxPath = null; }
            }
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
            line3 += " ✓" + p + (idxPath != null ? "(跨包)" : "");
        }
        else if (raw == null)
            line3 += " ✗未找到:" + p + TexIdxNote();                    // v4.8.9: 文件不存在(此前误报"解码失败")
        else
            line3 += " ✗解码失败:" + p + (DdsV4.LastErr != null ? "[" + DdsV4.LastErr + "]" : "[过短" + raw.Length + "B]");
    }

    // ============ v4.8.9: TexIndex — pak 根目录全盘文件名→路径索引 (懒建一次) ============
    static Dictionary<string, string> _texIdx;
    static int _texIdxN = -1;                                          // -1=未建
    static string _texIdxRoots = "";
    static void EnsureTexIndex()
    {
        if (_texIdxN >= 0) return;
        _texIdx = new Dictionary<string, string>();
        _texIdxN = 0;
        try
        {
            var roots = new List<string>();
            string pd = Application.persistentDataPath;
            string[] cands = {
                localTexDir, localActorDir,
                "/storage/emulated/0/Game/游戏端/pak_out",
                "/storage/emulated/0/Game/pak_out",
                "/storage/emulated/0/Game/游戏端/pak_out2",
                pd + "/pak_out",
            };
            foreach (var c in cands)
            {
                if (string.IsNullOrEmpty(c) || !Directory.Exists(c)) continue;
                string r = c;                                          // 目录在 pak_out/creature/xxx → 根取 pak_out
                string p1 = Path.GetDirectoryName(c.TrimEnd('/'));
                if (p1 != null && Path.GetFileName(p1) == "creature") r = p1;
                if (!roots.Contains(r)) roots.Add(r);
            }
            foreach (var r in roots)
            {
                try { ScanTexDir(r, r, 0); _texIdxRoots += (_texIdxRoots.Length > 0 ? "," : "") + Path.GetFileName(r); }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }
    static void ScanTexDir(string root, string dir, int depth)
    {
        if (_texIdxN > 60000 || depth > 4) return;                     // 上限: 6万文件/4层深
        try
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                string ln = Path.GetFileName(f).ToLower();
                if (!ln.EndsWith(".dds") && !ln.EndsWith(".png")) continue;
                if (!_texIdx.ContainsKey(ln)) { _texIdx[ln] = f; _texIdxN++; }
            }
            foreach (var sub in Directory.GetDirectories(dir)) ScanTexDir(root, sub, depth + 1);
        }
        catch (Exception) { }
    }
    static string TexIndexLookup(string name)
    {
        try { EnsureTexIndex(); } catch (Exception) { return null; }
        if (_texIdx == null || _texIdxN == 0) return null;
        string v;
        return _texIdx.TryGetValue(name.ToLower(), out v) ? v : null;
    }
    static string TexIdxNote()
    {
        try { EnsureTexIndex(); } catch (Exception) { }
        return "(全盘索引" + _texIdxN + "文件" + (_texIdxRoots.Length > 0 ? "[" + _texIdxRoots + "]" : "") + ")";
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
        Rect rName = new Rect(196, yb, 648, 48);
        if (catI == 0 || catI == 1)   // v4.8.3: 武器分类名字条=跳族按钮
        {
            if (GUI.Button(rName, curSkiList.Count == 0 ? "(本职业无此部件)"
                : string.Format("{0}/{1}  {2}  ▶下族", curIdx + 1, curSkiList.Count, Path.GetFileNameWithoutExtension(curSki)), small))
                JumpNextFam();
        }
        else GUI.Label(rName, curSkiList.Count == 0
            ? "(本职业无此部件) 点右上「职业」换人 / 点左侧其他分类"
            : string.Format("{0}/{1}  {2}", curIdx + 1, curSkiList.Count, Path.GetFileNameWithoutExtension(curSki)), small);
        if (GUI.Button(new Rect(850, yb, 60, 48), "转", btn)) { _rotIdx = (_rotIdx + 1) & 3; ModelRot = new Vector3(_rotAng[_rotIdx], 0f, 0f); ShowCurrent(); }  // v4.8.3: 朝向校准
        if (GUI.Button(new Rect(916, yb, 60, 48), "＋", btn)) _zoom = Mathf.Clamp(_zoom * 1.2f, 0.2f, 12f);   // v4.8.3: 即时缩放不重载
        if (GUI.Button(new Rect(982, yb, 60, 48), "－", btn)) _zoom = Mathf.Clamp(_zoom / 1.2f, 0.2f, 12f);

        // 拖动旋转
        var e = Event.current;
        if (e.type == EventType.MouseDrag && e.button == 0 && Input.touchCount < 2) { rotY += e.delta.x * 0.45f; ApplyRot(); e.Use(); }   // v4.8.3: 双指时让位缩放/平移
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
    public static string LastErr = null;   // v4.8.8: 解码失败原因 (诊断输出)

    public static Texture2D LoadFromMemory(byte[] raw, string name)
    {
        LastErr = null;
        try { return Inner(raw, name); }
        catch (Exception e) { LastErr = "异常:" + e.Message; return null; }
    }

    static Texture2D Inner(byte[] raw, string name)
    {
        if (raw == null) { LastErr = "无数据"; return null; }
        if (raw.Length < 64) { LastErr = "过短" + raw.Length + "B"; return null; }   // v4.8.9: 抽包占位文件定性
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
            // v4.8.8: DX10 扩展头 (fourcc="DX10", DXGI格式在128偏移, 数据从148起)
            if (fourcc == 0x30315844 && raw.Length >= 148)
            {
                int dxgi = BitConverter.ToInt32(raw, 128);
                dataOff = 148;
                if (dxgi >= 70 && dxgi <= 72) fourcc = 0x31545844;        // BC1
                else if (dxgi >= 73 && dxgi <= 75) fourcc = 0x33545844;   // BC2
                else if (dxgi >= 76 && dxgi <= 78) fourcc = 0x35545844;   // BC3
                else if (dxgi >= 79 && dxgi <= 81) fourcc = 0x31495441;   // BC4→ATI1
                else if (dxgi >= 82 && dxgi <= 84) fourcc = 0x32495441;   // BC5→ATI2
                else { LastErr = "DX10 dxgi=" + dxgi; return null; }
            }
            if (fourcc == 0x31545844) return DecodeDXT(raw, dataOff, width, height, false, name, 0, false);
            if (fourcc == 0x33545844) return DecodeDXT(raw, dataOff, width, height, true, name, 8, false);
            if (fourcc == 0x35545844) return DecodeDXT(raw, dataOff, width, height, true, name, 8, true);
            // v4.8.8: BC4/BC5 (ATI1/ATI2 — 原生fourcc或DX10映射)
            if (fourcc == 0x31495441) return DecodeBC45(raw, dataOff, width, height, false, name);
            if (fourcc == 0x32495441) return DecodeBC45(raw, dataOff, width, height, true, name);
            LastErr = "fourcc=" + fourcc.ToString("X8");
            return null;
        }
        bool hasAlpha = (pfFlags & 0x1) != 0 || (pfFlags & 0x2) != 0;
        return DecodeRaw(raw, dataOff, width, height, hasAlpha, name);
    }

    // v4.8.8: BC4/BC5 (ATI1/ATI2) — 每块8B单通道/16B双通道, 2端点+6bit索引调色板
    static Texture2D DecodeBC45(byte[] raw, int off, int w, int h, bool two, string name)
    {
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192) { LastErr = "BC45尺寸异常"; return null; }
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        int bs = two ? 16 : 8;
        if (off + (long)bw * bh * bs > raw.Length) { LastErr = "BC45数据不足"; return null; }
        var px = new Color32[w * h];
        var pal = new float[8];
        var blk = new float[4, 4, 2];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int bo = off + (by * bw + bx) * bs;
                int cx = Math.Min(4, w - bx * 4), cy = Math.Min(4, h - by * 4);
                for (int ch = 0; ch < (two ? 2 : 1); ch++)
                {
                    int c0 = raw[bo + ch * 8], c1 = raw[bo + ch * 8 + 1];
                    ulong bits = 0;
                    for (int i = 0; i < 6; i++) bits |= (ulong)raw[bo + ch * 8 + 2 + i] << (8 * i);
                    pal[0] = c0; pal[1] = c1;
                    if (c0 > c1) { for (int i = 2; i < 8; i++) pal[i] = ((8 - i) * c0 + (i - 1) * c1) / 7f; }
                    else { for (int i = 2; i < 6; i++) pal[i] = ((6 - i) * c0 + (i - 2) * c1) / 5f; pal[6] = 0f; pal[7] = 255f; }
                    for (int y = 0; y < cy; y++)
                        for (int x = 0; x < cx; x++)
                            blk[y, x, ch] = pal[(int)((bits >> (3 * (y * 4 + x))) & 7)];
                }
                for (int y = 0; y < cy; y++)
                    for (int x = 0; x < cx; x++)
                    {
                        int pi = (by * 4 + y) * w + bx * 4 + x;
                        if (two)
                        {
                            byte r = (byte)Mathf.Clamp(blk[y, x, 0], 0f, 255f);
                            byte g = (byte)Mathf.Clamp(blk[y, x, 1], 0f, 255f);
                            px[pi] = new Color32(r, g, 255, 255);
                        }
                        else
                        {
                            byte g2 = (byte)Mathf.Clamp(blk[y, x, 0], 0f, 255f);
                            px[pi] = new Color32(g2, g2, g2, 255);
                        }
                    }
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(); tex.name = name;
        return tex;
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
