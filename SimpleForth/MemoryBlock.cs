using System;
using System.Runtime.InteropServices;

namespace SimpleForth
{
    public enum MemoryBlockSecurity
    {
        ReadOnly,
        ReadWrite,
        ExecuteRead,
        ExecuteReadWrite
    }
    
    public class MemoryBlock : IDisposable
    {
        private IntPtr location;
        private UIntPtr size;
        private MemoryBlockSecurity security;

        public MemoryBlock(UIntPtr size)
        {
            size = (UIntPtr)(((long)size + 0xFFFFL) & ~0xFFFFL);
            this.size = size;
            location = VirtualAlloc((IntPtr)0, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            security = MemoryBlockSecurity.ReadWrite;
        }

        public MemoryBlockSecurity Security
        {
            get
            {
                return security;
            }
            set
            {
                security = value;
                uint oldProtect;
                VirtualProtect(location, size, GetSecurityFlags(security), out oldProtect);
            }
        }

        public IntPtr Location { get { return location; } }

        public UIntPtr Size { get { return size; } }

        public void Dispose()
        {
            VirtualFree(location, (UIntPtr)0, MEM_RELEASE);
        }

        private static uint GetSecurityFlags(MemoryBlockSecurity mbs)
        {
            switch(mbs)
            {
                case MemoryBlockSecurity.ReadOnly: return PAGE_READONLY;
                case MemoryBlockSecurity.ReadWrite: return PAGE_READWRITE;
                case MemoryBlockSecurity.ExecuteRead: return PAGE_EXECUTE_READ;
                case MemoryBlockSecurity.ExecuteReadWrite: return PAGE_EXECUTE_READWRITE;
                default: goto case MemoryBlockSecurity.ReadWrite;
            }
        }

        private static readonly uint MEM_COMMIT = 0x1000;
        private static readonly uint MEM_RESERVE = 0x2000;
        //private static readonly uint MEM_RESET = 0x80000;
        //private static readonly uint MEM_TOP_DOWN = 0x100000;
        //private static readonly uint MEM_DECOMMIT = 0x4000;
        private static readonly uint MEM_RELEASE = 0x8000;

        private static readonly uint PAGE_READONLY = 0x02;
        private static readonly uint PAGE_READWRITE = 0x04;
        private static readonly uint PAGE_EXECUTE_READ = 0x20;
        private static readonly uint PAGE_EXECUTE_READWRITE = 0x40;

        //private static readonly uint PAGE_WRITECOMBINE = 0x400;

        [DllImport("Kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("Kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("Kernel32.dll", EntryPoint = "VirtualProtect", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        public static bool Is64Bit
        {
            get
            {
                return IntPtr.Size == 8;
            }
        }

        [DllImport("Kernel32.dll", EntryPoint = "RtlAddFunctionTable")]
        private static extern bool RtlAddFunctionTable(IntPtr functionTable, uint entryCount, IntPtr baseAddress);
    }

    public interface IByteMemory
    {
        byte this[long offset] { get; set; }
        short ReadInt16(long offset);
        int ReadInt32(long offset);
        long ReadInt64(long offset);
        float ReadSingle(long offset);
        double ReadDouble(long offset);

        void WriteInt16(long offset, short s);
        void WriteInt32(long offset, int i);
        void WriteInt64(long offset, long l);
        void WriteSingle(long offset, float f);
        void WriteDouble(long offset, double d);

        long Size { get; }

        void MemMove(long dest, long src, int size);
    }

    public sealed class ByteArrayWrapper : IByteMemory
    {
        private byte[] bytes;

        public ByteArrayWrapper(byte[] bytes)
        {
            this.bytes = bytes;
        }

        private void CheckRegion(long offset, int size)
        {
            if (offset < 0L || (offset + (long)size) > bytes.LongLength) throw new AccessViolationException("Memory access out of range");
        }

        public long Size { get { return bytes.LongLength; } }

        public byte this[long offset]
        {
            get
            {
                CheckRegion(offset, 1);
                return bytes[offset];
            }
            set
            {
                CheckRegion(offset, 1);
                bytes[offset] = value;
            }
        }

        public short ReadInt16(long offset)
        {
            CheckRegion(offset, 2);
            return BitConverter.ToInt16(bytes, (int)offset);
        }

        public int ReadInt32(long offset)
        {
            CheckRegion(offset, 4);
            return BitConverter.ToInt32(bytes, (int)offset);
        }

        public long ReadInt64(long offset)
        {
            CheckRegion(offset, 8);
            return BitConverter.ToInt64(bytes, (int)offset);
        }

        public float ReadSingle(long offset)
        {
            CheckRegion(offset, 4);
            return BitConverter.ToSingle(bytes, (int)offset);
        }

        public double ReadDouble(long offset)
        {
            CheckRegion(offset, 8);
            return BitConverter.ToDouble(bytes, (int)offset);
        }

        public void WriteInt16(long offset, short s)
        {
            byte[] b1 = BitConverter.GetBytes(s);
            Array.Copy(b1, 0, bytes, offset, 2);
        }

        public void WriteInt32(long offset, int i)
        {
            byte[] b1 = BitConverter.GetBytes(i);
            Array.Copy(b1, 0, bytes, offset, 4);
        }

        public void WriteInt64(long offset, long l)
        {
            byte[] b1 = BitConverter.GetBytes(l);
            Array.Copy(b1, 0, bytes, offset, 8);
        }

        public void WriteSingle(long offset, float f)
        {
            byte[] b1 = BitConverter.GetBytes(f);
            Array.Copy(b1, 0, bytes, offset, 4);
        }

        public void WriteDouble(long offset, double d)
        {
            byte[] b1 = BitConverter.GetBytes(d);
            Array.Copy(b1, 0, bytes, offset, 8);
        }

        public void MemMove(long dest, long src, int size)
        {
            Array.Copy(bytes, checked((int)src), bytes, checked((int)dest), size);
        }
    }

    public sealed class MemoryAccessor : IByteMemory
    {
        private IntPtr location;
        private UIntPtr size;

        public MemoryAccessor(IntPtr location, UIntPtr size)
        {
            this.location = location;
            this.size = size;
        }

        private void CheckRegion(long offset, int size)
        {
            if (offset < 0L || (offset + (long)size) > (long)(this.size)) throw new AccessViolationException("Memory access out of range");
        }

        public long RealAddress { get { return unchecked((long)location); } }

        public long Size { get { return unchecked((long)size); } }

        private IntPtr AddOffset(IntPtr @base, long offset)
        {
            return (IntPtr)((long)@base + offset);
        }

        public byte this[long offset]
        {
            get
            {
                CheckRegion(offset, 1);
                return Marshal.ReadByte(AddOffset(location, offset));
            }
            set
            {
                CheckRegion(offset, 1);
                Marshal.WriteByte(AddOffset(location, offset), value);
            }
        }

        public short ReadInt16(long offset)
        {
            CheckRegion(offset, 2);
            return Marshal.ReadInt16(AddOffset(location, offset));
        }

        public int ReadInt32(long offset)
        {
            CheckRegion(offset, 4);
            return Marshal.ReadInt32(AddOffset(location, offset));
        }

        public long ReadInt64(long offset)
        {
            CheckRegion(offset, 8);
            return Marshal.ReadInt64(AddOffset(location, offset));
        }

        public float ReadSingle(long offset)
        {
            CheckRegion(offset, 4);
            int i = Marshal.ReadInt32(AddOffset(location, offset));
            return BitConverter.ToSingle(BitConverter.GetBytes(i), 0);
        }

        public double ReadDouble(long offset)
        {
            CheckRegion(offset, 8);
            long l = Marshal.ReadInt64(AddOffset(location, offset));
            return BitConverter.Int64BitsToDouble(l);
        }

        public void WriteInt16(long offset, short s)
        {
            CheckRegion(offset, 2);
            Marshal.WriteInt16(AddOffset(location, offset), s);
        }

        public void WriteInt32(long offset, int i)
        {
            CheckRegion(offset, 4);
            Marshal.WriteInt32(AddOffset(location, offset), i);
        }

        public void WriteInt64(long offset, long l)
        {
            CheckRegion(offset, 8);
            Marshal.WriteInt64(AddOffset(location, offset), l);
        }

        public void WriteSingle(long offset, float f)
        {
            CheckRegion(offset, 4);
            Marshal.WriteInt32(AddOffset(location, offset), BitConverter.ToInt32(BitConverter.GetBytes(f), 0));
        }

        public void WriteDouble(long offset, double d)
        {
            CheckRegion(offset, 8);
            Marshal.WriteInt64(AddOffset(location, offset), BitConverter.DoubleToInt64Bits(d));
        }

        public unsafe void MemMove(long dest, long src, int size)
        {
            CheckRegion(dest, size);
            CheckRegion(src, size);

            if (dest > src)
            {
                byte* d1 = (byte*)(dest + size);
                byte* s1 = (byte*)(src + size);
                byte* d1End = (byte*)dest;
                while (d1 > d1End)
                {
                    --d1;
                    --s1;
                    *d1 = *d1;
                }
            }
            else
            {
            }
        }
    }

    // Marshal.GetDelegateForFunctionPointer
    // Marshal.GetFunctionPointerForDelegate
}