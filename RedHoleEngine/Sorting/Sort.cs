namespace RedHoleEngine.Sorting;

public class Sort
{
    /// <summary>
    /// copies one section of an array to the same position in another
    /// </summary>
    /// <param name="a">array to copy from</param>
    /// <param name="start">start index</param>
    /// <param name="end">end index</param>
    /// <param name="b">array to copy to</param>
    private static void CopyArraySectionI32(Int32[] a, Int32 start, Int32 end, Int32[] b)
    {
        for (int i = start; i < end; i++)
        {
            b[i] = a[i];
        }
    }

    private static void TopDownMergeI32(Int32[] a, Int32 start, Int32 middle, Int32 end, Int32[] b)
    {
        int i = start;
        int j = middle;
        
        //merge the two sorted runs into b
        for (int k = start; k < end; k++)
        {
            if (i < middle && (j >= end || a[i] < a[j]))
            {
                b[k] = a[i];
                i++;
            }
            else
            {
                b[k] = a[j];
                j++;
            }
        }
    }

    /// <summary>
    /// split the array into two halves, sort both halves into b and merge both back into a
    /// </summary>
    /// <param name="a"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="b"></param>
    private static void TopDownSplitMergeI32(Int32[] a, Int32 start, Int32 end, Int32[] b)
    {
        if (end - start <= 1)
        {
            return;
        }
        
        int middle = (start + end) / 2;
        
        // recursively sort
        TopDownSplitMergeI32(a, start, middle, b);
        TopDownSplitMergeI32(a, middle, end, b);
        
        //merge the sorted halves into b
        TopDownMergeI32(a, start, middle, end, b);
        
        //copy the merged result back into a
        CopyArraySectionI32(b, start, end, a);
    } 
    /// <summary>
    /// 32bit merge sort implementation
    /// </summary>
    /// <param name="a">array to sort</param>
    /// <param name="n">index to sort to</param>
    /// <returns>array sorted to N</returns>
    public static Int32[] TopDownMergeSortI32(Int32[] a, Int32 n)
    {
        //init new array for sorting
        Int32[] b = new Int32[a.Length];
        
        CopyArraySectionI32(a,0, n, b);
        TopDownSplitMergeI32(a, 0, n, b);
        return a;
    }
    /// <summary>
    /// copies one section of an array to the same position in another
    /// </summary>
    /// <param name="a">array to copy from</param>
    /// <param name="start">start index</param>
    /// <param name="end">end index</param>
    /// <param name="b">array to copy to</param>
    private static void CopyArraySectionI64(Int64[] a, Int64 start, Int64 end, Int64[] b)
    {
        for (Int64 i = start; i < end; i++)
        {
            b[i] = a[i];
        }
    }

    private static void TopDownMergeI64(Int64[] a, Int64 start, Int64 middle, Int64 end, Int64[] b)
    {
        Int64 i = start;
        Int64 j = middle;
        
        //merge the two sorted runs into b
        for (Int64 k = start; k < end; k++)
        {
            if (i < middle && (j >= end || a[i] < a[j]))
            {
                b[k] = a[i];
                i++;
            }
            else
            {
                b[k] = a[j];
                j++;
            }
        }
    }

    /// <summary>
    /// split the array into two halves, sort both halves into b and merge both back into a
    /// </summary>
    /// <param name="a"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="b"></param>
    private static void TopDownSplitMergeI64(Int64[] a, Int64 start, Int64 end, Int64[] b)
    {
        if (end - start <= 1)
        {
            return;
        }
        
        Int64 middle = (start + end) / 2;
        
        // recursively sort
        TopDownSplitMergeI64(a, start, middle, b);
        TopDownSplitMergeI64(a, middle, end, b);
        
        //merge the sorted halves into b
        TopDownMergeI64(a, start, middle, end, b);
        
        //copy the merged result back into a
        CopyArraySectionI64(b, start, end, a);
    } 
    /// <summary>
    /// 64bit array merge sort
    /// </summary>
    /// <param name="a">array to sort</param>
    /// <param name="n">index to sort to</param>
    /// <returns>sorted array to the index of N</returns>
    public static Int64[] TopDownMergeSortI64(Int64[] a, Int64 n)
    {
        //init new array for sorting
        Int64[] b = new Int64[a.Length];
        
        CopyArraySectionI64(a,0, n, b);
        TopDownSplitMergeI64(a, 0, n, b);
        return a;
    }
    /// <summary>
    /// copies one section of an array to the same position in another
    /// </summary>
    /// <param name="a">array to copy from</param>
    /// <param name="start">start index</param>
    /// <param name="end">end index</param>
    /// <param name="b">array to copy to</param>
    public static void CopyArraySectionI128(Int128[] a, int start, int end, Int128[] b)
    {
        for (int i = start; i < end; i++)
        {
            b[i] = a[i];
        }
    }

    private static void TopDownMergeI128(Int128[] a, int start, int middle, int end, Int128[] b)
    {
        int i = start;
        int j = middle;
        
        //merge the two sorted runs into b
        for (int k = start; k < end; k++)
        {
            if (i < middle && (j >= end || a[i] < a[j]))
            {
                b[k] = a[i];
                i++;
            }
            else
            {
                b[k] = a[j];
                j++;
            }
        }
    }

    /// <summary>
    /// split the array into two halves, sort both halves into b and merge both back into a
    /// </summary>
    /// <param name="a"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="b"></param>
    private static void TopDownSplitMergeI128(Int128[] a, int start, int end, Int128[] b)
    {
        if (end - start <= 1)
        {
            return;
        }
        
        int middle = (start + end) / 2;
        
        // recursively sort
        TopDownSplitMergeI128(a, start, middle, b);
        TopDownSplitMergeI128(a, middle, end, b);
        
        //merge the sorted halves into b
        TopDownMergeI128(a, start, middle, end, b);
        
        //copy the merged result back into a
        CopyArraySectionI128(b, start, end, a);
    } 
    /// <summary>
    /// 128 bit implementation of merge sort
    /// </summary>
    /// <param name="a">array to sort</param>
    /// <param name="n">index to sort to</param>
    /// <returns>array sorted to N</returns>
    public static Int128[] TopDownMergeSortI128(Int128[] a, int n)
    {
        //init new array for sorting
        Int128[] b = new Int128[a.Length];
        
        CopyArraySectionI128(a,0, n, b);
        TopDownSplitMergeI128(a, 0, n, b);
        return a;
    }
    
}