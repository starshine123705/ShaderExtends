using Microsoft.Xna.Framework.Graphics;
using ShaderExtends.Base;
using System;
using System.Runtime.InteropServices;

namespace ShaderExtends.Base
{
    public enum FNA3D_SysRendererType
    {
        OpenGL = 0,
        Vulkan = 1, // 已移除，不要使用
        D3D11 = 2,
        Metal = 3,  // 已移除，不要使用
        SDL_GPU = 4,
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct FNA3D_SysRendererEXT
    {
        [FieldOffset(0)]
        public uint version; 

        [FieldOffset(4)]
        public FNA3D_SysRendererType rendererType;

        [FieldOffset(8)]
        public IntPtr opengl_context;

        [FieldOffset(8)]
        public IntPtr d3d11_device;

        [FieldOffset(16)]
        public IntPtr d3d11_context;

        [FieldOffset(8)]
        public IntPtr vulkan_instance;

        [FieldOffset(16)]
        public IntPtr vulkan_physicalDevice;

        [FieldOffset(24)]
        public IntPtr vulkan_logicalDevice;

        [FieldOffset(32)]
        public uint vulkan_queueFamilyIndex;
    }

    public enum FNA3D_RendererType : uint
    {
        OpenGL = 0,
        Vulkan = 1,
        D3D11 = 2,
        Metal = 3
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct FNA3D_SysTextureEXT
    {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4)] public FNA3D_RendererType RendererType;

        [FieldOffset(8)] public IntPtr D3D11_Handle;
        [FieldOffset(16)] public IntPtr D3D11_ShaderView;

        [FieldOffset(8)] public uint GL_Handle;
        [FieldOffset(12)] public uint GL_Target;
    }
}
public class BackendInterop
{
    [DllImport("FNA3D", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FNA3D_GetSysRendererEXT(
            IntPtr device,
            ref FNA3D_SysRendererEXT sysrenderer
        );
    [DllImport("FNA3D", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FNA3D_GetSysTextureEXT(IntPtr texture, ref FNA3D_SysTextureEXT systexture);

    public static FNA3D_SysTextureEXT GetSysTexture(Texture texture)
    {
        var field = typeof(Texture).GetField("texture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        IntPtr fnaTexturePtr = (IntPtr)field.GetValue(texture);

        FNA3D_SysTextureEXT sysTexture = new FNA3D_SysTextureEXT { Version = 0 };
        FNA3D_GetSysTextureEXT(fnaTexturePtr, ref sysTexture);
        return sysTexture;
    }
    public static FNA3D_SysRendererEXT GetBackendPointers(GraphicsDevice graphicsDevice)
    {
        IntPtr devicePtr = GetFNA3DDevicePointer(graphicsDevice);

        FNA3D_SysRendererEXT sysrenderer = new FNA3D_SysRendererEXT();
        sysrenderer.version = 0;

        // 调用 FNA3D API
        FNA3D_GetSysRendererEXT(devicePtr, ref sysrenderer);

        return sysrenderer;
    }

    private static IntPtr GetFNA3DDevicePointer(GraphicsDevice graphicsDevice)
    {
        var deviceField = typeof(GraphicsDevice).GetField(
            "GLDevice",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (IntPtr)deviceField.GetValue(graphicsDevice);

    }

    public static FNA3D_SysRendererType GetRendererType(GraphicsDevice device)
    {
        return (FNA3D_SysRendererType)GetBackendPointers(device).rendererType;
    }
}