# 美术资源替换指南

当前游戏使用程序化生成的**矢量剪影占位图**，每个兵种一个 SVG，渲染时按阵营（蓝/红）染色。
替换成正式美术**不需要改任何代码**。

## 目录约定

```
art/units/<兵种id>.svg      # 当前使用的占位图标
art/units/<兵种id>.png      # 可选：优先于 svg 加载（若同时存在两个，先找 svg）
```

兵种 id 见 `data/units.json` 的 `id` 字段（如 militia / skeleton / guan_yu / titan）。

## 替换方式（三步）

1. 把你的图按兵种 id 命名，放进 `art/units/`，例如 `knight.png`
2. 在 Godot 里执行一次导入（编辑器打开工程会自动导入；命令行：`godot --headless --path . --import`）
3. 重新导出即可

## 美术规格建议

| 项 | 建议 |
|---|---|
| 画布 | 正方形，建议 128×128 或 256×256 |
| 背景 | 透明 |
| 主体 | **白色/浅灰**（渲染时会被阵营色染色）；如果要保留原色，需要在 `BattleView.DrawUnit` 里把 `tint` 改成 `Colors.White` |
| 朝向 | 默认朝右，左方队伍原样绘制，右方队伍自动水平翻转 |
| 锚点 | 图像中心对齐单位坐标 |

## 想换成骨骼动画 / 序列帧？

渲染入口只有一个方法：`src/Game/BattleView.cs` 的 `DrawUnit(SimUnit u)`。
它拿到的是模拟层的单位对象（位置、血量、朝向、是否正在攻击、是否受击）。
把 `DrawTextureRect` 换成 `AnimatedSprite2D` 或 Spine 的绘制调用即可，
模拟层（`src/Core/`）完全不需要改动——逻辑与表现是严格分离的。

## 攻击/受击/移动的状态来源

| 状态 | 字段 |
|---|---|
| 正在攻击 | `u.attackCd`（刚出手时接近攻击间隔） |
| 受击闪烁 | `u.hitUntil > sim.time` |
| 移动中 | 有目标但距离大于射程 |
| 被减速 | `u.slowUntil > sim.time` |
| 有护盾 | `u.shield > 0` |
