# OpenCSG.NET 扩展设计文档

## 元信息

| 字段 | 值 |
|------|-----|
| 创建日期 | 2026-08-02 |
| 版本 | 1.0 |
| 状态 | 设计完成，待实施 |
| 相关文档 | `2026-07-31-opencsg-net-integration-design.md`（上游合并设计） |
| 上游需求 | 转子包装箱参数化建模方案（见技术分析报告 + 正式需求文档） |

---

## 一、概述

### 1.1 背景

OpenCSG.NET 当前是一个纯 C# CSG 几何库，提供基本体（Cube/Sphere/Cylinder）的布尔运算（并/差/交）和 STL 导出。上游需求（转子包装箱参数化建模）要求扩展能力以支持更复杂的几何表达和跨平台迁移。

### 1.2 目标

在不破坏现有 API 的前提下，为 OpenCSG.NET 新增三项核心能力：

1. **扩展 CSG 图元**：新增 `Extrude`（截面拉伸）、`Wedge`（楔形）、参数化截面库（H型钢/槽钢/方管/梯形/胶囊形/L形）
2. **CSG 树序列化**：CsgNode 树与 JSON 的双向转换（配方模式），支撑调试审查和跨平台迁移
3. **CsgEvaluator 求值器**：将 CsgNode 树递归求值为现有 `Solid` 实体

### 1.3 非目标（不在本设计范围）

以下能力由上层的参数化 CAD 系统独立实现，不进入 OpenCSG.NET 内核：

- 参数计算引擎（用户 JSON → 零件尺寸）
- 参数校验引擎（边界条件检测）
- 锚点系统（零件间相对定位）
- 装配体编排
- 2D 投影 / DWG 输出
- CAD 平台适配器（浩辰CAD / AutoCAD等）

---

## 二、架构路线

采用 **路线 3：纯数据 CsgNode 树 + 独立求值器**。

### 2.1 为什么是路线 3

| 对比维度 | 路线 1（扩展现有API） | 路线 2（重构为树优先） | 路线 3（独立 CsgNode 树） |
|----------|:---:|:---:|:---:|
| 现有 API 影响 | 零 | 破坏性 | 零 |
| 序列化自然度 | 需桥接层 | 天然支持 | 天然支持（record类型） |
| 与上游架构一致性 | 中 | 高 | 高 |
| 迁移成本 | 低 | 高 | 无 |
| 新增行数 | ~800 | ~1200（含重构） | ~950 |

### 2.2 核心原则

1. **零侵入**：不修改 `Solid.cs`、`Solids.cs`、`Vector.cs` 及任何现有文件的公开 API
2. **独立文件**：所有新代码放入 `CsgNode.cs`、`CsgEvaluator.cs`、`CsgSerialization.cs`、`Profiles.cs` 四个新文件
3. **record 类型**：CsgNode 全部子类型使用 C# `record`——不可变、值相等、JSON 序列化友好
4. **不缓存结果**：CsgEvaluator 每次调用重新求值。缓存留给上层

---

## 三、CsgNode 树结构

### 3.1 节点类型体系

```
CsgNode (abstract record)
├── 基本图元（叶节点）
│   ├── BoxNode(Center, Size)
│   ├── SphereNode(Center, Radius, Resolution)
│   ├── CylinderNode(Center, Radius, Height, Resolution)
│   ├── ConeNode(Center, RadiusBottom, RadiusTop, Height, Resolution)
│   ├── ExtrudeNode(Profile2D, Height, Center)      -- 新增
│   └── WedgeNode(Center, Size)                       -- 新增
│
├── 布尔运算（组合节点）
│   ├── UnionNode(Left, Right)
│   ├── SubtractNode(Left, Right)
│   └── IntersectNode(Left, Right)
│
└── 变换包装
    └── TransformNode(Child, Position, Rotation)
```

### 3.2 节点属性定义

所有叶节点携带自身定位信息（`Center` 和 `Rotation`），布尔运算节点只携带左右子树。`TransformNode` 作为可选包装，当需要在布尔运算结果上再施加变换时使用。

```csharp
// 基类
public abstract record CsgNode;

// 基本图元
public record BoxNode(Vector3D Center, Vector3D Size) : CsgNode;
public record SphereNode(Vector3D Center, double Radius, int Resolution = 12) : CsgNode;
public record CylinderNode(Vector3D Center, double Radius, double Height, int Resolution = 32) : CsgNode;
public record ConeNode(Vector3D Center, double RadiusBottom, double RadiusTop, double Height, int Resolution = 32) : CsgNode;
public record ExtrudeNode(Profile2D Profile, double Height, Vector3D Center) : CsgNode;
public record WedgeNode(Vector3D Center, Vector3D Size) : CsgNode;

// 布尔运算
public record UnionNode(CsgNode Left, CsgNode Right) : CsgNode;
public record SubtractNode(CsgNode Left, CsgNode Right) : CsgNode;
public record IntersectNode(CsgNode Left, CsgNode Right) : CsgNode;

// 变换包装
public record TransformNode(CsgNode Child, Vector3D Position, Vector3D Rotation) : CsgNode;
```

