using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Particles;

/// <summary>
/// Sorting mode for particles
/// </summary>
public enum ParticleSortMode
{
    /// <summary>
    /// No sorting (fastest, may have visual artifacts with transparency)
    /// </summary>
    None,
    
    /// <summary>
    /// Sort by distance from camera (back to front for correct alpha blending)
    /// </summary>
    ByDistance,
    
    /// <summary>
    /// Sort by age (oldest first)
    /// </summary>
    ByAge,
    
    /// <summary>
    /// Sort by age (youngest first)
    /// </summary>
    ByAgeReverse,
    
    /// <summary>
    /// Sort by depth along camera forward vector (more accurate for perspective)
    /// </summary>
    ByDepth
}

/// <summary>
/// Efficient fixed-size pool for particle storage.
/// Uses contiguous memory for cache-friendly iteration.
/// </summary>
/// <remarks>
/// FUTURE OPTIMIZATION: For very large particle counts (10k+), consider implementing
/// GPU compute shader sorting using bitonic sort or radix sort. This would:
/// - Eliminate CPU-GPU data transfer for sorting
/// - Scale better with particle count (parallel sorting)
/// - Require a compute shader (particle_sort.comp) and storage buffer
/// - Need double-buffering for sort keys and indices
/// See: https://developer.nvidia.com/gpugems/gpugems2/part-vi-simulation-and-numerical-algorithms/chapter-46-improved-gpu-sorting
/// </remarks>
public class ParticlePool
{
    private readonly Particle[] _particles;
    private readonly float[] _baseSizes; // Store initial sizes for SizeOverLifetime
    private int _aliveCount;
    private int _capacity;
    
    // Pre-allocated sorting buffers (avoid per-frame allocation)
    private int[] _sortIndices;
    private float[] _sortKeys;
    private Particle[] _tempParticles;
    private float[] _tempSizes;

    /// <summary>
    /// Number of currently alive particles
    /// </summary>
    public int AliveCount => _aliveCount;

    /// <summary>
    /// Maximum number of particles this pool can hold
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Whether the pool is full
    /// </summary>
    public bool IsFull => _aliveCount >= _capacity;

    /// <summary>
    /// Create a new particle pool with the specified capacity
    /// </summary>
    public ParticlePool(int capacity)
    {
        _capacity = capacity;
        _particles = new Particle[capacity];
        _baseSizes = new float[capacity];
        _aliveCount = 0;
        
        // Pre-allocate sorting buffers
        _sortIndices = new int[capacity];
        _sortKeys = new float[capacity];
        _tempParticles = new Particle[capacity];
        _tempSizes = new float[capacity];
    }

    /// <summary>
    /// Emit a new particle. Returns false if pool is full.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Emit(in Particle particle)
    {
        if (_aliveCount >= _capacity)
            return false;

        _particles[_aliveCount] = particle;
        _baseSizes[_aliveCount] = particle.Size;
        _aliveCount++;
        return true;
    }

    /// <summary>
    /// Get a reference to a particle by index
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref Particle GetParticle(int index)
    {
        return ref _particles[index];
    }

    /// <summary>
    /// Get the base size (initial size) of a particle
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetBaseSize(int index)
    {
        return _baseSizes[index];
    }

