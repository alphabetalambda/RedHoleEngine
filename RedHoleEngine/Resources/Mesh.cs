using System.Numerics;

namespace RedHoleEngine.Resources;

/// <summary>
/// Vertex data structure for meshes
/// </summary>
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
    public Vector4 Color;
    public Vector4 Tangent;

    public static readonly int SizeInBytes = 
        sizeof(float) * 3 +  // Position
        sizeof(float) * 3 +  // Normal
        sizeof(float) * 2 +  // TexCoord
        sizeof(float) * 4 +  // Color
        sizeof(float) * 4;   // Tangent

    public Vertex(Vector3 position)
    {
        Position = position;
        Normal = Vector3.UnitY;
        TexCoord = Vector2.Zero;
        Color = Vector4.One;
        Tangent = new Vector4(1, 0, 0, 1);
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
        Color = Vector4.One;
        Tangent = new Vector4(1, 0, 0, 1);
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 color)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
        Color = color;
        Tangent = new Vector4(1, 0, 0, 1);
    }
}

/// <summary>
/// Mesh data (CPU-side)
/// </summary>
public class Mesh : IDisposable
{
    /// <summary>
    /// Mesh name/identifier
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Vertex data
    /// </summary>
    public Vertex[] Vertices { get; set; }
    
    /// <summary>
    /// Index data (triangles)
    /// </summary>
    public uint[] Indices { get; set; }
    
    /// <summary>
    /// Axis-aligned bounding box minimum
    /// </summary>
    public Vector3 BoundsMin { get; private set; }
    
    /// <summary>
    /// Axis-aligned bounding box maximum
    /// </summary>
    public Vector3 BoundsMax { get; private set; }
    
    /// <summary>
    /// Bounding sphere center
    /// </summary>
    public Vector3 BoundingSphereCenter { get; private set; }
    
    /// <summary>
    /// Bounding sphere radius
    /// </summary>
    public float BoundingSphereRadius { get; private set; }

    public Mesh(string name = "Mesh")
    {
        Name = name;
        Vertices = Array.Empty<Vertex>();
        Indices = Array.Empty<uint>();
    }

    public Mesh(Vertex[] vertices, uint[] indices, string name = "Mesh")
    {
        Name = name;
        Vertices = vertices;
        Indices = indices;
        RecalculateBounds();
    }

