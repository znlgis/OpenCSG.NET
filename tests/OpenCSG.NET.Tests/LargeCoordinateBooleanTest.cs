using NUnit.Framework;
using Csg;
using static Csg.Solids;

namespace Csg.Test
{
    /// <summary>
    /// 大坐标布尔回归测试（审查项 2）。
    /// 缺陷背景：FuzzyCsgFactory 的顶点/平面键曾用 int 存放「坐标 ÷ 容差(1e-5)」，
    /// |坐标| &gt; 21474.8 时键越界（x64 上统一得到 int.MinValue），相距很远的顶点被错误合并，
    /// 差集/交集退化为 2 个面、体积归零。Union 因先平移居中而侥幸不受影响。
    /// 这些用例在测量坐标（数万）量级校验「结果与原点处同构结果一致」。
    /// </summary>
    [TestFixture]
    public class LargeCoordinateBooleanTest
    {
        const double Tol = 1e-9;

        static readonly Vector3D SurveyOffset = new Vector3D(-49256, 12000, 5);

        static (Solid A, Solid B) PairAt(Vector3D offset)
            => (Cube(1, offset), Cube(1, offset + new Vector3D(0.5, 0, 0)));

        [Test]
        public void Subtract_AtSurveyCoordinates_MatchesOriginResult()
        {
            var (originA, originB) = PairAt(new Vector3D(0, 0, 0));
            var (surveyA, surveyB) = PairAt(SurveyOffset);

            var origin = originA.Subtract(originB);
            var survey = surveyA.Subtract(surveyB);

            Assert.That(survey.Polygons.Count, Is.EqualTo(origin.Polygons.Count),
                "大坐标差集的面数应与原点处一致");
            GeometryAssert.AssertVolume(survey, 0.5, Tol, "大坐标差集");
            GeometryAssert.AssertBounds(survey,
                SurveyOffset + new Vector3D(-0.5, -0.5, -0.5),
                SurveyOffset + new Vector3D(0, 0.5, 0.5), Tol, "大坐标差集");
            GeometryAssert.AssertSolidInvariants(survey, "大坐标差集");
        }

        [Test]
        public void Intersect_AtSurveyCoordinates_MatchesOriginResult()
        {
            var (surveyA, surveyB) = PairAt(SurveyOffset);
            var survey = surveyA.Intersect(surveyB);

            Assert.That(survey.Polygons.Count, Is.EqualTo(6), "大坐标交集应保留 6 个面（原缺陷为 2 个退化面）");
            GeometryAssert.AssertVolume(survey, 0.5, Tol, "大坐标交集");
            GeometryAssert.AssertBounds(survey,
                SurveyOffset + new Vector3D(0, -0.5, -0.5),
                SurveyOffset + new Vector3D(0.5, 0.5, 0.5), Tol, "大坐标交集");
            GeometryAssert.AssertSolidInvariants(survey, "大坐标交集");
        }

        [Test]
        public void Union_AtSurveyCoordinates_MatchesOriginResult()
        {
            var (surveyA, surveyB) = PairAt(SurveyOffset);
            var survey = surveyA.Union(surveyB);

            Assert.That(survey.Polygons.Count, Is.EqualTo(6), "大坐标并集应为 6 个面");
            GeometryAssert.AssertVolume(survey, 1.5, Tol, "大坐标并集");
            GeometryAssert.AssertBounds(survey,
                SurveyOffset + new Vector3D(-0.5, -0.5, -0.5),
                SurveyOffset + new Vector3D(1.0, 0.5, 0.5), Tol, "大坐标并集");
            GeometryAssert.AssertSolidInvariants(survey, "大坐标并集");
        }

        [Test]
        public void Subtract_BeyondIntVertexKeyRange_IsNotDegenerate()
        {
            // x = 30000 → 键值 3.0e9 > int.MaxValue：修复前此例退化为 2 个面 / 体积 0
            var offset = new Vector3D(30000, 0, 0);
            var (a, b) = PairAt(offset);
            var result = a.Subtract(b);

            Assert.That(result.Polygons.Count, Is.EqualTo(6), "越过 int 键值范围后差集退化了");
            GeometryAssert.AssertVolume(result, 0.5, Tol, "越过 int 键值范围的差集");
            GeometryAssert.AssertBounds(result,
                offset + new Vector3D(-0.5, -0.5, -0.5),
                offset + new Vector3D(0, 0.5, 0.5), Tol, "越过 int 键值范围的差集");
        }

        [Test]
        public void Subtract_AtMillionCoordinates_MatchesExpectedVolume()
        {
            var offset = new Vector3D(1.0e6, -2.0e6, 3.0e6);
            var (a, b) = PairAt(offset);
            var result = a.Subtract(b);

            GeometryAssert.AssertVolume(result, 0.5, 1e-6, "百万量级坐标差集");
            GeometryAssert.AssertBounds(result,
                offset + new Vector3D(-0.5, -0.5, -0.5),
                offset + new Vector3D(0, 0.5, 0.5), 1e-6, "百万量级坐标差集");
        }
    }
}
