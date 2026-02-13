using ShaderExtends.Base;

namespace ShaderExtends.Core
{
    public static class GlobalVertexPool
    {
        // 池子 A：原始乱序池
        public static readonly NativeVertexBuffer RawPool = new(1024 * 1024 * 4);

        // 池子 B：排序整理池
        public static readonly NativeVertexBuffer SortedPool = new(1024 * 1024 * 4);

        /// <summary>
        /// 每一帧渲染开始或结束时统一重置
        /// </summary>
        public static void ResetAll()
        {
            RawPool.Reset();
            SortedPool.Reset();
        }

        public static void DisposeAll()
        {
            RawPool.Dispose();
            SortedPool.Dispose();
        }
    }
}