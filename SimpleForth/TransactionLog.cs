using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SimpleForth
{
    public enum TransactionLogState
    {
        Open,
        Committed,
        RolledBack,
        ContinuousCommit
    }

    public abstract class AbstractTransactionLog
    {
        public abstract TransactionLogState State { get; }

        public abstract AbstractTransactionLog Parent { get; }

        public abstract void AddUndo(ObjectKey k, Action undoAction);

        /// <summary>
        /// Promote all the undo actions to the parent transaction if any
        /// </summary>
        public abstract void Commit();

        /// <summary>
        /// Carry out all the undo actions.
        /// </summary>
        public abstract void RollBack();
    }

    public sealed class ContinuousCommitTransactionLog : AbstractTransactionLog
    {
        private static readonly ContinuousCommitTransactionLog instance = new ContinuousCommitTransactionLog();

        private ContinuousCommitTransactionLog() { }

        public static ContinuousCommitTransactionLog Instance => instance;

        public override TransactionLogState State => TransactionLogState.ContinuousCommit;

        public override AbstractTransactionLog Parent => this;

        public override void AddUndo(ObjectKey k, Action undoAction)
        {
            // do nothing
        }

        public override void Commit()
        {
            // do nothing
        }

        public override void RollBack()
        {
            throw new InvalidOperationException("Cannot roll back due to continuous commits");
        }
    }

    public sealed class TransactionLog : AbstractTransactionLog
    {
        private TransactionLogState state;
        private readonly AbstractTransactionLog parent;
        private readonly Dictionary<ObjectKey, Action> undoActions;

        public TransactionLog(AbstractTransactionLog parent)
        {
            state = TransactionLogState.Open;
            this.parent = parent;
            undoActions = new Dictionary<ObjectKey, Action>();
        }

        public override TransactionLogState State => state;

        public override AbstractTransactionLog Parent => parent;

        public override void AddUndo(ObjectKey k, Action undoAction)
        {
            undoActions.TryAdd(k, undoAction);
        }

        public override void Commit()
        {
            if (state == TransactionLogState.Open)
            {
                if (parent.State == TransactionLogState.Open)
                {
                    foreach (KeyValuePair<ObjectKey, Action> kvp in undoActions)
                    {
                        parent.AddUndo(kvp.Key, kvp.Value);
                    }
                }
                else if (parent.State == TransactionLogState.ContinuousCommit)
                {
                    // do nothing
                }
                else
                {
                    throw new InvalidOperationException($"Parent transaction state should have been Open or ContinuousCommit, was {parent.State}");
                }

                state = TransactionLogState.Committed;
            }
            else
            {
                throw new InvalidOperationException($"State should have been Open, was {state}");
            }
        }

        public override void RollBack()
        {
            if (state == TransactionLogState.Open)
            {
                foreach(KeyValuePair<ObjectKey, Action> kvp in undoActions)
                {
                    kvp.Value();
                }

                state = TransactionLogState.RolledBack;
            }
            else
            {
                throw new InvalidOperationException($"State should have been Open, was {state}");
            }
        }
    }

    public sealed class TransactionBox<T>
    {
        private readonly StrongBox<AbstractTransactionLog> log;
        private T boxValue;

        public TransactionBox(StrongBox<AbstractTransactionLog> log, T value)
        {
            this.log = log;
            this.boxValue = value;
        }

        public T Value
        {
            get
            {
                return boxValue;
            }
            set
            {
                T oldValue = this.boxValue;
                log.Value?.AddUndo(ObjectKey.CreateNamed(this, "value"), () => { boxValue = oldValue; });
                this.boxValue = value;
            }
        }
    }

    public abstract class ObjectKey : IEquatable<ObjectKey>, IComparable<ObjectKey>
    {
        private static readonly object staticSyncRoot;
        private static ulong nextId = 0uL;
        private static readonly ConditionalWeakTable<object, StrongBox<ulong>> objectToId;

        static ObjectKey()
        {
            staticSyncRoot = new object();
            objectToId = new ConditionalWeakTable<object, StrongBox<ulong>>();
        }

        private static ulong GetId(object obj)
        {
            lock(staticSyncRoot)
            {
                if (objectToId.TryGetValue(obj, out StrongBox<ulong>? idBox))
                {
                    return idBox.Value;
                }
                else
                {
                    StrongBox<ulong> newIdBox = new StrongBox<ulong>(nextId++);
                    objectToId.Add(obj, newIdBox);
                    return newIdBox.Value;
                }
            }
        }

        public static ObjectKey CreateIndexed(object key, int index) => new IndexedObjectKey(GetId(key), index);

        public static ObjectKey CreateNamed(object key, string fieldName) => new NamedObjectKey(GetId(key), fieldName);

        public static ObjectKey CreateNamedIndexed(object key, string fieldName, int index) => new NamedIndexedObjectKey(GetId(key), fieldName, index);

        public abstract override string ToString();

        private sealed class IndexedObjectKey : ObjectKey
        {
            private readonly ulong objId;
            private readonly int index;

            public IndexedObjectKey(ulong objId, int index)
            {
                this.objId = objId;
                this.index = index;
            }

            public override string ToString()
            {
                return $"(obj = {objId}, index = {index})";
            }
        }

        private sealed class NamedObjectKey : ObjectKey
        {
            private readonly ulong objId;
            private readonly string fieldName;

            public NamedObjectKey(ulong objId, string fieldName)
            {
                this.objId = objId;
                this.fieldName = fieldName;
            }

            public override string ToString()
            {
                return $"(obj = {objId}, name = {fieldName})";
            }
        }

        private sealed class NamedIndexedObjectKey : ObjectKey
        {
            private readonly ulong objId;
            private readonly string fieldName;
            private readonly int index;

            public NamedIndexedObjectKey(ulong objId, string fieldName, int index)
            {
                this.objId = objId;
                this.fieldName = fieldName;
                this.index = index;
            }

            public override string ToString()
            {
                return $"(obj = {objId}, name = {fieldName}, index = {index})";
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ObjectKey) return false;
            return string.Equals(ToString(), obj.ToString(), StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public bool Equals(ObjectKey? other)
        {
            if (other is null) return false;
            return string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        public int CompareTo(ObjectKey? other)
        {
            if (other is null) return 1;
            return string.Compare(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        public static bool operator ==(ObjectKey o1, ObjectKey o2)
        {
            return o1.Equals(o2);
        }

        public static bool operator !=(ObjectKey o1, ObjectKey o2)
        {
            return !o1.Equals(o2);
        }

        public static bool operator <(ObjectKey o1, ObjectKey o2)
        {
            return string.Compare(o1.ToString(), o2.ToString(), StringComparison.Ordinal) < 0;
        }

        public static bool operator >(ObjectKey o1, ObjectKey o2)
        {
            return string.Compare(o1.ToString(), o2.ToString(), StringComparison.Ordinal) > 0;
        }

        public static bool operator <=(ObjectKey o1, ObjectKey o2)
        {
            return string.Compare(o1.ToString(), o2.ToString(), StringComparison.Ordinal) <= 0;
        }

        public static bool operator >=(ObjectKey o1, ObjectKey o2)
        {
            return string.Compare(o1.ToString(), o2.ToString(), StringComparison.Ordinal) >= 0;
        }
    }
}