### 3.3 设计约束

- 布尔运算限制为**二元**（而非可变参数），多级操作用嵌套表达
- 变换不嵌入叶节点自身，`TransformNode` 作为独立包装——叶节点自身 `Center` 是建模坐标系定位
- `Rotation` 使用欧拉角（`(rotX, rotY, rotZ)`，单位度），与现有 `Solid.RotateX/Y/Z` 保持一致

---

## 四、Profile2D 截面系统

### 4.1 设计理念

截面系统负责描述 2D 形状的**配方**（参数），不存储展开后的多边形顶点。求值阶段由 CsgEvaluator 根据参数实时展开。

截面不自带 2D CSG 布尔运算能力。切角、开孔等操作留给上层装配体系统用 `SubtractNode` 处理。

### 4.2 截面类型

```csharp
public abstract record Profile2D;

// 矩形
public record RectangleProfile(double Width, double Height) : Profile2D;

// H型钢
public record HBeamProfile(double WebHeight, double FlangeWidth,
                            double WebThickness, double FlangeThickness) : Profile2D;

// 槽钢
public record ChannelProfile(double WebHeight, double FlangeWidth,
                              double WebThickness, double FlangeThickness) : Profile2D;

// 方管
public record SquareTubeProfile(double Width, double Height, double Thickness) : Profile2D;

// 梯形
public record TrapezoidProfile(double TopWidth, double BottomWidth, double Height) : Profile2D;

// 胶囊形（矩形 + 两端半圆）
public record CapsuleProfile(double RectWidth, double Radius) : Profile2D;

// L形
public record LShapeProfile(double Vertical, double Horizontal, double Thickness) : Profile2D;
```

### 4.3 静态工厂（便捷入口）

```csharp
public static class Profiles
{
    public static RectangleProfile Rectangle(double width, double height);
    public static HBeamProfile HBeam(double webHeight, double flangeWidth,
                                      double webThickness, double flangeThickness);
    public static ChannelProfile Channel(double webHeight, double flangeWidth,
                                          double webThickness, double flangeThickness);
    public static SquareTubeProfile SquareTube(double width, double height, double thickness);
    public static TrapezoidProfile Trapezoid(double topWidth, double bottomWidth, double height);
    public static CapsuleProfile Capsule(double rectWidth, double radius);
    public static LShapeProfile LShape(double vertical, double horizontal, double thickness);
}
```

### 4.4 需求覆盖

| 需求场景 | 对应截面 |
|----------|----------|
| 件1-4：H型钢底座 | `HBeamProfile` |
| 件5：槽钢 | `ChannelProfile` |
| 大方管/小方管（罩壳） | `SquareTubeProfile` |
| 加劲肋梯形板（件4/10/11） | `TrapezoidProfile` |
| L形口（件11起吊板） | `LShapeProfile` |
| 胶囊形减重孔（件12） | `CapsuleProfile` |
| 矩形板 | `RectangleProfile` |

---

## 五、CsgEvaluator 求值器

### 5.1 接口

```csharp
public static class CsgEvaluator
{
    /// <summary>将 CsgNode 树递归求值为 Solid 实体</summary>
    /// <returns>求值后的 Solid；求值失败时抛出 CsgEvaluationException</returns>
    public static Solid Evaluate(CsgNode node);

    /// <summary>批量求值，每个节点独立</summary>
    public static IReadOnlyList<Solid> EvaluateAll(IEnumerable<CsgNode> nodes);
}
```

### 5.2 求值映射

| CsgNode | 求值逻辑 |
|---------|----------|
| `BoxNode` | `Solids.Cube(Size, Center)` |
| `SphereNode` | `Solids.Sphere(Radius, Center)` |
| `CylinderNode` | `Solids.Cylinder(Radius, Height, Center)` |
| `ConeNode` | `Solids.Cylinder(RadiusBottom, RadiusTop, Height, Center)`（现有 Cylinder 已支持锥台） |
| `ExtrudeNode` | 将 Profile2D 展开为多边形顶点 → 三角化 → 生成上下底面 + 侧面三角形 → 组装为 Solid（新增算法） |
| `WedgeNode` | 直接构造 6 个 Polygon 面，组装为 Solid（新增算法） |
| `UnionNode` | `left.Union(right)` |
| `SubtractNode` | `left.Subtract(right)` |
| `IntersectNode` | `left.Intersect(right)` |
| `TransformNode` | 先求值 `Child`，再施加 `Translate(Position)` + `RotateX/Y/Z(Rotation)` |

