using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;

namespace ShaderExtends.Base
{
    public class PostProcessSystem : ModSystem
    {
        public static bool EnablePostProcess = true;
        private static bool ShouldPrint = false;
        public static IFCSMaterial Material, Material2, Material3, Material4;
        public static FNARenderContext Context;
        public static SpriteBatch helperSpriteBatch;
        public static RenderTarget2D tempTarget, tempTarget2;
        public override void Load()
        {
        }
        public override void Unload()
        {
            Main.QueueMainThreadAction(()=>
            {
                RenderTargetPatch.MyFinalTarget?.Dispose();
                RenderTargetPatch.MyFinalTarget = null;
                tempTarget?.Dispose();
                tempTarget = null;
                tempTarget2?.Dispose();
                tempTarget2 = null;
            });
        }
        public override void PreDrawMapIconOverlay(IReadOnlyList<IMapLayer> layers, MapOverlayDrawContext mapOverlayDrawContext)
        {
            base.PreDrawMapIconOverlay(layers, mapOverlayDrawContext);
        }

        public override void PostSetupContent()
        {
            try
            {
                Main.QueueMainThreadAction(() =>
                {
                    Context = new FNARenderContext(Main.graphics?.GraphicsDevice);
                    Material = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/GaussHCS.fcs");
                    Material2 = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/AdvancedPostProcess.fcs");
                    Material3 = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/GaussVCS.fcs");
                    Material4 = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/Discard.fcs");
                    Material.Parameters["TextureWidth"].SetValue(Main.screenWidth);
                    Material3.Parameters["TextureHeight"].SetValue(Main.screenHeight);
                    Vector2 res = new Vector2(Main.spriteBatch.GraphicsDevice.Viewport.Width, Main.spriteBatch.GraphicsDevice.Viewport.Height);
                    Material2.Parameters["ScreenResolution"].SetValue(res);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"完整错误: {ex}");
            }
            base.PostSetupContent();
        }
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            LegacyGameInterfaceLayer layer = new("", () =>
            {
                BlitToScreen(Main.spriteBatch.GraphicsDevice);
                return true;
            }, InterfaceScaleType.None);
            int firstIndex = layers.FindIndex(layer => layer.Name == "Vanilla: Interface Logic 1");
            if (firstIndex != -1) layers.Insert(firstIndex, layer);
            base.ModifyInterfaceLayers(layers);
        }
        static float totalSeconds = 0;
        private static void BlitToScreen(GraphicsDevice device)
        {
            int w = device.PresentationParameters.BackBufferWidth;
            int h = device.PresentationParameters.BackBufferHeight;

            if (tempTarget == null || tempTarget.Width != w || tempTarget.Height != h)
            {
                tempTarget?.Dispose();
                tempTarget = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            }
            if (tempTarget2 == null || tempTarget2.Width != w || tempTarget2.Height != h)
            {
                tempTarget2?.Dispose();
                tempTarget2 = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            }

            if (helperSpriteBatch == null || helperSpriteBatch.IsDisposed)
            {
                helperSpriteBatch = Main.spriteBatch;
            }

            try
            {
                if (ShaderControlConfig.EnableSampleStatic)
                {
                    totalSeconds += (float)Main.gameTimeCache.ElapsedGameTime.TotalSeconds;
                    var mat = Material;
                    var mat2 = Material2;
                    var mat3 = Material3;
                    var mat4 = Material4;
                    var context = Context;
                    mat2.Parameters["Time"].SetValue(totalSeconds);
                    mat4.Parameters["Threshold"].SetValue(ShaderControlConfig.Threshold);
                    mat4.Parameters["Smoothness"].SetValue(ShaderControlConfig.Smoothness);
                    mat2.Parameters["ChromaticAberration"].SetValue(ShaderControlConfig.ChromaticAberration);
                    mat2.Parameters["BloomIntensity"].SetValue(ShaderControlConfig.BloomIntensity);
                    mat2.Parameters["VignetteStrength"].SetValue(ShaderControlConfig.VignetteStrength);
                    mat2.Parameters["FilmGrainAmount"].SetValue(ShaderControlConfig.FilmGrainAmount);
                    mat2.Parameters["Saturation"].SetValue(ShaderControlConfig.Saturation);
                    mat2.Parameters["Contrast"].SetValue(ShaderControlConfig.Contrast);
                    mat2.Parameters["Brightness"].SetValue(ShaderControlConfig.Brightness);
                    mat2.SourceTexture[1] = tempTarget;
                    helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget);
                    context.Begin(blendState: BlendState.Opaque,
        depthStencilState: DepthStencilState.None,
        rasterizerState: RasterizerState.CullNone);
                    mat4.Apply(context.GetFNARenderDriver());
                    context.Draw(Main.screenTarget, Vector2.Zero, Color.White);
                    context.End();
                    helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget2);
                    context.Begin(blendState: BlendState.Opaque,
        depthStencilState: DepthStencilState.None,
        rasterizerState: RasterizerState.CullNone);
                    mat.Apply(context.GetFNARenderDriver());
                    context.Draw(tempTarget, Vector2.Zero, Color.White);
                    context.End();
                    helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget);
                    context.Begin(blendState: BlendState.Opaque,
        depthStencilState: DepthStencilState.None,
        rasterizerState: RasterizerState.CullNone);
                    mat3.Apply(context.GetFNARenderDriver());
                    context.Draw(tempTarget2, Vector2.Zero, Color.White);
                    context.End();
                    helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget2);
                    context.Begin(blendState: BlendState.Opaque,
        depthStencilState: DepthStencilState.None,
        rasterizerState: RasterizerState.CullNone);
                    mat2.Apply(context.GetFNARenderDriver());
                    context.Draw(Main.screenTarget, Vector2.Zero, Color.White);
                    context.End();
                    device.SetRenderTarget(null);
                    device.Clear(Color.Black);
                    device.Viewport = new Viewport(0, 0, Main.screenWidth, Main.screenHeight);
                    helperSpriteBatch.Draw(tempTarget2, Vector2.Zero, Color.White);
                }
            }
            catch (Exception ex)
            {

            }
        }
    }
}