using System.Collections;

namespace RedHoleEngine.Core.ECS;

/// <summary>
/// Zero-allocation view of entities that have component <typeparamref name="T"/>.
/// foreach uses the struct enumerator. LINQ boxes one enumerator, same as before.
/// </summary>
public readonly struct EntityQuery<T> : IEnumerable<Entity> where T : IComponent
{
    private readonly ComponentPool<T> _pool;
    private readonly int[] _generations;

    internal EntityQuery(ComponentPool<T> pool, int[] generations)
    {
        _pool = pool;
        _generations = generations;
    }

    public Enumerator GetEnumerator() => new(_pool, _generations);

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly ComponentPool<T> _pool;
        private readonly int[] _generations;
        private int _index;

        internal Enumerator(ComponentPool<T> pool, int[] generations)
        {
            _pool = pool;
            _generations = generations;
            _index = 0;
            Current = default;
        }

        public Entity Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if ((uint)_index >= (uint)_pool.Count)
                return false;

            int id = _pool.GetEntityIdAt(_index);
            Current = new Entity(id, _generations[id]);
            _index++;
            return true;
        }

        public void Reset() => _index = 0;
        public void Dispose() { }
    }
}

public readonly struct EntityQuery<T1, T2> : IEnumerable<Entity>
    where T1 : IComponent
    where T2 : IComponent
{
    private readonly ComponentPool<T1> _pool1;
    private readonly ComponentPool<T2> _pool2;
    private readonly int[] _generations;

    internal EntityQuery(ComponentPool<T1> pool1, ComponentPool<T2> pool2, int[] generations)
    {
        _pool1 = pool1;
        _pool2 = pool2;
        _generations = generations;
    }

    public Enumerator GetEnumerator() => new(_pool1, _pool2, _generations);

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly ComponentPool<T1> _pool1;
        private readonly ComponentPool<T2> _pool2;
        private readonly int[] _generations;
        private int _index;

        internal Enumerator(ComponentPool<T1> pool1, ComponentPool<T2> pool2, int[] generations)
        {
            _pool1 = pool1;
            _pool2 = pool2;
            _generations = generations;
            _index = 0;
            Current = default;
        }

        public Entity Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            int count = _pool1.Count;
            while ((uint)_index < (uint)count)
            {
                int id = _pool1.GetEntityIdAt(_index);
                _index++;
                if (!_pool2.Has(id))
                    continue;
                Current = new Entity(id, _generations[id]);
                return true;
            }
            return false;
        }

        public void Reset() => _index = 0;
        public void Dispose() { }
    }
}

public readonly struct EntityQuery<T1, T2, T3> : IEnumerable<Entity>
    where T1 : IComponent
    where T2 : IComponent
    where T3 : IComponent
{
    private readonly ComponentPool<T1> _pool1;
    private readonly ComponentPool<T2> _pool2;
    private readonly ComponentPool<T3> _pool3;
    private readonly int[] _generations;

    internal EntityQuery(ComponentPool<T1> pool1, ComponentPool<T2> pool2, ComponentPool<T3> pool3, int[] generations)
    {
        _pool1 = pool1;
        _pool2 = pool2;
        _pool3 = pool3;
        _generations = generations;
    }

    public Enumerator GetEnumerator() => new(_pool1, _pool2, _pool3, _generations);

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly ComponentPool<T1> _pool1;
        private readonly ComponentPool<T2> _pool2;
        private readonly ComponentPool<T3> _pool3;
        private readonly int[] _generations;
        private int _index;

        internal Enumerator(ComponentPool<T1> pool1, ComponentPool<T2> pool2, ComponentPool<T3> pool3, int[] generations)
        {
            _pool1 = pool1;
            _pool2 = pool2;
            _pool3 = pool3;
            _generations = generations;
            _index = 0;
            Current = default;
        }

        public Entity Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            int count = _pool1.Count;
            while ((uint)_index < (uint)count)
            {
                int id = _pool1.GetEntityIdAt(_index);
                _index++;
                if (!_pool2.Has(id) || !_pool3.Has(id))
                    continue;
                Current = new Entity(id, _generations[id]);
                return true;
            }
            return false;
        }

        public void Reset() => _index = 0;
        public void Dispose() { }
    }
}
