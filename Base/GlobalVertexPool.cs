using ShaderExtends.Base;

namespace ShaderExtends.Core
{
    public static class GlobalVertexPool
    {
        // 池子 A：原始乱序池，用于存放上层提交的顶点原始数据（未排序）
        public static readonly NativeVertexBuffer RawPool = new(1024 * 1024 * 4);

        // 池子 B：排序整理池，用于将 RawPool 中的顶点紧凑化并合并后上传到 GPU
        public static readonly NativeVertexBuffer SortedPool = new(1024 * 1024 * 4);

        // 池子 C：索引池，用于存放上层提交的索引数据
        public static readonly NativeVertexBuffer IndexPool = new(1024 * 1024);

        // 池子 D：有序索引池，用于在合批后生成或拷贝出的索引数据
        public static readonly NativeVertexBuffer SortedIndexPool = new(1024 * 1024);

        /// <summary>
        /// 在每一帧渲染开始或结束时调用，重置所有池的分配指针以便复用缓冲区。
        /// 注意：重置并不会释放底层内存，只会将游标回退到起始位置。
        /// </summary>
        public static void ResetAll()
        {
            RawPool.Reset();
            SortedPool.Reset();
            IndexPool.Reset();
            SortedIndexPool.Reset();
        }

        /// <summary>
        /// 释放所有池分配的底层资源，仅在应用退出或明确不再使用时调用。
        /// </summary>
        public static void DisposeAll()
        {
            RawPool.Dispose();
            SortedPool.Dispose();
            IndexPool.Dispose();
            SortedIndexPool.Dispose();
        }
    }
}