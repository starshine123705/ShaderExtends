using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;

namespace ShaderExtends.Base
{
    public class PostProcessSystem : ModSystem
    {
        public static bool EnablePostProcess = true;
        private static bool ShouldPrint = false;
        public static IFCSMaterial Material, Material2;
        public static FNARenderContext Context;
        public override void Load()
        {
            On_Main.DoDraw += Main_DoDraw;
            RenderTargetPatch.Patch();
            PresentPatch.Patch();
        }
        private void Main_DoDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
        {
            if(Main.keyState.IsKeyDown(Keys.F12) && Main.oldKeyState.IsKeyUp(Keys.F12))
            {
                using (var fs = File.Create(Path.Combine("D:\\SteamLibrary\\steamapps\\common\\tModLoader\\tModLoader\\Capture", $"input_{0}.png")))
                {
                    var source = RenderTargetPatch.MyFinalTarget;
                    source.SaveAsPng(fs, source.Width, source.Height);
                }

                using (var fs = File.Create(Path.Combine("D:\\SteamLibrary\\steamapps\\common\\tModLoader\\tModLoader\\Capture", $"input_{1}.png")))
                {
                    var source = PresentPatch.tempTarget;
                    source.SaveAsPng(fs, source.Width, source.Height);
                }

                using (var fs = File.Create(Path.Combine("D:\\SteamLibrary\\steamapps\\common\\tModLoader\\tModLoader\\Capture", $"input_{2}.png")))
                {
                    var source = PresentPatch.tempTarget2;
                    source.SaveAsPng(fs, source.Width, source.Height);
                }
            }
            RenderTargetPatch.UpdateRenderTarget();

            RenderTargetPatch.AllowPresent = false;

            try
            {
                orig(self, gameTime); 
            }
            finally
            {
                RenderTargetPatch.AllowPresent = true;
            }

            var device = Main.graphics.GraphicsDevice;

            device.SetRenderTarget(null); 
        }
        public override void Unload()
        {
            RenderTargetPatch.MyFinalTarget?.Dispose();
            RenderTargetPatch.MyFinalTarget = null;
            PresentPatch.tempTarget?.Dispose();
            PresentPatch.tempTarget = null;
            PresentPatch.tempTarget2.Dispose();
            PresentPatch.tempTarget2 = null;
            PresentPatch.Unpatch();
        }
        public override void PreDrawMapIconOverlay(IReadOnlyList<IMapLayer> layers, MapOverlayDrawContext mapOverlayDrawContext)
        {
            base.PreDrawMapIconOverlay(layers, mapOverlayDrawContext);
        }



