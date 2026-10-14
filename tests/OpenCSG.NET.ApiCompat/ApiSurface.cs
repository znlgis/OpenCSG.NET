// netstandard2.0 消费面探针：本工程不产出可执行文件，唯一目的是让编译器
// 从 netstandard2.0 侧校验库的公共 API 面。任何 API 不再可用 ⇒ 本工程编译失败。
// 所有返回值都用弃元 `_` 消费：本工程开了 TreatWarningsAsErrors，
// 命名局部变量"已赋值未使用"会触发 CS0219 而变成错误。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Csg;

namespace OpenCSG.NET.ApiCompat
{
    internal static class ApiSurface
    {
        internal static void TouchPrimitives()
        {
            var cube = Solids.Cube(1, center: true);
            var box = Solids.Cube(new Vector3D(1, 2, 3), center: true);
            var sphere = Solids.Sphere(1);
            var cylinder = Solids.Cylinder(0.5, 2);
            var cylinderOptions = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -1, 0),
                End = new Vector3D(0, 1, 0),
                RadiusStart = 1,
                RadiusEnd = 0,
                SectorAngle = 360,
                Resolution = 12,
            });
            var sphereOptions = Solids.Sphere(new SphereOptions
            {
                XAxis = new Vector3D(1, 0, 0),
                YAxis = new Vector3D(0, -1, 0),
                ZAxis = new Vector3D(0, 0, 1),
                Center = new Vector3D(0, 0, 0),
                Radius = 1,
                Resolution = 12,
            });
            _ = box.Polygons.Count + cylinderOptions.Polygons.Count + sphereOptions.Polygons.Count;

            var combined = Solids.Union(cube, sphere);
            var cut = Solids.Difference(combined, cylinder);
            var shared = Solids.Intersection(cut, combined);

            var moved = shared.Translate(x: 1, y: 2, z: 3).RotateX(10).RotateY(20).RotateZ(45).Scale(2);
            var mirrored = moved.Scale(-1, 1, 1);
            var scaledByVector = mirrored.Scale(new Vector3D(1, 1, 1));
            var transformed = scaledByVector.Transform(Matrix4x4.RotationX(30));

            if (transformed.Polygons.Count > 0)
            {
                var plane = transformed.Polygons[0].Plane;
                _ = plane.Normal.X + plane.W + plane.Flipped().W;
            }

            _ = Matrix4x4.Scaling(new Vector3D(2, 2, 2)).IsMirroring;
            _ = Matrix4x4.Translation(new Vector3D(1, 2, 3)).IsMirroring;
            _ = (Matrix4x4.RotationX(30) * Matrix4x4.RotationY(30)).Elements.Length;
        }

        internal static void TouchNodeTree()
        {
            CsgNode tree = CsgNodes.Union(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Cylinder(new Vector3D(2, 0, 0), 0.5, 1),
                CsgNodes.Cone(new Vector3D(4, 0, 0), 0.2, 0.8, 1),
                CsgNodes.Extrude(Profiles.HBeam(100, 80, 6, 8), 20),
                CsgNodes.Wedge(new Vector3D(6, 0, 0), 1, 1, 1),
                CsgNodes.Transform(new Vector3D(1, 1, 1), new Vector3D(0, 0, 45),
                    CsgNodes.Sphere(new Vector3D(8, 0, 0), 0.5)));

            var subtract = CsgNodes.Subtract(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Sphere(new Vector3D(0, 0, 0), 0.4));
            var intersect = CsgNodes.Intersect(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Sphere(new Vector3D(0, 0, 0), 0.6));

            var solid = CsgEvaluator.Evaluate(tree);
            var all = CsgEvaluator.EvaluateAll(new List<CsgNode> { tree, subtract, intersect });

            var json = CsgSerialization.ToJson(tree);
            var compact = CsgSerialization.ToJson(tree, indented: false);
            var roundTripped = CsgSerialization.FromJson(compact);
            var arrayJson = CsgSerialization.ToJson(new List<CsgNode> { tree }, indented: false);
            var array = CsgSerialization.FromJsonArray(arrayJson);

            _ = new Profile2D[] { Profiles.Rectangle(1, 1), Profiles.Channel(1, 1, 1, 1) }.Length;
            _ = new Profile2D[] { Profiles.SquareTube(1, 1, 1), Profiles.Trapezoid(1, 2, 1) }.Length;
            _ = new Profile2D[] { Profiles.Capsule(1, 0.5), Profiles.LShape(1, 1, 1) }.Length;
            _ = new Profile2D[]
            {
                new PolygonProfile(new Vector2D[] { new Vector2D(0, 0), new Vector2D(1, 0), new Vector2D(1, 1) }),
            }.Length;

            _ = all.Count + array.Count + json.Length + solid.Polygons.Count + roundTripped.GetHashCode();
        }

        internal static void TouchMeshIo(string directory)
        {
            var solid = Solids.Cube(10, center: true);

            var triangles = CsgMesh.TrianglesOf(solid);
            var many = CsgMesh.TrianglesOf(new List<Solid> { solid });

            var binary = StlWriter.Write(Path.Combine(directory, "api-compat-binary.stl"), triangles);
            var ascii = StlWriter.Write(Path.Combine(directory, "api-compat-ascii.stl"),
                triangles, StlFormat.Ascii, "api-compat");

            _ = binary.TriangleCount + binary.Bytes + ascii.TriangleCount + many.Count;
            _ = StlWriter.NormalOf(triangles[0]).X;

            using (var textWriter = new StringWriter())
            {
                solid.WriteStl("api-compat", textWriter);
                _ = textWriter.ToString().Length;
            }

            using (var polygonWriter = new StringWriter())
            {
                solid.Polygons[0].WriteStl(polygonWriter);
                _ = polygonWriter.ToString().Length;
            }

            _ = new Vector3D(1, 2, 3).ToStlString();
            _ = solid.Polygons[0].Vertices[0].ToStlString();
            _ = solid.ToStlString("api-compat");

            _ = ViewMeshReader.ReadStlFile(binary.Path).Count;
        }

        internal static void TouchViews()
        {
            var solid = Solids.Cube(10, center: true);
            var mesh = CsgMesh.TrianglesOf(solid);

            var triangles = new List<ViewTriangle>();
            foreach (var triangle in mesh)
                triangles.Add(new ViewTriangle(
                    new ViewPoint(triangle.A.X, triangle.A.Y, triangle.A.Z),
                    new ViewPoint(triangle.B.X, triangle.B.Y, triangle.B.Z),
                    new ViewPoint(triangle.C.X, triangle.C.Y, triangle.C.Z)));

            var topology = new ViewMeshTopology(triangles);
            _ = topology.Edges.Count + topology.Points.Count;

            var projector = new ViewProjector(triangles);
            var views = projector.Project(ViewDirections.OrthographicThree);
            var single = projector.ProjectView(ViewDirections.Front);

            var bundle = new OrthographicViews("api-compat", "API 兼容探针", views);
            _ = bundle.Bounds.Width + bundle.Views.Count;
            _ = single.VisibleCount + single.HiddenCount + single.ArcCount;
            _ = projector.TriangleCount;

            var segments = new List<ViewSegment>
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(1, 0), new ViewPoint2(1, 1), false),
            };
            var curves = ViewArcFitter.Fit(segments);
            var merged = ViewArcFitter.MergeConcyclic(curves.Arcs);
            var cleaned = ViewSegmentCleanup.Clean(segments, new ViewBounds2(0, 0, 1, 1));

            _ = curves.Arcs.Count + curves.Segments.Count + merged.Count + cleaned.Count;
            _ = ViewBounds2.Of(new[] { new ViewPoint2(0, 0) }).Width;
            _ = ViewBounds2.Empty.Offset(1, 1).Union(new ViewBounds2(0, 0, 1, 1)).Height;
        }

        internal static void TouchHelpers()
        {
            _ = CsgHelpers.Place(CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 0));
            _ = CsgHelpers.RotateAboutCenter(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 45));
            _ = CsgHelpers.PrismXY(
                new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1) }, 0, 1);
            _ = CsgHelpers.PrismXZ(
                new List<(double X, double Z)> { (0, 0), (1, 0), (1, 1) }, 0, 1);
            _ = CsgHelpers.CylinderAlongX(new Vector3D(0, 0, 0), 1, 2);
            _ = CsgHelpers.CylinderAlongZ(new Vector3D(0, 0, 0), 1, 2);
            _ = CsgHelpers.CornerWedge(0, 0, 1, 1, 1, 0, 1);
            _ = CsgHelpers.CornerCut(0, 0, 0, 1, 1);
            _ = CsgHelpers.TabbedPlateSlantAngleDeg(10, 0, 1, 0.5);
            _ = CsgHelpers.TabbedPlate(0, 1, 0, 10, 0, 1, 0.5, 0.2);
            _ = CsgHelpers.RevolvedStack(
                new List<(double Radius, double Y)> { (1, 0), (2, 1), (1, 2) }, 0, 0, 0);
            _ = CsgHelpers.RoundConvexCorner(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)), 1, 1, 0.5, 1, 1, 1);
            _ = CsgHelpers.FilletConcave(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)), 1, 1, 0.5, 1, 1, 1);
            _ = CsgHelpers.ArcSegment(0, 90, 4, 0, 1);
            _ = CsgHelpers.PlaceRotateThenTranslate(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 0));

            var arcPoints = new List<(double X, double Y)> { (0, 0) };
            CsgHelpers.AddArc(arcPoints, 0, 0, 1, 0, 90, 4);
            _ = arcPoints.Count;

            _ = HalfPlaneCut.Build(0, 0, 0, 0, 1, 10, 1, 1);
            _ = HalfPlaneCut.BuildXY(0, 0, 1, 0, 10, 1, 1, 0);

            _ = Tessellation.SegmentsFor(5.0, 0.05);
            _ = Tessellation.SegmentsForArc(5.0, 90, 0.05);
            _ = Tessellation.IsAdaptive;
            Tessellation.ChordTolerance = 0.05;
            var previous = Tessellation.ChordTolerance;
            Tessellation.ChordTolerance = 0;

            _ = PrimitiveConvention.OriginBased.BoxX;
            _ = PrimitiveConvention.FrustumRadii(1, 0);
            _ = previous;
        }

        // record + init 消费面：库的 record（CsgNode / Profile2D 及其全部子类）对外暴露 init
        // 访问器，本方法覆盖两条消费路径——with 表达式与对象初始化器赋值。实测 netstandard2.0
        // 下两条路径都可用：编译器从库的元数据解析 modreq(IsExternalInit)，消费方无需自备
        // polyfill（库内那份 polyfill 是 internal 也不影响消费）。只有消费方要在自己的
        // netstandard2.0 工程里「声明」record 或 init 成员时，才必须自带同名类型（CS0518）。
        internal static void TouchRecordInitSurface()
        {
            var node = CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var resizedViaWith = node with { Size = new Vector3D(2, 2, 2) };
            var rebuiltViaInitializer = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1))
            {
                Size = new Vector3D(3, 3, 3),
            };
            var resizedProfile = Profiles.Rectangle(1, 1) with { Width = 2 };

            _ = resizedViaWith.Size.X + rebuiltViaInitializer.Size.X + resizedProfile.Width;
        }

        // 包围盒/包围球（Plan F / Task 4 补缺：spec §13 点名，此前从未按名引用）：
        // BoundingBox 的两个构造器、Min/Max/Size/Center、operator+、Intersects 与
        // BoundingSphere 的「默认构造 + 对象初始化器」全部从 netstandard2.0 侧钉住。
        internal static void TouchBoundingVolumes()
        {
            var solid = Solids.Cube(new Vector3D(4, 6, 8), center: true);
            var fromVertices = solid.Polygons[0].BoundingBox;
            var fromVerticesSphere = solid.Polygons[0].BoundingSphere;

            var byExtents = new BoundingBox(2, 4, 6);
            var shifted = byExtents + new Vector3D(1, 1, 1);
            var overlaps = shifted.Intersects(fromVertices);
            var explicitSphere = new BoundingSphere { Center = new Vector3D(0, 0, 0), Radius = 2 };

            _ = fromVertices.Min.X + fromVertices.Max.Y + fromVertices.Size.Z + fromVertices.Center.X;
            _ = shifted.Max.Z + byExtents.Min.Z + fromVertices.ToString().Length;
            _ = overlaps;
            _ = fromVerticesSphere.Center.X + fromVerticesSphere.Radius
                + explicitSphere.Center.Y + explicitSphere.Radius;
        }

        // PolygonShared 与 CsgEvaluationException（Plan F / Task 4 补缺：spec §13 点名）：
        // 前者钉住构造器 + Tag/Hash 读数 + 经 Polygon.Shared 往返；后者钉住两构造器
        // 与 catch 形态（Evaluate 的运行期空值守卫抛该异常）。
        internal static void TouchPolygonSharedAndEvaluationFailures()
        {
            var shared = new PolygonShared(null);
            var polygon = new Polygon(
                new List<Vertex>
                {
                    new Vertex(new Vector3D(0, 0, 0), new Vector2D(0, 0)),
                    new Vertex(new Vector3D(1, 0, 0), new Vector2D(1, 0)),
                    new Vertex(new Vector3D(0, 1, 0), new Vector2D(0, 1)),
                },
                shared);
            var reconstructed = new Polygon(polygon.Vertices, polygon.Shared, polygon.Plane);
            _ = shared.Tag + shared.Hash.Length + reconstructed.Shared.Tag;

            CsgEvaluationException? caught = null;
            try
            {
                CsgEvaluator.Evaluate(null!);
            }
            catch (CsgEvaluationException ex)
            {
                caught = ex;
            }

            var wrapped = new CsgEvaluationException("api-compat", new InvalidOperationException("api-compat"));
            var single = new CsgEvaluationException("api-compat");
            _ = (caught?.Message.Length ?? 0) + wrapped.Message.Length
                + (wrapped.InnerException?.Message.Length ?? 0) + single.Message.Length;
        }

        // ViewVector（Plan F / Task 4 补缺：spec §13 点名，此前只经 ViewDirections/ViewTriangle
        // 间接出现）：按名引用构造器、X/Y/Z、Length、两个 Cross、Dot、Normalized、operator*、
        // ToString，并经公开读数（ViewTriangle.Normal、ViewMeshTopology.Normals）走一遍。
        internal static void TouchViewVectorSurface()
        {
            var front = ViewDirections.Front;
            var look = front.Look;
            var toCamera = ViewVector.Cross(front.Right, front.Up);
            var edgeCross = ViewVector.Cross(new ViewPoint(0, 0, 0), new ViewPoint(1, 0, 0));
            var normalized = toCamera.Normalized();
            var scaled = new ViewVector(0, 0, 1) * 2.0;
            var dot = look.Dot(normalized);

            var triangle = new ViewTriangle(
                new ViewPoint(0, 0, 0), new ViewPoint(1, 0, 0), new ViewPoint(0, 1, 0));
            var topology = new ViewMeshTopology(new List<ViewTriangle> { triangle });

            _ = look.X + look.Y + look.Z + look.Length;
            _ = edgeCross.Z + normalized.X + scaled.Y + dot;
            _ = triangle.Normal.Z + topology.Normals.Count + toCamera.ToString().Length;
        }

        // CsgSerialization 的三个读取期辅助 RequireObject/NameEquals/ReadNumber（位于同文件的
        // internal 类型 JsonReadHelpers）在 netstandard2.0 消费侧**不可见**——实测 CS0122
        // 「JsonReadHelpers 不可访问，因为它具有一定的保护级别」（src/ 未授予 IVT 且冻结；
        // 即便上 Microsoft.CodeAnalysis.PublicApiAnalyzers，PublicAPI.Shipped.txt 也只覆盖
        // 可见 API 面，同样盖不住它们）。故本探针按能钉的两层钉：
        //   ① 公开入口 FromJson/FromJsonArray 的输入形态——含 "$type" 判别的对象、PascalCase
        //      分量名（NameEquals 的命中路径）、数值分量（ReadNumber 的读取路径）；
        //   ② 消费侧用库传递的同一 System.Text.Json（8.0.6）自行实现等价校验惯例
        //      （token 类型检查 + 忽略大小写名称比较 + GetDouble），同时钉住
        //      Utf8JsonReader/Utf8JsonWriter/JsonConverter 对 netstandard2.0 消费方可用——
        //      若库将来把 System.Text.Json 改为不可传递（PrivateAssets）或不可消费版本，
        //      本工程立即编译失败。三个辅助的运行期行为由 tests/OpenCSG.NET.Tests 的
        //      CsgSerializationTest（大小写、非对象/非数值 token 抛错路径）覆盖。
        internal static void TouchSerializationReaderSurface()
        {
            var pascalCase = CsgSerialization.FromJson(
                "{\"$type\":\"Box\",\"Center\":{\"X\":1,\"Y\":2,\"Z\":3},\"Size\":{\"x\":1,\"y\":1,\"z\":1}}");
            var fromArray = CsgSerialization.FromJsonArray(
                "[{\"$type\":\"Sphere\",\"Center\":{\"x\":0,\"y\":0,\"z\":0},\"Radius\":1}]");

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new ConsumerVector3DConverter());

            var json = JsonSerializer.Serialize(new Vector3D(1, 2, 3), options);
            var roundTripped = JsonSerializer.Deserialize<Vector3D>(json, options);

            var utf8 = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: default);
            var isObject = reader.Read() && reader.TokenType == JsonTokenType.StartObject;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("x", roundTripped.X);
                writer.WriteEndObject();
            }

            _ = pascalCase.GetType().Name.Length + fromArray.Count;
            _ = roundTripped.Y + (isObject ? 1.0 : 0.0) + stream.Length;
        }

        /// <summary>
        /// 消费侧自建转换器：镜像库内 JsonReadHelpers 三辅助的校验惯例
        /// （RequireObject → token 必须为对象起始；NameEquals → 名称忽略大小写；
        /// ReadNumber → token 必须为数值）。「netstandard2.0 消费方能写这类转换器」
        /// 本身就是被钉住的契约。
        /// </summary>
        private sealed class ConsumerVector3DConverter : JsonConverter<Vector3D>
        {
            public override Vector3D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Vector3D must be a JSON object");

                double x = 0, y = 0, z = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;
                    var name = reader.GetString();
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.Number)
                        throw new JsonException($"Property '{name}' must be a JSON number");
                    var value = reader.GetDouble();
                    if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase)) x = value;
                    else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase)) y = value;
                    else if (string.Equals(name, "z", StringComparison.OrdinalIgnoreCase)) z = value;
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
    }
}
