# crt_pipeline — CRT 装备模型本地量产工具 (零依赖纯 Python 版)

从 `pak_out` 的 `.ski` 装备模型 + `role_*.act` 角色骨架，批量烘焙出带贴图的 OBJ 模型库。
（配方与服务器端 297 个全量量产完全一致，纯 Python 版与 numpy 版产出逐位一致。）

## 环境准备

** Termux 用户（重要）：不要用 pip 装 numpy！** 那会触发源码编译失败。
Termux 直接用预编译包（可选，只有"抽样渲染"功能需要）：

```bash
pkg install python python-numpy python-pillow
```

本工具核心功能**零依赖**——没有 numpy/pillow 也能完整量产（贴图直接复制 dds）。
装了 pillow 则自动把贴图转 PNG（推荐）。

电脑用户：
```bash
pip3 install pillow   # 可选
```

## 一条命令全量量产

```bash
python3 mass_produce.py \
    --actor /storage/emulated/0/Game/pak_out/creature/actor \
    --texture /storage/emulated/0/Game/pak_out/creature/texture \
    --out ./model_pack
```

跑完自动生成：

```
model_pack/
├── obj/       每个 ski 一个 .obj + .mtl（带 UV）
├── texture/   贴图 PNG
├── report.txt 成功/失败清单（失败的看这里）
└── (可选) sample_check.png
```

## 常用变体

```bash
# 先试 20 个看看效果（快）
python3 mass_produce.py --actor ... --texture ... --out ./test20 --limit 20

# 指定骨架（例如女角色/其他职业），不自动匹配
python3 mass_produce.py --actor ... --texture ... --out ./pack_f \
    --act /storage/emulated/0/Game/pak_out/creature/actor/role_wt_f_01.act

# T-pose（bind 姿势，做骨骼动画用）
python3 mass_produce.py --actor ... --texture ... --out ./pack_bind --frame 477

# 只跑某个职业（按文件名前缀）
python3 mass_produce.py --actor ... --texture ... --out ./pack_hj --prefix a_hj_
```

## 产出自检

```bash
python3 quick_check.py --pack ./model_pack --samples 8
```

会输出：OBJ 总数 / 坏文件数 / 贴图覆盖情况 + 抽样渲染拼图 `sample_check.png`。

## 装备 → 骨架自动匹配规则

`a_wt_m_glo_00.ski` → 取职业 `wt` + 性别 `m` → 找 `role_wt_m_*.act`。
找不到该职业骨架时回退到任一可用骨架（report.txt 里记 FALLBACK，
该职业专属装备形状会不准，需要正确骨架后重跑）。

## 已知配方要点（出问题时排查用）

1. 面装配 = 索引区每 3 个 u16 一组三角形（不是 strip！），大端 BE
2. 骨骼烘焙 = act 帧 pos+quat 直接当世界矩阵（R·L+t 加权求和），无需父子级联
3. 贴图锚名 = CRT_Mtl 块里第一个非 CRT 字符串，resolve 顺序：原名→剥尾缀→层后缀
4. 姿势帧：3736=自然站立（展示用）、477=T-pose（动画绑定用）
5. 模型坐标系 z-up，身高约 48 单位

## 文件说明

| 文件 | 作用 |
|---|---|
| mass_produce.py | 量产主脚本（CLI） |
| quick_check.py | 产出自检 + 抽样渲染 |
| act_parse.py | act 骨架解析（127 节点 Biped 格式，4577 帧） |
| ski_core.py | ski 解析（顶点/UV/索引/贴图锚） |
| render_obj.py | 纯 Python 软件渲染（预览用，无 OpenGL 依赖） |
