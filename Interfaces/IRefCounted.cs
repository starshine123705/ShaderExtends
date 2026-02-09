using System;

public interface IRefCounted : IDisposable
{
    void AddRef();
    void Release();
}