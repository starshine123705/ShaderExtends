using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace ShaderExtends.Base
{
    public static class RenderTargetPatch
    {
        public static RenderTarget2D MyFinalTarget;
        public static bool AllowPresent = false;
        private static MethodInfo method;
        public static void Patch()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            method = typeof(GraphicsDevice).GetMethod("SetRenderTarget", flags, [typeof(RenderTarget2D)]);
            if (method is not null)
            {
                MonoModHooks.Modify(method, IL_SetRenderTarget);
            }
        }

        public static void UpdateRenderTarget()
        {
            var device = Main.spriteBatch.GraphicsDevice;
            int width = device.Viewport.Width;
            int height = device.Viewport.Height;

            if (MyFinalTarget == null ||
                MyFinalTarget.Width != width ||
                MyFinalTarget.Height != height)
            {
                MyFinalTarget?.Dispose();
                MyFinalTarget = new RenderTarget2D(device, width, height);
            }
        }
        private static void IL_SetRenderTarget(ILContext il)
        {
            var c = new ILCursor(il);
            c.Goto(0);
            var continueLabel = il.DefineLabel();
            c.Emit(OpCodes.Ldsfld, typeof(RenderTargetPatch).GetField(nameof(AllowPresent)));
            c.Emit(OpCodes.Brtrue_S, continueLabel);
            c.Emit(OpCodes.Ldarg_1);
            c.Emit(OpCodes.Brtrue_S, continueLabel);
            c.Emit(OpCodes.Ldsfld, typeof(RenderTargetPatch).GetField(nameof(MyFinalTarget)));
            c.Emit(OpCodes.Starg_S, (byte)1);
            c.MarkLabel(continueLabel);
        }
    }
}