        public static float ChromaticAberration = 0.5f;
        public static float BloomIntensity = 0.0f;
        public static float VignetteStrength = 1.2f;
        public static float FilmGrainAmount = 0.05f;
        public static float Saturation = 1.1f;
        public static float Contrast = 1.1f;
        public static float Brightness = 0.0f;
        public override void PostSetupContent()
        {
            try
            {
                Main.QueueMainThreadAction(() =>
                {
                    Context = new FNARenderContext(Main.graphics?.GraphicsDevice);
                    Material = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/ComputeShader.fcs");
                    Material2 = FCSShaderFactory.CreateMaterial(Main.graphics?.GraphicsDevice, "CompiledShaders/AdvancedPostProcess.fcs");
                    Material.Parameters["TintColor"].SetValue(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
                    Material.Parameters["Contrast"].SetValue(1.1f);
                    Material.Parameters["Brightness"].SetValue(0.0f);
                    Vector2 res = new Vector2(Main.spriteBatch.GraphicsDevice.Viewport.Width, Main.spriteBatch.GraphicsDevice.Viewport.Height);
                    Material.Parameters["ScreenResolution"].SetValue(res);

                    Material2.Parameters["ChromaticAberration"].SetValue(ChromaticAberration);
                    Material2.Parameters["BloomIntensity"].SetValue(BloomIntensity);
                    Material2.Parameters["VignetteStrength"].SetValue(VignetteStrength);
                    Material2.Parameters["FilmGrainAmount"].SetValue(FilmGrainAmount);
                    Material2.Parameters["Saturation"].SetValue(Saturation);
                    Material2.Parameters["Contrast"].SetValue(Contrast);
                    Material2.Parameters["Brightness"].SetValue(Brightness);
                    Material2.Parameters["ScreenResolution"].SetValue(res);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fantasy] 完整错误: {ex}");
            }
            base.PostSetupContent();
        }
        
        public override void OnWorldLoad()
        {/*
            if (!isInitialized && Main.graphics?.GraphicsDevice != null)
            {
                try
                {
                    isInitialized = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fantasy] 完整错误: {ex}");
                }
            }*/
            base.OnWorldLoad();
        }
    }

    /// <summary>
    /// 拦截 Present 调用，在显示之前将 MyFinalTarget 绘制到屏幕
    /// </summary>
    public static class PresentPatch
    {
        private static SpriteBatch helperSpriteBatch;

        public static RenderTarget2D tempTarget;

        public static RenderTarget2D tempTarget2;
        public static void Patch()
        {
            try
            {
                On_Main.Draw += On_Main_Draw;
            }
            catch (Exception ex)
            {
                Main.NewText($"[Fantasy] ❌ Present 拦截失败: {ex.Message}", Color.Red);
                Console.WriteLine($"[Fantasy] 完整错误: {ex}");
            }
        }

        private static void On_Main_Draw(On_Main.orig_Draw orig, Main self, GameTime gameTime)
        {
            orig(self, gameTime);

            try
            {
                if (!Main.gameMenu)
                    BlitToScreen(Main.graphics.GraphicsDevice);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Blit 崩溃: {ex}");
            }
        }

        public static void Unpatch()
        {
            helperSpriteBatch?.Dispose();
            helperSpriteBatch = null;
        } 
        static float totalSeconds = 0;
        private static void BlitToScreen(GraphicsDevice device)
        {

            if (RenderTargetPatch.MyFinalTarget == null) return;
            if (RenderTargetPatch.MyFinalTarget.IsDisposed) return;

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
                if (ShaderControlConfig.EnableCSStatic || ShaderControlConfig.EnableVSPSStatic)
                {
                    helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget);
                    if (ShaderControlConfig.EnableCSStatic)
                    {
                        var mat = PostProcessSystem.Material;
                        totalSeconds += (float)Main.gameTimeCache.ElapsedGameTime.TotalSeconds;
                        mat.Parameters["Time"].SetValue(totalSeconds);
                        if (ShaderControlConfig.EnableVSPSStatic)
                        {
                            helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget);
                            PostProcessSystem.Context.Begin();
                            PostProcessSystem.Material.Apply();
                            PostProcessSystem.Context.Draw(RenderTargetPatch.MyFinalTarget, Vector2.Zero, Color.White);
                            PostProcessSystem.Context.End();
                            //PostProcessSystem.Context.Apply(mat, RenderTargetPatch.MyFinalTarget, tempTarget);
                        }
                        else
                        {
                            helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget2);
                            //PostProcessSystem.Context.Apply(mat, RenderTargetPatch.MyFinalTarget, tempTarget2);
                        }

                    }
                    if (ShaderControlConfig.EnableVSPSStatic)
                    {
                        var mat2 = PostProcessSystem.Material2;
                        mat2.Parameters["Time"].SetValue(totalSeconds);
                        if (ShaderControlConfig.EnableCSStatic)
                        {
                            helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget2);
                            PostProcessSystem.Context.Apply(mat2, tempTarget, tempTarget2);
                        }
                        else
                        {
                            helperSpriteBatch.GraphicsDevice.SetRenderTarget(tempTarget2);
                            PostProcessSystem.Context.Apply(mat2, RenderTargetPatch.MyFinalTarget, tempTarget2);
                        }
                    }
                    RenderTargetPatch.AllowPresent = true;

                    device.SetRenderTarget(null);
                    device.Clear(Color.Black);
                    device.Viewport = new Viewport(0, 0, Main.screenWidth, Main.screenHeight);
                    helperSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
                    helperSpriteBatch.Draw(tempTarget2, Vector2.Zero, Color.White);
                    helperSpriteBatch.End();
                }
                else
                {
                    device.SetRenderTarget(null);
                    DrawDirect(device, helperSpriteBatch);
                }
            }
            finally
            {
                RenderTargetPatch.AllowPresent = false;
            }
        }

        private static void DrawDirect(GraphicsDevice device, SpriteBatch spriteBatch)
        {
            var destRect = new Rectangle(
                0, 0,
                device.PresentationParameters.BackBufferWidth,
                device.PresentationParameters.BackBufferHeight
            );

            spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.Opaque,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone
            );

            spriteBatch.Draw(RenderTargetPatch.MyFinalTarget, destRect, Color.White);

            spriteBatch.End();
        }
    }
}