    /// <summary>
    /// Remove dead particles by swapping with the last alive particle.
    /// Call this after updating lifetimes.
    /// </summary>
    public void RemoveDeadParticles()
    {
        int i = 0;
        while (i < _aliveCount)
        {
            if (!_particles[i].IsAlive || _particles[i].Lifetime <= 0)
            {
                // Swap with last alive particle
                _aliveCount--;
                if (i < _aliveCount)
                {
                    _particles[i] = _particles[_aliveCount];
                    _baseSizes[i] = _baseSizes[_aliveCount];
                }
                // Don't increment i, check the swapped particle
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// Clear all particles
    /// </summary>
    public void Clear()
    {
        _aliveCount = 0;
    }

    /// <summary>
    /// Get particles as a span for efficient iteration
    /// </summary>
    public Span<Particle> GetAliveParticles()
    {
        return _particles.AsSpan(0, _aliveCount);
    }

    /// <summary>
    /// Get read-only span of alive particles
    /// </summary>
    public ReadOnlySpan<Particle> GetAliveParticlesReadOnly()
    {
        return _particles.AsSpan(0, _aliveCount);
    }

    /// <summary>
    /// Copy particle render data to an array for GPU upload
    /// </summary>
    public int CopyRenderData(ParticleRenderData[] destination, int startIndex = 0)
    {
        int count = Math.Min(_aliveCount, destination.Length - startIndex);
        
        for (int i = 0; i < count; i++)
        {
            destination[startIndex + i] = ParticleRenderData.FromParticle(in _particles[i]);
        }

        return count;
    }

    /// <summary>
    /// Copy render data to a span
    /// </summary>
    public int CopyRenderData(Span<ParticleRenderData> destination)
    {
        int count = Math.Min(_aliveCount, destination.Length);
        
        for (int i = 0; i < count; i++)
        {
            destination[i] = ParticleRenderData.FromParticle(in _particles[i]);
        }

        return count;
    }

    /// <summary>
    /// Sort particles using the specified mode
    /// </summary>
    public void Sort(ParticleSortMode mode, Vector3 cameraPosition, Vector3 cameraForward)
    {
        switch (mode)
        {
            case ParticleSortMode.None:
                return;
            case ParticleSortMode.ByDistance:
                SortByDistance(cameraPosition);
                break;
            case ParticleSortMode.ByDepth:
                SortByDepth(cameraPosition, cameraForward);
                break;
            case ParticleSortMode.ByAge:
                SortByAge(reverse: false);
                break;
            case ParticleSortMode.ByAgeReverse:
                SortByAge(reverse: true);
                break;
        }
    }

    /// <summary>
    /// Sort particles by distance from camera (back to front for correct alpha blending)
    /// Uses pre-allocated buffers for zero per-frame allocation.
    /// </summary>
    public void SortByDistance(Vector3 cameraPosition)
    {
        if (_aliveCount <= 1)
            return;

        // Compute sort keys (squared distance - no need for sqrt)
        for (int i = 0; i < _aliveCount; i++)
        {
            Vector3 diff = _particles[i].Position - cameraPosition;
            _sortKeys[i] = diff.LengthSquared();
            _sortIndices[i] = i;
        }

        // Sort indices by key (descending - back to front)
        SortIndicesByKeyDescending(_aliveCount);

        // Reorder particles using pre-allocated temp buffer
        ReorderParticles(_aliveCount);
    }

    /// <summary>
    /// Sort particles by depth along camera forward vector.
    /// More accurate for perspective projection than distance-based sorting.
    /// </summary>
    public void SortByDepth(Vector3 cameraPosition, Vector3 cameraForward)
    {
        if (_aliveCount <= 1)
            return;

        // Normalize forward vector
        cameraForward = Vector3.Normalize(cameraForward);

        // Compute sort keys (dot product = depth along view direction)
        for (int i = 0; i < _aliveCount; i++)
        {
            Vector3 toParticle = _particles[i].Position - cameraPosition;
            _sortKeys[i] = Vector3.Dot(toParticle, cameraForward);
            _sortIndices[i] = i;
        }

        // Sort indices by key (descending - back to front)
        SortIndicesByKeyDescending(_aliveCount);

        // Reorder particles
        ReorderParticles(_aliveCount);
    }

    /// <summary>
    /// <summary>
    /// Sort particles by age.
    /// Default (reverse=false) sorts oldest first (highest NormalizedAge first).
    /// </summary>
    public void SortByAge(bool reverse = false)
    {
        if (_aliveCount <= 1)
            return;

        // Compute sort keys (normalized age)
        for (int i = 0; i < _aliveCount; i++)
        {
            _sortKeys[i] = _particles[i].NormalizedAge;
            _sortIndices[i] = i;
        }

        // Sort indices - default is oldest first (descending NormalizedAge)
        if (reverse)
            SortIndicesByKeyAscending(_aliveCount);  // youngest first
        else
            SortIndicesByKeyDescending(_aliveCount); // oldest first

        // Reorder particles
        ReorderParticles(_aliveCount);
    }

    /// <summary>
    /// Optimized quicksort for indices by key (descending order)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SortIndicesByKeyDescending(int count)
    {
        // Use Array.Sort with a span for efficiency
        var indices = _sortIndices.AsSpan(0, count);
        var keys = _sortKeys.AsSpan(0, count);
        
        // Create array of tuples for sorting
        for (int i = 0; i < count; i++)
        {
            _sortIndices[i] = i;
        }

        // Quicksort with custom comparison (descending)
        QuickSortDescending(0, count - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SortIndicesByKeyAscending(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _sortIndices[i] = i;
        }

        QuickSortAscending(0, count - 1);
    }

    private void QuickSortDescending(int left, int right)
    {
        if (left >= right) return;

        int pivotIndex = PartitionDescending(left, right);
        QuickSortDescending(left, pivotIndex - 1);
        QuickSortDescending(pivotIndex + 1, right);
    }

    private void QuickSortAscending(int left, int right)
    {
        if (left >= right) return;

        int pivotIndex = PartitionAscending(left, right);
        QuickSortAscending(left, pivotIndex - 1);
        QuickSortAscending(pivotIndex + 1, right);
    }

    private int PartitionDescending(int left, int right)
    {
        float pivotKey = _sortKeys[_sortIndices[right]];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            if (_sortKeys[_sortIndices[j]] >= pivotKey) // Descending
            {
                i++;
                (_sortIndices[i], _sortIndices[j]) = (_sortIndices[j], _sortIndices[i]);
            }
        }

        (_sortIndices[i + 1], _sortIndices[right]) = (_sortIndices[right], _sortIndices[i + 1]);
        return i + 1;
    }

    private int PartitionAscending(int left, int right)
    {
        float pivotKey = _sortKeys[_sortIndices[right]];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            if (_sortKeys[_sortIndices[j]] <= pivotKey) // Ascending
            {
                i++;
                (_sortIndices[i], _sortIndices[j]) = (_sortIndices[j], _sortIndices[i]);
            }
        }

        (_sortIndices[i + 1], _sortIndices[right]) = (_sortIndices[right], _sortIndices[i + 1]);
        return i + 1;
    }

    /// <summary>
    /// Reorder particles based on sorted indices using pre-allocated buffers
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReorderParticles(int count)
    {
        // Copy to temp buffer in sorted order
        for (int i = 0; i < count; i++)
        {
            int srcIdx = _sortIndices[i];
            _tempParticles[i] = _particles[srcIdx];
            _tempSizes[i] = _baseSizes[srcIdx];
        }

        // Copy back to main arrays
        Array.Copy(_tempParticles, 0, _particles, 0, count);
        Array.Copy(_tempSizes, 0, _baseSizes, 0, count);
    }

    /// <summary>
    /// Iterate over all alive particles
    /// </summary>
    public ParticleEnumerator GetEnumerator()
    {
        return new ParticleEnumerator(_particles, _aliveCount);
    }

    /// <summary>
    /// Custom enumerator for efficient particle iteration
    /// </summary>
    public ref struct ParticleEnumerator
    {
        private readonly Span<Particle> _particles;
        private int _index;

        internal ParticleEnumerator(Particle[] particles, int count)
        {
            _particles = particles.AsSpan(0, count);
            _index = -1;
        }

        public ref Particle Current => ref _particles[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _particles.Length;
        }

        public void Reset()
        {
            _index = -1;
        }
    }
}

/// <summary>
/// Global particle pool manager for shared particle rendering
/// </summary>
public class ParticlePoolManager
{
    private readonly Dictionary<string, ParticlePool> _namedPools = new();
    private readonly List<ParticlePool> _allPools = new();
    private ParticleRenderData[] _renderBuffer;
    private int _totalMaxParticles;

    public ParticlePoolManager(int maxTotalParticles = 100000)
    {
        _totalMaxParticles = maxTotalParticles;
        _renderBuffer = new ParticleRenderData[maxTotalParticles];
    }

    /// <summary>
    /// Create or get a named pool
    /// </summary>
    public ParticlePool GetOrCreatePool(string name, int capacity)
    {
        if (!_namedPools.TryGetValue(name, out var pool))
        {
            pool = new ParticlePool(capacity);
            _namedPools[name] = pool;
            _allPools.Add(pool);
        }
        return pool;
    }

    /// <summary>
    /// Create an anonymous pool
    /// </summary>
    public ParticlePool CreatePool(int capacity)
    {
        var pool = new ParticlePool(capacity);
        _allPools.Add(pool);
        return pool;
    }

    /// <summary>
    /// Get total number of alive particles across all pools
    /// </summary>
    public int TotalAliveCount
    {
        get
        {
            int total = 0;
            foreach (var pool in _allPools)
                total += pool.AliveCount;
            return total;
        }
    }

    /// <summary>
    /// Collect all render data from all pools into a single buffer
    /// </summary>
    public ReadOnlySpan<ParticleRenderData> CollectRenderData()
    {
        int offset = 0;
        foreach (var pool in _allPools)
        {
            if (offset >= _renderBuffer.Length)
                break;

            offset += pool.CopyRenderData(_renderBuffer, offset);
        }

        return _renderBuffer.AsSpan(0, offset);
    }

    /// <summary>
    /// Remove a pool
    /// </summary>
    public void RemovePool(ParticlePool pool)
    {
        _allPools.Remove(pool);
        
        // Remove from named pools if present
        string? keyToRemove = null;
        foreach (var kvp in _namedPools)
        {
            if (kvp.Value == pool)
            {
                keyToRemove = kvp.Key;
                break;
            }
        }
        
        if (keyToRemove != null)
            _namedPools.Remove(keyToRemove);
    }

    /// <summary>
    /// Clear all pools
    /// </summary>
    public void ClearAll()
    {
        foreach (var pool in _allPools)
            pool.Clear();
    }
}
