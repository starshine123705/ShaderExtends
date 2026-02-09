using ShaderExtends.Base;
using System;

namespace ShaderExtends.Interfaces
{
    public interface IFCSEffect : IRefCounted
    {
        FCSMetadata Metadata { get; }
    }
}