### 5.3 Extrude 求值算法

1. **展开截面**：根据 `Profile2D` 参数，生成 2D 多边形顶点数组（外轮廓 + 可能的孔洞）
2. **三角化底面**：对每个闭合多边形做 ear-clipping 三角化，生成底面三角形
3. **生成顶面**：底面三角形顶点 Z 坐标偏移 `+Height`
4. **生成侧面**：对外轮廓的每条边，生成两个三角形（构成矩形侧面）
5. **组装 Solid**：合并上底面 + 下底面 + 所有侧面三角形为一组 `Polygon` 列表，构造 `Solid`
6. **平移至 Center**：对构造好的 Solid 施加 `Translate(Center)`

### 5.4 Wedge 求值算法

楔形 = 6 个面（1 底面矩形 + 1 顶面边 + 2 个三角形侧面 + 2 个矩形侧面 + 1 个矩形背面），直接构造 6 个 `Polygon` 组装为 `Solid`。

### 5.5 错误处理

求值失败时抛 `CsgEvaluationException`，包含节点类型和失败原因描述。不静默返回空 Solid。

```csharp
public class CsgEvaluationException : Exception
{
    public CsgNode FailedNode { get; }
    public CsgEvaluationException(string message, CsgNode node) : base(message)
        => FailedNode = node;
}
```

---

## 六、JSON 序列化

### 6.1 序列化格式

使用 `System.Text.Json` 多态序列化，生成自描述的 JSON 配方。`$type` 字段标识节点类型。

```json
{
  "$type": "UnionNode",
  "left": {
    "$type": "BoxNode",
    "center": { "x": 0, "y": 0, "z": 15 },
    "size": { "x": 800, "y": 300, "z": 30 }
  },
  "right": {
    "$type": "SubtractNode",
    "left": {
      "$type": "CylinderNode",
      "center": { "x": 100, "y": 150, "z": 0 },
      "radius": 10,
      "height": 30,
      "resolution": 32
    },
    "right": {
      "$type": "CylinderNode",
      "center": { "x": 300, "y": 150, "z": 0 },
      "radius": 10,
      "height": 30,
      "resolution": 32
    }
  }
}
```

### 6.2 API

```csharp
public static class CsgSerialization
{
    /// <summary>CsgNode → JSON 字符串</summary>
    public static string ToJson(CsgNode node, bool indented = true);

    /// <summary>JSON 字符串 → CsgNode</summary>
    public static CsgNode FromJson(string json);

    /// <summary>CsgNode 集合 → JSON 数组字符串</summary>
    public static string ToJson(IEnumerable<CsgNode> nodes, bool indented = true);

    /// <summary>JSON 数组字符串 → CsgNode 集合</summary>
    public static IReadOnlyList<CsgNode> FromJsonArray(string json);
}
```

### 6.3 技术选型

| 决策 | 理由 |
|------|------|
| `System.Text.Json`（通过 NuGet `System.Text.Json` 8.0+） | Microsoft 官方包，与 `netstandard2.0` 兼容，`[JsonDerivedType]` 支持多态 |
| `$type` 判别符 | 类名（`"BoxNode"`），通过 `[JsonDerivedType(typeof(BoxNode), "BoxNode")]` 注册 |
| 默认缩进输出 | 方便人工审查（调试场景，与技术报告要求一致） |
| `Vector3D` 序列化 | 自定义 `JsonConverter<Vector3D>` → `{"x":1,"y":2,"z":3}` |
| `Profile2D` 序列化 | 自定义 `JsonConverter<Profile2D>` → 多态输出包含 type 判别符和参数值 |

### 6.4 反序列化错误处理

遇到未知 `$type` 值时抛 `JsonException`，附带具体未知类型名。JSON 结构不完整时抛标准 `JsonException`，无额外包装。

### 6.5 新增依赖

唯一新增的 NuGet 依赖：`System.Text.Json`（版本 8.0+）。

- `netstandard2.0` 目标需要显式引用 NuGet 包
- `net8.0` 测试/示例项目无需额外引用（框架内置）
- 如未来严格零依赖为硬要求，可降级为手动字符串拼接（~200 行），但会丢失多态反序列化能力

---

## 七、文件组织

### 7.1 新增文件清单

```
src/OpenCSG.NET/
├── CsgNode.cs           -- 所有 CsgNode record 类型 + CsgEvaluationException
├── CsgEvaluator.cs      -- 递归求值器
├── CsgSerialization.cs  -- JSON 序列化/反序列化 + Vector3D/Profile2D JsonConverter
├── Profiles.cs          -- Profile2D 基类 + 参数化截面工厂
│
├── Solid.cs             -- 不变
├── Solids.cs            -- 不变
├── Vector.cs            -- 不变
├── Tree.cs              -- 不变
├── Polygon.cs           -- 不变
├── Plane.cs             -- 不变
├── Vertex.cs            -- 不变
├── Formats.cs           -- 不变
```

