/*
using System;
using System.Runtime.InteropServices;

namespace OpenCvSharp.Internal.Util
{
    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public readonly struct ReadOnlyArray2D<T> 
        where T : unmanaged
    {
        private readonly T[,] array;

        /// <summary>
        ///
        /// </summary>
        /// <param name="array"></param>
        public ReadOnlyArray2D(T[,] array)
        {
            this.array = array ?? throw new ArgumentNullException(nameof(array));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="i1"></param>
        /// <param name="i2"></param>
        /// <returns></returns>
        public ref readonly T Item(int i1, int i2) => ref array[i1, i2];
        
        /// <summary>
        ///
        /// </summary>
        /// <param name="pin"></param>
        /// <returns></returns>
        public unsafe ScopedGCHandle Pin(out T* p)
        {
            var handle = new ScopedGCHandle(array, GCHandleType.Pinned);
            p = (T*)handle.AddrOfPinnedObject();
            return handle;
        }
    }
}
*/
