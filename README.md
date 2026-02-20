# ShaderExtends

为 tModLoader 提供 Shader Model 5.0 支持（顶点/像素/计算着色器），供其他 Mod 使用。

## 目标框架

- `.NET 8`

## API 使用说明

以下示例基于 `FCSShaderFactory` + `FNARenderContext` 的渲染流程。

### 1. 创建材质

使用 `FCSShaderFactory.CreateMaterial` 从 `.fcs` 资源创建材质实例：

```csharp
var material = FCSShaderFactory.CreateMaterial(Main.graphics.GraphicsDevice, "CompiledShaders/YourShader.fcs");
```

### 2. 设置参数

参数通过 `material.Parameters` 写入（会自动写入常量缓冲区）：

```csharp
material.Parameters["Time"].SetValue(totalSeconds);
material.Parameters["ScreenResolution"].SetValue(new Vector2(Main.screenWidth, Main.screenHeight));
```

### 3. 绑定纹理

使用 `SourceTexture` 设置自定义纹理槽：

```csharp
material.SourceTexture[1] = extraTexture;
```

### 4. 渲染流程

推荐的渲染流程：

```csharp
var context = new FNARenderContext(Main.graphics.GraphicsDevice);
graphicsDevice.SetRenderTarget(target);
context.Begin(
    destination: target,
    blendState: BlendState.Opaque,
    depthStencilState: DepthStencilState.None,
    rasterizerState: RasterizerState.CullNone);

material.Apply(context.GetFNARenderDriver());
context.Draw(sourceTexture, Vector2.Zero, Color.White);
context.End();
```

也可以将 `material` 直接传给 `Begin`，然后正常 `Draw/End`。

### 5. 可选：阴影缓冲区

如果着色器需要中间缓冲，可以确保尺寸：

```csharp
material.EnsureShadow(width, height);
```

## 主要类型

- `FCSShaderFactory`：创建 `IFCSMaterial`。
- `IFCSMaterial`：材质实例，管理参数和纹理。
- `FCSParameter`：参数写入入口。
- `FNARenderContext`：类似 `SpriteBatch` 的渲染上下文。
- `IFNARenderDriver`：底层渲染驱动抽象。
