using System.Runtime.InteropServices;

public static unsafe class NativeLinearIndexGenerator
{
    // 文件: Base/IndexGenerator.cs
    // 说明: 预生成一个线性连续的索引序列（0,1,2,3,...）并暴露为原始 byte* 指针，方便在
    //      拷贝索引数据时直接用 Unsafe.CopyBlockUnaligned/Buffer.MemoryCopy 进行快速复制。
    //      该类会在静态构造时分配非托管内存并初始化，程序退出时应确保释放（当前示例未实现释放）。

    // 直接对外暴露 byte*，方便 CopyBlock 调用
    public static readonly byte* Data;

    // 常量定义，避免魔术数字
    public const int MaxIndexCount = 65536;
    public const int SizeInBytes = MaxIndexCount * sizeof(ushort); // 128KB

    static NativeLinearIndexGenerator()
    {
        // 1. 分配非托管内存 (128KB)
        Data = (byte*)Marshal.AllocHGlobal(SizeInBytes);

        // 2. 填充数据 (使用 ushort 指针操作)
        ushort* ptr = (ushort*)Data;
        for (int i = 0; i < MaxIndexCount; i++)
        {
            ptr[i] = (ushort)i;
        }
    }
}