### 7.2 各文件职责与估算

| 文件 | 职责 | 行数估算 |
|------|------|:---:|
| `CsgNode.cs` | 10 个 CsgNode record 子类型 + `CsgNode` 抽象基类 + `CsgEvaluationException` | ~250 |
| `CsgEvaluator.cs` | `Evaluate()` 递归分发 + Extrude 三角化 + Wedge 构建 + Profile2D 展开逻辑 | ~350 |
| `CsgSerialization.cs` | `ToJson`/`FromJson` + `JsonSerializerOptions` 配置 + `Vector3D`/`Profile2D` JsonConverter | ~250 |
| `Profiles.cs` | 7 种截面 record + `Profiles` 静态工厂 | ~150 |
| **合计** | | **~1000** |

### 7.3 命名空间

所有新文件使用 `namespace Csg`，与现有代码保持一致。

### 7.4 现有文件修改

**零修改**（路线 3 保证）。如果 `Solid.cs` 中没有 `internal` 方法可以直接满足 Extrude/Wedge 所需的 Polygon 直接构造能力，可能需要在 `Solid.cs` 新增一个 `internal` 构造函数 `Solid(IEnumerable<Polygon>)`（现有代码已经接受 `List<Polygon>`）。

---

## 八、测试策略

### 8.1 测试文件计划

```
tests/OpenCSG.NET.Tests/
├── CsgNodeTest.cs         -- CsgNode 序列化往返测试
├── CsgEvaluatorTest.cs    -- 求值器正确性（黄金 STL 对比）
├── ExtrudeTest.cs         -- 各截面拉伸体形状正确性
├── WedgeTest.cs           -- 楔形形状正确性
└── ProfilesTest.cs        -- 截面顶点生成正确性
```

### 8.2 测试原则

- 沿用现有测试风格：NUnit + 黄金 STL 文件对比（`AssertAcceptedStl`）
- 每个新图元 + 每种新截面至少一个 STL 黄金测试
- CsgNode 序列化：构建树 → JSON → 反序列化 → 再序列化 → 断言 JSON 一致（往返测试）
- 求值稳定后，将黄金 STL 文件提交至 `tests/OpenCSG.NET.Tests/Results/`

---

## 九、与上层系统的接口约定

以下接口约定供上层参数化 CAD 系统消费，不在本次实现范围：

| 接口 | 消费方 | 说明 |
|------|--------|------|
| `CsgNode` 树构建 | 参数引擎 | 根据计算出的零件尺寸，构建 CsgNode 表达式树 |
| `CsgEvaluator.Evaluate()` | CSG 生成器 | 将树求值为 `Solid`，传递给 CAD 平台适配器 |
| `CsgSerialization.ToJson()` | 调试/审计 | 将树导出为 JSON 文本供审查 |
| `CsgSerialization.FromJson()` | 跨平台迁移 | 从 JSON 恢复 CsgNode 树，重新求值 |

上层系统不直接构造 `Solid`，而是通过 `CsgNode` 树 + `CsgEvaluator` 间接生成。

---

## 十、设计决策总结

| # | 决策 | 选项 | 最终选择 | 理由 |
|---|------|------|:---:|------|
| 1 | 定位边界 | A.纯内核 / B.参数化平台 / C.不变 | **A** | 与技术报告"CSG 树与 CAD API 解耦"原则一致 |
| 2 | 范围 | 三种能力全部 / 仅图元+序列化 / 仅图元 | **图元+序列化+求值器** | 用户确认：实现 1+2+3，锚点排除 |
| 3 | 截面方案 | A.纯参数化 / B.完整2D CSG / C.参数化+自定义多边形 | **A** | 纯参数化截面工厂，切角/开孔留给上层用 CsgNode SubtractNode |
| 4 | 锚点归属 | A.嵌入CSG节点 / B.独立装配层 | **B** | 保持内核纯粹，上层项目独立实现 |
| 5 | 序列化粒度 | A.配方 / B.网格 / C.两者 | **A** | 符合技术报告"CSG 树可审查为 JSON"定位 |
| 6 | 实现路线 | 1.渐进演进 / 2.树优先重构 / 3.独立CsgNode树 | **3** | 零破坏性、与技术报告架构一致、record 天然序列化友好 |
| 7 | Profile2D 2D CSG | 需要 / 不需要 | **不需要** | 切角/开孔留给上层用 CsgNode SubtractNode |
