using System.Diagnostics;
using Xunit.Abstractions;

namespace RedHoleEngine.Tests.Algorithms;

public class MergeSortTests
{
    private readonly ITestOutputHelper _output;

    public MergeSortTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Accuracy

    [Fact]
    public void TopDownMergeSortI32()
    {
        Int32[] testArray = [1,5,23,93,52,100,2,9];
        Int32[] expectedArray = [1,2,5,9,23,52,93,100];
        Int32[] result = Sorting.Sort.TopDownMergeSortI32(testArray, testArray.Length);
        Assert.Equal(expectedArray, result);
        Assert.Equal(expectedArray.Length, result.Length);
    }

    [Fact]
    public void TopDownMergeSortI64()
    {
        Int64[] testArray = [9, 525, 15, 19, 521, 36, 39, 61, 95, 4, 113, 118, 123, 124, 129, 132, 139, 150, 154, 157, 195, 204, 272, 281, 290, 297, 309, 313, 319, 321, 323, 325, 332, 336, 355, 364, 374, 381, 382, 385, 405, 418, 421, 423, 433, 437, 445, 460, 470, 471, 484, 494, 507, 520, 537, 544, 551, 589, 596, 607, 623, 636, 652, 655, 679, 697, 698, 711, 989, 728, 735, 743, 760, 762, 770, 771, 790, 800, 801, 803, 804, 807, 828, 840, 849, 858, 869, 66, 875, 882, 886, 906, 912, 915, 920, 934, 949, 954, 988, 991];
        Int64[] expectedArray = [4, 9, 15, 19, 36, 39, 61, 66, 95, 113, 118, 123, 124, 129, 132, 139, 150, 154, 157, 195, 204, 272, 281, 290, 297, 309, 313, 319, 321, 323, 325, 332, 336, 355, 364, 374, 381, 382, 385, 405, 418, 421, 423, 433, 437, 445, 460, 470, 471, 484, 494, 507, 520, 521, 525, 537, 544, 551, 589, 596, 607, 623, 636, 652, 655, 679, 697, 698, 711, 728, 735, 743, 760, 762, 770, 771, 790, 800, 801, 803, 804, 807, 828, 840, 849, 858, 869, 875, 882, 886, 906, 912, 915, 920, 934, 949, 954, 988, 989, 991];
        Int64[] result = Sorting.Sort.TopDownMergeSortI64(testArray, testArray.Length);
        Assert.Equal(expectedArray, result);
        Assert.Equal(expectedArray.Length, result.Length);
    }

    [Fact]
    public void TopDownMergeSortI128()
    {
        Int128[] testArray = [9, 525, 15, 19, 521, 36, 39, 61, 95, 4, 113, 118, 123, 124, 129, 132, 139, 150, 154, 157, 195, 204, 272, 281, 290, 297, 309, 313, 319, 321, 323, 325, 332, 336, 355, 364, 374, 381, 382, 385, 405, 418, 421, 423, 433, 437, 445, 460, 470, 471, 484, 494, 507, 520, 537, 544, 551, 589, 596, 607, 623, 636, 652, 655, 679, 697, 698, 711, 989, 728, 735, 743, 760, 762, 770, 771, 790, 800, 801, 803, 804, 807, 828, 840, 849, 858, 869, 66, 875, 882, 886, 906, 912, 915, 920, 934, 949, 954, 988, 991];
        Int128[] expectedArray = [4, 9, 15, 19, 36, 39, 61, 66, 95, 113, 118, 123, 124, 129, 132, 139, 150, 154, 157, 195, 204, 272, 281, 290, 297, 309, 313, 319, 321, 323, 325, 332, 336, 355, 364, 374, 381, 382, 385, 405, 418, 421, 423, 433, 437, 445, 460, 470, 471, 484, 494, 507, 520, 521, 525, 537, 544, 551, 589, 596, 607, 623, 636, 652, 655, 679, 697, 698, 711, 728, 735, 743, 760, 762, 770, 771, 790, 800, 801, 803, 804, 807, 828, 840, 849, 858, 869, 875, 882, 886, 906, 912, 915, 920, 934, 949, 954, 988, 989, 991];
        Int128[] result = Sorting.Sort.TopDownMergeSortI128(testArray, testArray.Length);
        Assert.Equal(expectedArray, result);
        Assert.Equal(expectedArray.Length, result.Length);
    }

    #endregion

    #region Performance

    private static Int32[] GenerateRandomArrayI32(int size)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var array = new Int32[size];
        for (int i = 0; i < size; i++)
        {
            array[i] = random.Next();
        }
        return array;
    }

    private static Int64[] GenerateRandomArrayI64(int size)
    {
        var random = new Random(42);
        var array = new Int64[size];
        for (int i = 0; i < size; i++)
        {
            array[i] = random.NextInt64();
        }
        return array;
    }

    private static Int128[] GenerateRandomArrayI128(int size)
    {
        var random = new Random(42);
        var array = new Int128[size];
        for (int i = 0; i < size; i++)
        {
            array[i] = new Int128((ulong)random.NextInt64(), (ulong)random.NextInt64());
        }
        return array;
    }

    [Fact]
    public void TopDownMergeSortI32_Performance_10K()
    {
        const int size = 10_000;
        var testArray = GenerateRandomArrayI32(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI32(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI32 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        // Verify sorted
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI32_Performance_100K()
    {
        const int size = 100_000;
        var testArray = GenerateRandomArrayI32(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI32(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI32 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI32_Performance_1M()
    {
        const int size = 1_000_000;
        var testArray = GenerateRandomArrayI32(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI32(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI32 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI64_Performance_10K()
    {
        const int size = 10_000;
        var testArray = GenerateRandomArrayI64(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI64(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI64 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI64_Performance_100K()
    {
        const int size = 100_000;
        var testArray = GenerateRandomArrayI64(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI64(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI64 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI64_Performance_1M()
    {
        const int size = 1_000_000;
        var testArray = GenerateRandomArrayI64(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI64(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI64 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI128_Performance_10K()
    {
        const int size = 10_000;
        var testArray = GenerateRandomArrayI128(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI128(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI128 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI128_Performance_100K()
    {
        const int size = 100_000;
        var testArray = GenerateRandomArrayI128(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI128(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI128 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    [Fact]
    public void TopDownMergeSortI128_Performance_1M()
    {
        const int size = 1_000_000;
        var testArray = GenerateRandomArrayI128(size);
        
        var stopwatch = Stopwatch.StartNew();
        var result = Sorting.Sort.TopDownMergeSortI128(testArray, testArray.Length);
        stopwatch.Stop();

        _output.WriteLine($"TopDownMergeSortI128 ({size:N0} elements): {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");

        Assert.Equal(size, result.Length);
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1] <= result[i], $"Array not sorted at index {i}");
        }
    }

    #endregion
}
