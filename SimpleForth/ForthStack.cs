using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace SimpleForth
{
    public sealed class ForthStack<T>
    {
        private TransactionBox<ImmutableList<T>> _items;

        public ForthStack(StrongBox<AbstractTransactionLog> transactionLog)
        {
            _items = new TransactionBox<ImmutableList<T>>(transactionLog, ImmutableList<T>.Empty);
        }

        private ImmutableList<T> items
        {
            get { return _items.Value; }
            set { _items.Value = value; }
        }

        public void Push(T item)
        {
            items = items.Insert(0, item);
        }

        public T Pop()
        {

            if (items.IsEmpty) throw new ForthStackException("Stack underflow");
            T item = items[0];
            items = items.RemoveAt(0);
            return item;
        }

        public T Top
        {
            get
            {
                if (items.IsEmpty) throw new ForthStackException("Stack underflow");
                return items[0];
            }
            set
            {
                if (items.IsEmpty) throw new ForthStackException("Stack underflow");
                items = items.RemoveAt(0).Insert(0, value);
            }
        }

        public T this[int offset]
        {
            get
            {
                if (offset < 0) throw new ForthStackException("Invalid offset");
                if (offset >= items.Count) throw new ForthStackException("Stack underflow");
                return items[offset];
            }
            set
            {
                if (offset < 0) throw new ForthStackException("Invalid offset");
                if (offset >= items.Count) throw new ForthStackException("Stack underflow");
                items = items.RemoveAt(offset).Insert(offset, value);
            }
        }

        public void Alloc(int count, T theDefault)
        {
            for (int i = 0; i < count; ++i)
            {
                Push(theDefault);
            }
        }

        public void Free(int count)
        {
            if (count > items.Count) throw new ForthStackException("Stack underflow");
            items = items.RemoveRange(0, count);
        }

        public void Dup()
        {
            Push(Top);
        }

        public void Drop()
        {
            if (items.IsEmpty) throw new ForthStackException("Stack underflow");
            items = items.RemoveAt(0);
        }

        public int Depth
        {
            get
            {
                return items.Count;
            }
        }

        public bool IsEmpty { get { return items.IsEmpty; } }

        public void Swap()
        {
            if (items.Count < 2) throw new ForthStackException("Stack underflow");
            T i1 = items[0];
            items = items.RemoveAt(0).Insert(1, i1);
        }

        public void Over()
        {
            if (items.Count < 2) throw new ForthStackException("Stack underflow");
            items = items.Insert(0, items[1]);
        }

        public void Nip()
        {
            if (items.Count < 2) throw new ForthStackException("Stack underflow");
            items = items.RemoveAt(1);
        }

        public void Tuck()
        {
            Swap();
            Over();
        }

        public ImmutableList<T> Items
        {
            get { return items; }
            set { items = value; }
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
