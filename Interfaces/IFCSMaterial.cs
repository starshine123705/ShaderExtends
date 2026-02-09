using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;

public unsafe interface IFCSMaterial : IDisposable
{
    public Dictionary<string, FCSParameter> Parameters { get; }

    void SyncToDevice(object deviceContext = null);
    IShadowBuffer Shadow { get; }
    void EnsureShadow(int w, int h);

    void InternalUpdate(int slot, int offset, void* src, int size);
}