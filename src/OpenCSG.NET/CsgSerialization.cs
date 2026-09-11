using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Csg
{
    public static class CsgSerialization
    {
        static readonly JsonSerializerOptions s_options = CreateOptions();
        static readonly JsonSerializerOptions s_compactOptions = new JsonSerializerOptions(CreateOptions()) { WriteIndented = false };

        static JsonSerializerOptions CreateOptions()
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            opts.Converters.Add(new Vector3DConverter());
            opts.Converters.Add(new Vector2DConverter());
            opts.Converters.Add(new Profile2DConverter());
            opts.Converters.Add(new CsgNodeConverter());
            return opts;
        }

        public static string ToJson(CsgNode node, bool indented = true)
        {
            var opts = indented ? s_options : s_compactOptions;
            return JsonSerializer.Serialize(node, typeof(CsgNode), opts);
        }

        public static CsgNode FromJson(string json)
        {
            var node = JsonSerializer.Deserialize<CsgNode>(json, s_options);
            if (node is null)
                throw new JsonException("Deserialization returned null");
            return node;
        }

        public static string ToJson(IEnumerable<CsgNode> nodes, bool indented = true)
        {
            var opts = indented ? s_options : s_compactOptions;
            return JsonSerializer.Serialize(nodes, opts);
        }

        public static IReadOnlyList<CsgNode> FromJsonArray(string json)
        {
            var nodes = JsonSerializer.Deserialize<List<CsgNode>>(json, s_options);
            if (nodes is null)
                throw new JsonException("Deserialization returned null");
            return nodes;
        }
    }

    /// <summary>读取期共用的校验/取值助手。</summary>
    static class JsonReadHelpers
    {
        /// <summary>要求当前记号是 JSON 对象起始；否则立即抛 JsonException，
        /// 避免转换器在多态/错误类型输入上把读取位置带偏、报出无关错误。</summary>
        public static void RequireObject(ref Utf8JsonReader reader, string typeName)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"{typeName} must be a JSON object");
        }

        /// <summary>属性名比较（不区分大小写）。</summary>
        public static bool NameEquals(string? name, string expected)
            => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

        /// <summary>读取数值型属性值；非数值时抛 JsonException 而非 InvalidOperationException。</summary>
        public static double ReadNumber(ref Utf8JsonReader reader, string? name)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException($"Property '{name}' must be a JSON number");
            return reader.GetDouble();
        }
    }

    sealed class Vector3DConverter : JsonConverter<Vector3D>
    {
        public override Vector3D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonReadHelpers.RequireObject(ref reader, "Vector3D");
            double x = 0, y = 0, z = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    // 分量名不区分大小写：手写/第三方生成的 PascalCase JSON 不再被静默读成 (0,0,0)。
                    if (JsonReadHelpers.NameEquals(prop, "x")) x = JsonReadHelpers.ReadNumber(ref reader, prop);
                    else if (JsonReadHelpers.NameEquals(prop, "y")) y = JsonReadHelpers.ReadNumber(ref reader, prop);
                    else if (JsonReadHelpers.NameEquals(prop, "z")) z = JsonReadHelpers.ReadNumber(ref reader, prop);
                }
            }
            return new Vector3D(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3D value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("z", value.Z);
            writer.WriteEndObject();
        }
    }

    sealed class Vector2DConverter : JsonConverter<Vector2D>
    {
        public override Vector2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonReadHelpers.RequireObject(ref reader, "Vector2D");
            double x = 0, y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    if (JsonReadHelpers.NameEquals(prop, "x")) x = JsonReadHelpers.ReadNumber(ref reader, prop);
                    else if (JsonReadHelpers.NameEquals(prop, "y")) y = JsonReadHelpers.ReadNumber(ref reader, prop);
                }
            }
            return new Vector2D(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vector2D value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteEndObject();
        }
    }

    sealed class Profile2DConverter : JsonConverter<Profile2D>
    {
        public override Profile2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("$type", out var typeProp))
                throw new JsonException("Missing required '$type' discriminator property in Profile2D JSON");
            var typeName = typeProp.GetString();
            var json = root.GetRawText();

            return typeName switch
            {
                "Rectangle"  => JsonSerializer.Deserialize<RectangleProfile>(json, options)!,
                "HBeam"      => JsonSerializer.Deserialize<HBeamProfile>(json, options)!,
                "Channel"    => JsonSerializer.Deserialize<ChannelProfile>(json, options)!,
                "SquareTube" => JsonSerializer.Deserialize<SquareTubeProfile>(json, options)!,
                "Trapezoid"  => JsonSerializer.Deserialize<TrapezoidProfile>(json, options)!,
                "Capsule"    => JsonSerializer.Deserialize<CapsuleProfile>(json, options)!,
                "LShape"     => JsonSerializer.Deserialize<LShapeProfile>(json, options)!,
                _ => throw new JsonException($"Unknown Profile2D $type: {typeName}")
            };
        }

        public override void Write(Utf8JsonWriter writer, Profile2D value, JsonSerializerOptions options)
        {
            var typeName = value switch
            {
                RectangleProfile  => "Rectangle",
                HBeamProfile      => "HBeam",
                ChannelProfile    => "Channel",
                SquareTubeProfile => "SquareTube",
                TrapezoidProfile  => "Trapezoid",
                CapsuleProfile    => "Capsule",
                LShapeProfile     => "LShape",
                _ => throw new JsonException($"Unknown Profile2D type: {value.GetType().Name}")
            };

            var json = JsonSerializer.Serialize(value, value.GetType(), options);
            using var doc = JsonDocument.Parse(json);
            writer.WriteStartObject();
            writer.WriteString("$type", typeName);
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }
    }

    sealed class CsgNodeConverter : JsonConverter<CsgNode>
    {
        public override CsgNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("$type", out var typeProp))
                throw new JsonException("Missing required '$type' discriminator property in CsgNode JSON");
            var typeName = typeProp.GetString();
            var json = root.GetRawText();

            return typeName switch
            {
                "Box"        => JsonSerializer.Deserialize<BoxNode>(json, options)!,
                "Sphere"     => JsonSerializer.Deserialize<SphereNode>(json, options)!,
                "Cylinder"   => JsonSerializer.Deserialize<CylinderNode>(json, options)!,
                "Cone"       => JsonSerializer.Deserialize<ConeNode>(json, options)!,
                "Extrude"    => JsonSerializer.Deserialize<ExtrudeNode>(json, options)!,
                "Wedge"      => JsonSerializer.Deserialize<WedgeNode>(json, options)!,
                "Union"      => JsonSerializer.Deserialize<UnionNode>(json, options)!,
                "Subtract"   => JsonSerializer.Deserialize<SubtractNode>(json, options)!,
                "Intersect"  => JsonSerializer.Deserialize<IntersectNode>(json, options)!,
                "Transform"  => JsonSerializer.Deserialize<TransformNode>(json, options)!,
                _ => throw new JsonException($"Unknown CsgNode $type: {typeName}")
            };
        }

        public override void Write(Utf8JsonWriter writer, CsgNode value, JsonSerializerOptions options)
        {
            var typeName = value switch
            {
                BoxNode        => "Box",
                SphereNode     => "Sphere",
                CylinderNode   => "Cylinder",
                ConeNode       => "Cone",
                ExtrudeNode    => "Extrude",
                WedgeNode      => "Wedge",
                UnionNode      => "Union",
                SubtractNode   => "Subtract",
                IntersectNode  => "Intersect",
                TransformNode  => "Transform",
                _ => throw new JsonException($"Unknown CsgNode type: {value.GetType().Name}")
            };

            var json = JsonSerializer.Serialize(value, value.GetType(), options);
            using var doc = JsonDocument.Parse(json);
            writer.WriteStartObject();
            writer.WriteString("$type", typeName);
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
