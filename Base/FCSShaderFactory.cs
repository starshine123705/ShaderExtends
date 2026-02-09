using ShaderExtends.D3D11;
using ShaderExtends.Interfaces;
using ShaderExtends .OpenGL;
using Microsoft.Xna.Framework.Graphics;
using ShaderExtends.Base;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using Vortice.Direct3D11;

public static class FCSShaderFactory
{
    private static readonly Dictionary<string, object> _effectCache = new();

    public static IFCSMaterial CreateMaterial(GraphicsDevice device, string fcsPath)
    {
        var type = BackendInterop.GetRendererType(device);
        switch (type)
        {
            case FNA3D_SysRendererType.OpenGL:
                {
                    if (!_effectCache.TryGetValue(fcsPath, out var cachedGLEffect))
                    {
                        var reader = FCSReader.Load(fcsPath);
                        cachedGLEffect = new GLFCSEffect(reader);
                        _effectCache[fcsPath] = cachedGLEffect;
                    }

                    var glEffect = (GLFCSEffect)cachedGLEffect;
                    glEffect.AddRef();

                    return new GLFCSMaterial(glEffect, () => UnregisterEffect(fcsPath));
                }
            case FNA3D_SysRendererType.D3D11:
                {
                    if (!_effectCache.TryGetValue(fcsPath, out var cachedD3DEffect))
                    {
                        var backend = BackendInterop.GetBackendPointers(device);
                        var d3dDevice = MarshallingHelpers.FromPointer<ID3D11Device>(backend.d3d11_device);
                        var reader = FCSReader.Load(fcsPath);
                        cachedD3DEffect = new D3D11FCSEffect(d3dDevice, reader);
                        _effectCache[fcsPath] = cachedD3DEffect;
                    }

                    var d3dEffect = (D3D11FCSEffect)cachedD3DEffect;
                    d3dEffect.AddRef();

                    return new D3D11FCSMaterial(d3dEffect.d3D11Device, d3dEffect, () => UnregisterEffect(fcsPath));
                }
            default:
                {
                    throw new NotImplementedException($"{type.ToString()}:该后端尚未实现");
                }
        }
    }

    private static void UnregisterEffect(string key)
    {
        _effectCache.Remove(key);
    }
}