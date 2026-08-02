using System;
using System.Collections.Generic;
using System.Linq;

namespace Csg
{
    /// <summary>CSG node tree evaluation exception</summary>
    public class CsgEvaluationException : Exception
    {
        public CsgEvaluationException(string message) : base(message) { }
        public CsgEvaluationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Evaluates a CsgNode tree into a Solid.</summary>
    public static class CsgEvaluator
    {
        public static Solid Evaluate(CsgNode node)
        {
            return node switch
            {
                BoxNode n => Solids.Cube(n.Size, n.Center),
                SphereNode n => Solids.Sphere(n.Radius, n.Center),
                CylinderNode n => Solids.Cylinder(n.Radius, n.Height, true),
                ConeNode n => throw new CsgEvaluationException("Cone not yet supported (requires Solid API investigation)"),
                UnionNode n => EvaluateBool(n.Children, (a, b) => a.Union(b)),
                SubtractNode n => EvaluateBool(n.Children, (a, b) => a.Subtract(b)),
                IntersectNode n => EvaluateBool(n.Children, (a, b) => a.Intersect(b)),
                TransformNode n => ApplyTransform(Evaluate(n.Child), n.Translation, n.Rotation),
                ExtrudeNode n => EvaluateExtrude(n),
                WedgeNode n => EvaluateWedge(n),
                _ => throw new CsgEvaluationException($"Unknown node type: {node.GetType().Name}")
            };
        }

        public static IReadOnlyList<Solid> EvaluateAll(IEnumerable<CsgNode> nodes)
        {
            return nodes.Select(Evaluate).ToList();
        }

        private static Solid EvaluateBool(List<CsgNode> children, Func<Solid, Solid, Solid> op)
        {
            if (children == null || children.Count == 0)
                throw new CsgEvaluationException("Boolean node requires at least one child");

            var solids = children.Select(Evaluate).ToList();
            var result = solids[0];
            for (int i = 1; i < solids.Count; i++)
                result = op(result, solids[i]);
            return result;
        }

        private static Solid ApplyTransform(Solid solid, Vector3D translation, Vector3D rotation)
        {
            var result = solid;
            if (translation.X != 0 || translation.Y != 0 || translation.Z != 0)
                result = result.Translate(translation);
            if (rotation.X != 0)
                result = result.RotateX(rotation.X);
            if (rotation.Y != 0)
                result = result.RotateY(rotation.Y);
            if (rotation.Z != 0)
                result = result.RotateZ(rotation.Z);
            return result;
        }

        private static Solid EvaluateExtrude(ExtrudeNode n)
        {
            throw new CsgEvaluationException("Extrude evaluation not yet implemented");
        }

        private static Solid EvaluateWedge(WedgeNode n)
        {
            throw new CsgEvaluationException("Wedge evaluation not yet implemented");
        }
    }
}
