using System;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace ShaderExtends.D3D11
{

    /// <summary>
    /// FNA 纹理到 D3D11 资源的完整桥接
    /// 基于 FNA3D 源码的精确结构定义
    /// </summary>
    public static class D3D11TextureHelper
    {
        /// <summary>
        /// D3D11Texture 结构定义（与 FNA3D C 代码对应）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11Texture
        {
            public IntPtr handle;           // ID3D11Resource* (Offset +0)
            public IntPtr shaderView;       // ID3D11ShaderResourceView* (Offset +8 on x64)
            public int levelCount;          // Offset +16
            public byte isRenderTarget;     // Offset +20
            public int format;              // FNA3D_SurfaceFormat (Offset +24)
            public byte rtType;             // Offset +28

            public int twod_width;              // +32
            public int twod_height;             // +36
            public IntPtr twod_rtView;          // +40
        }

        [StructLayout(LayoutKind.Sequential)]
        struct D3D11Texture2DView
        {
            public int width;              // [offset 0]
            public int height;             // [offset 4]
            public IntPtr rtView;          // [offset 8] 指向ID3D11RenderTargetView*
        }

        /// <summary>
        /// 从 FNA Texture2D 获取 D3D11 ShaderResourceView（方法 1：使用结构体）
        /// </summary>
        public static IntPtr GetD3D11SRV_Method1(Texture2D texture)
        {
            IntPtr fna3dTexturePtr = FNAHooks.GetNativeTexturePtr(texture);

            if (fna3dTexturePtr == IntPtr.Zero)
                throw new Exception("FNA3D 纹理指针为空");

            D3D11Texture d3dTexture = Marshal.PtrToStructure<D3D11Texture>(fna3dTexturePtr);

            if (d3dTexture.shaderView == IntPtr.Zero)
                throw new Exception("ShaderResourceView 为空");

            return d3dTexture.shaderView;
        }

        /// <summary>
        /// 从 FNA Texture2D 获取 D3D11 ShaderResourceView（方法 2：直接读取偏移）
        /// </summary>
        public static IntPtr GetD3D11SRV_Method2(Texture2D texture)
        {
            IntPtr fna3dTexturePtr = FNAHooks.GetNativeTexturePtr(texture);

            if (fna3dTexturePtr == IntPtr.Zero)
                throw new Exception("FNA3D 纹理指针为空");

            int srvOffset = IntPtr.Size;
            IntPtr srv = Marshal.ReadIntPtr(fna3dTexturePtr, srvOffset);

            if (srv == IntPtr.Zero)
                throw new Exception("ShaderResourceView 为空");

            return srv;
        }

        /// <summary>
        /// 获取 D3D11 Resource Handle（纹理本身）
        /// </summary>
        public static IntPtr GetD3D11ResourceHandle(Texture2D texture)
        {
            IntPtr fna3dTexturePtr = FNAHooks.GetNativeTexturePtr(texture);

            if (fna3dTexturePtr == IntPtr.Zero)
                throw new Exception("FNA3D 纹理指针为空");

            IntPtr handle = Marshal.ReadIntPtr(fna3dTexturePtr, 0);

            if (handle == IntPtr.Zero)
                throw new Exception("ID3D11Resource handle 为空");

            return handle;
        }

        /// <summary>
        /// 获取 D3D11 RenderTargetView（如果是 RenderTarget）
        /// </summary>
        public static IntPtr GetD3D11RTV(RenderTarget2D renderTarget)
        {
            IntPtr fna3dTexturePtr = FNAHooks.GetNativeTexturePtr(renderTarget);

            if (fna3dTexturePtr == IntPtr.Zero)
                throw new Exception("FNA3D 纹理指针为空");

            D3D11Texture d3dTexture = Marshal.PtrToStructure<D3D11Texture>(fna3dTexturePtr);

            if (d3dTexture.isRenderTarget == 0)
                throw new Exception("此纹理不是 RenderTarget");
            return d3dTexture.twod_rtView;
        }
    }
}