    /// <summary>
    /// Recalculate bounding volumes
    /// </summary>
    public void RecalculateBounds()
    {
        if (Vertices.Length == 0)
        {
            BoundsMin = Vector3.Zero;
            BoundsMax = Vector3.Zero;
            BoundingSphereCenter = Vector3.Zero;
            BoundingSphereRadius = 0;
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var vertex in Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        BoundsMin = min;
        BoundsMax = max;
        BoundingSphereCenter = (min + max) * 0.5f;
        BoundingSphereRadius = Vector3.Distance(BoundingSphereCenter, max);
    }

    /// <summary>
    /// Recalculate normals from triangle faces
    /// </summary>
    public void RecalculateNormals()
    {
        // Reset normals
        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Normal = Vector3.Zero;
        }

        // Accumulate face normals
        for (int i = 0; i < Indices.Length; i += 3)
        {
            uint i0 = Indices[i];
            uint i1 = Indices[i + 1];
            uint i2 = Indices[i + 2];

            var v0 = Vertices[i0].Position;
            var v1 = Vertices[i1].Position;
            var v2 = Vertices[i2].Position;

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var faceNormal = Vector3.Cross(edge1, edge2);

            Vertices[i0].Normal += faceNormal;
            Vertices[i1].Normal += faceNormal;
            Vertices[i2].Normal += faceNormal;
        }

        // Normalize
        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Normal = Vector3.Normalize(Vertices[i].Normal);
        }
    }

    /// <summary>
    /// Calculate tangents for normal mapping
    /// </summary>
    public void RecalculateTangents()
    {
        var tangents = new Vector3[Vertices.Length];
        var bitangents = new Vector3[Vertices.Length];

        for (int i = 0; i < Indices.Length; i += 3)
        {
            uint i0 = Indices[i];
            uint i1 = Indices[i + 1];
            uint i2 = Indices[i + 2];

            var v0 = Vertices[i0];
            var v1 = Vertices[i1];
            var v2 = Vertices[i2];

            var edge1 = v1.Position - v0.Position;
            var edge2 = v2.Position - v0.Position;

            var deltaUV1 = v1.TexCoord - v0.TexCoord;
            var deltaUV2 = v2.TexCoord - v0.TexCoord;

            float f = 1.0f / (deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y);

            var tangent = new Vector3(
                f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X),
                f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y),
                f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z)
            );

            var bitangent = new Vector3(
                f * (-deltaUV2.X * edge1.X + deltaUV1.X * edge2.X),
                f * (-deltaUV2.X * edge1.Y + deltaUV1.X * edge2.Y),
                f * (-deltaUV2.X * edge1.Z + deltaUV1.X * edge2.Z)
            );

            tangents[i0] += tangent;
            tangents[i1] += tangent;
            tangents[i2] += tangent;

            bitangents[i0] += bitangent;
            bitangents[i1] += bitangent;
            bitangents[i2] += bitangent;
        }

        for (int i = 0; i < Vertices.Length; i++)
        {
            var n = Vertices[i].Normal;
            var t = tangents[i];

            // Gram-Schmidt orthogonalize
            var orthoT = Vector3.Normalize(t - n * Vector3.Dot(n, t));

            // Calculate handedness
            float w = Vector3.Dot(Vector3.Cross(n, t), bitangents[i]) < 0 ? -1 : 1;

            Vertices[i].Tangent = new Vector4(orthoT.X, orthoT.Y, orthoT.Z, w);
        }
    }

    #region Primitive Factories

    /// <summary>
    /// Create a unit cube centered at origin
    /// </summary>
    public static Mesh CreateCube(float size = 1f)
    {
        float h = size * 0.5f;
        
        var vertices = new Vertex[]
        {
            // Front face
            new(new Vector3(-h, -h,  h), new Vector3(0, 0, 1), new Vector2(0, 0)),
            new(new Vector3( h, -h,  h), new Vector3(0, 0, 1), new Vector2(1, 0)),
            new(new Vector3( h,  h,  h), new Vector3(0, 0, 1), new Vector2(1, 1)),
            new(new Vector3(-h,  h,  h), new Vector3(0, 0, 1), new Vector2(0, 1)),
            
            // Back face
            new(new Vector3( h, -h, -h), new Vector3(0, 0, -1), new Vector2(0, 0)),
            new(new Vector3(-h, -h, -h), new Vector3(0, 0, -1), new Vector2(1, 0)),
            new(new Vector3(-h,  h, -h), new Vector3(0, 0, -1), new Vector2(1, 1)),
            new(new Vector3( h,  h, -h), new Vector3(0, 0, -1), new Vector2(0, 1)),
            
            // Top face
            new(new Vector3(-h,  h,  h), new Vector3(0, 1, 0), new Vector2(0, 0)),
            new(new Vector3( h,  h,  h), new Vector3(0, 1, 0), new Vector2(1, 0)),
            new(new Vector3( h,  h, -h), new Vector3(0, 1, 0), new Vector2(1, 1)),
            new(new Vector3(-h,  h, -h), new Vector3(0, 1, 0), new Vector2(0, 1)),
            
            // Bottom face
            new(new Vector3(-h, -h, -h), new Vector3(0, -1, 0), new Vector2(0, 0)),
            new(new Vector3( h, -h, -h), new Vector3(0, -1, 0), new Vector2(1, 0)),
            new(new Vector3( h, -h,  h), new Vector3(0, -1, 0), new Vector2(1, 1)),
            new(new Vector3(-h, -h,  h), new Vector3(0, -1, 0), new Vector2(0, 1)),
            
            // Right face
            new(new Vector3( h, -h,  h), new Vector3(1, 0, 0), new Vector2(0, 0)),
            new(new Vector3( h, -h, -h), new Vector3(1, 0, 0), new Vector2(1, 0)),
            new(new Vector3( h,  h, -h), new Vector3(1, 0, 0), new Vector2(1, 1)),
            new(new Vector3( h,  h,  h), new Vector3(1, 0, 0), new Vector2(0, 1)),
            
            // Left face
            new(new Vector3(-h, -h, -h), new Vector3(-1, 0, 0), new Vector2(0, 0)),
            new(new Vector3(-h, -h,  h), new Vector3(-1, 0, 0), new Vector2(1, 0)),
            new(new Vector3(-h,  h,  h), new Vector3(-1, 0, 0), new Vector2(1, 1)),
            new(new Vector3(-h,  h, -h), new Vector3(-1, 0, 0), new Vector2(0, 1)),
        };

        var indices = new uint[]
        {
            0, 1, 2, 0, 2, 3,       // Front
            4, 5, 6, 4, 6, 7,       // Back
            8, 9, 10, 8, 10, 11,   // Top
            12, 13, 14, 12, 14, 15, // Bottom
            16, 17, 18, 16, 18, 19, // Right
            20, 21, 22, 20, 22, 23  // Left
        };

        return new Mesh(vertices, indices, "Cube");
    }

    /// <summary>
    /// Create a UV sphere
    /// </summary>
    public static Mesh CreateSphere(float radius = 1f, int segments = 32, int rings = 16)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;
            float y = MathF.Cos(phi) * radius;
            float ringRadius = MathF.Sin(phi) * radius;

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = 2 * MathF.PI * seg / segments;
                float x = MathF.Cos(theta) * ringRadius;
                float z = MathF.Sin(theta) * ringRadius;

                var pos = new Vector3(x, y, z);
                var normal = Vector3.Normalize(pos);
                var uv = new Vector2((float)seg / segments, (float)ring / rings);

                vertices.Add(new Vertex(pos, normal, uv));
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                uint current = (uint)(ring * (segments + 1) + seg);
                uint next = current + (uint)segments + 1;

                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        return new Mesh(vertices.ToArray(), indices.ToArray(), "Sphere");
    }

    /// <summary>
    /// Create a plane (facing up)
    /// </summary>
    public static Mesh CreatePlane(float width = 1f, float depth = 1f, int segmentsX = 1, int segmentsZ = 1)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        for (int z = 0; z <= segmentsZ; z++)
        {
            for (int x = 0; x <= segmentsX; x++)
            {
                float px = -hw + width * x / segmentsX;
                float pz = -hd + depth * z / segmentsZ;
                float u = (float)x / segmentsX;
                float v = (float)z / segmentsZ;

                vertices.Add(new Vertex(
                    new Vector3(px, 0, pz),
                    Vector3.UnitY,
                    new Vector2(u, v)
                ));
            }
        }

        for (int z = 0; z < segmentsZ; z++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                uint current = (uint)(z * (segmentsX + 1) + x);
                uint next = current + (uint)segmentsX + 1;

                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        var mesh = new Mesh(vertices.ToArray(), indices.ToArray(), "Plane");
        mesh.RecalculateTangents();
        return mesh;
    }

    #endregion

    public void Dispose()
    {
        Vertices = Array.Empty<Vertex>();
        Indices = Array.Empty<uint>();
    }
}
