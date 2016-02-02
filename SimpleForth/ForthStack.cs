using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleForth
{
    public class ForthStack<T>
    {
        private T[] items;
        private int ptr;

        public ForthStack()
        {
            items = new T[GetPreferredSpace(0)];
            ptr = items.Length;
        }

        private static int GetPreferredSpace(int size)
        {
            int i = 16;
            while (i < size) i <<= 1;
            return i;
        }

        private void Respace(int newSpace)
        {
            T[] items2 = new T[newSpace];
            int len = items.Length - ptr;
            int ptr2 = newSpace - len;
            Array.Copy(items, ptr, items2, ptr2, len);
            items = items2;
            ptr = ptr2;
        }

        public void Push(T item)
        {
            if (ptr == 0)
            {
                Respace(items.Length + 1);
            }
            --ptr;
            items[ptr] = item;
        }

        public T Pop()
        {
            if (ptr >= items.Length) throw new ForthStackException("Stack underflow");
            T value = items[ptr];
            items[ptr] = default(T);
            ++ptr;
            return value;
        }

        public T Top
        {
            get
            {
                if (ptr >= items.Length) throw new ForthStackException("Stack underflow");
                return items[ptr];
            }
            set
            {
                if (ptr >= items.Length) throw new ForthStackException("Stack underflow");
                items[ptr] = value;
            }
        }

        public T this[int offset]
        {
            get
            {
                if (offset < 0) throw new ForthStackException("Invalid offset");
                if ((ptr + offset) > items.Length) throw new ForthStackException("Stack underflow");
                return items[ptr + offset];
            }
            set
            {
                if (offset < 0) throw new ForthStackException("Invalid offset");
                if ((ptr + offset) > items.Length) throw new ForthStackException("Stack underflow");
                items[ptr + offset] = value;
            }
        }

        public void Alloc(int count)
        {
            for (int i = 0; i < count; ++i)
            {
                Push(default(T));
            }
        }

        public void Free(int count)
        {
            if ((ptr + count) > items.Length) throw new ForthStackException("Stack underflow");
            for (int i = 0; i < count; ++i)
            {
                items[ptr] = default(T);
                ++ptr;
            }
        }

        public void Dup()
        {
            Push(Top);
        }

        public void Drop()
        {
            if (ptr >= items.Length) throw new ForthStackException("Stack underflow");
            items[ptr] = default(T);
            ++ptr;
        }

        public int Depth
        {
            get
            {
                return items.Length - ptr;
            }
        }

        public bool IsEmpty { get { return ptr == items.Length; } }

        public void Swap()
        {
            T temp = items[ptr];
            items[ptr] = items[ptr + 1];
            items[ptr + 1] = temp;
        }

        public void Over()
        {
            Push(items[ptr + 1]);
        }

        public void Nip()
        {
            items[ptr + 1] = items[ptr];
            ++ptr;
        }

        public void Tuck()
        {
            Dup();
            items[ptr + 1] = items[ptr + 2];
            items[ptr + 2] = items[ptr];
        }
    }

    [Serializable]
    public class ForthStackException : Exception
    {
        public ForthStackException() : base() { }
        public ForthStackException(string message) : base(message) { }
        public ForthStackException(string message, Exception cause) : base(message, cause) { }
    }
}
