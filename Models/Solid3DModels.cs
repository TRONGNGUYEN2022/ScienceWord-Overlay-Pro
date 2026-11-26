using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ScienceWordOverlay.Models
{
    public enum CanvasBackgroundMode
    {
        Transparent = 0,
        ChalkboardGrid = 1,
        WhiteboardPlain = 2,
        WhiteboardGrid = 3,
        Blackboard = 4
    }

    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3 Normalized() { double l = Length; return l > 1e-6 ? new(X / l, Y / l, Z / l) : new(0, 0, 1); }
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    public class Vertex3D
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string? HostEdgeV1 { get; set; }
        public string? HostEdgeV2 { get; set; }
        public double HostT { get; set; } = 0.5;
        public Vec3 Pos => new(X, Y, Z);
    }

    public class Edge3D
    {
        public string V1Id { get; set; } = "";
        public string V2Id { get; set; } = "";
        public bool IsConstruction { get; set; } = false;
        public string? CustomColorHex { get; set; }
    }

    public class Face3D
    {
        public List<string> VertexIds { get; set; } = new();
    }

    public class Section3D
    {
        public List<string> VertexIds { get; set; } = new();
        public string ColorHex { get; set; } = "#FACC15";
        public double Opacity { get; set; } = 0.42;
    }

    public class Solid3DScene
    {
        public string Name { get; set; } = "Chóp S.ABCD";
        public Point CenterScreen { get; set; } = new(420, 420);
        public double Scale { get; set; } = 135.0;
        public double YawDeg { get; set; } = -28.0;
        public double PitchDeg { get; set; } = 22.0;
        public double SplitAmount { get; set; } = 0.0;
        public bool AutoHiddenLines { get; set; } = true;

        public List<Vertex3D> Vertices { get; set; } = new();
        public List<Edge3D> Edges { get; set; } = new();
        public List<Face3D> Faces { get; set; } = new();
        public List<Section3D> Sections { get; set; } = new();
        public int NextIdx { get; set; } = 1;

        public Vertex3D? GetV(string id) => Vertices.FirstOrDefault(v => v.Id == id);

        public Vec3 RotatePoint(Vec3 p)
        {
            double ry = YawDeg * Math.PI / 180.0;
            double rx = PitchDeg * Math.PI / 180.0;
            double x1 = p.X * Math.Cos(ry) + p.Z * Math.Sin(ry);
            double y1 = p.Y;
            double z1 = -p.X * Math.Sin(ry) + p.Z * Math.Cos(ry);
            return new Vec3(x1, y1 * Math.Cos(rx) - z1 * Math.Sin(rx), y1 * Math.Sin(rx) + z1 * Math.Cos(rx));
        }

        public Point Project(Vec3 p, out double depthZ)
        {
            Vec3 r = RotatePoint(p);
            double persp = 1.0 / (1.0 - r.Z * 0.09);
            depthZ = r.Z;
            return new Point(CenterScreen.X + r.X * Scale * persp, CenterScreen.Y - r.Y * Scale * persp);
        }

        public bool TryGetCutPlane(out Vec3 origin, out Vec3 normal)
        {
            origin = new Vec3(0, 0, 0);
            normal = new Vec3(0, 1, 0);
            var sec = Sections.LastOrDefault();
            if (sec == null || sec.VertexIds.Count < 3) return false;
            var a = GetV(sec.VertexIds[0]); var b = GetV(sec.VertexIds[1]); var c = GetV(sec.VertexIds[2]);
            if (a == null || b == null || c == null) return false;
            origin = a.Pos;
            normal = Vec3.Cross(b.Pos - a.Pos, c.Pos - a.Pos).Normalized();
            if (normal.Y < 0) normal = normal * -1.0;
            return normal.Length > 0.5;
        }

        public Vec3 GetEffectivePos(Vertex3D v, bool applySplit)
        {
            Vec3 p = v.Pos;
            if (applySplit && SplitAmount > 0.01 && TryGetCutPlane(out Vec3 o, out Vec3 n))
            {
                if (Vec3.Dot(p - o, n) > 0.04) p = p + n * (SplitAmount * 0.85);
            }
            return p;
        }

        public bool IsEdgeHidden(Edge3D edge)
        {
            if (!AutoHiddenLines) return false;
            var adjFaces = Faces.Where(f => f.VertexIds.Contains(edge.V1Id) && f.VertexIds.Contains(edge.V2Id)).ToList();
            if (adjFaces.Count == 0)
            {
                var v1 = GetV(edge.V1Id); var v2 = GetV(edge.V2Id);
                if (v1 == null || v2 == null) return false;
                return RotatePoint((v1.Pos + v2.Pos) * 0.5).Z < -0.15;
            }
            foreach (var f in adjFaces) if (IsFaceFrontFacing(f)) return false;
            return true;
        }

        public bool IsFaceFrontFacing(Face3D f)
        {
            if (f.VertexIds.Count < 3) return true;
            var v0 = GetV(f.VertexIds[0]); var v1 = GetV(f.VertexIds[1]); var v2 = GetV(f.VertexIds[2]);
            if (v0 == null || v1 == null || v2 == null) return true;
            Vec3 p0 = RotatePoint(v0.Pos), p1 = RotatePoint(v1.Pos), p2 = RotatePoint(v2.Pos);
            return Vec3.Cross(p1 - p0, p2 - p0).Z > 0;
        }

        public Vertex3D? ConstructParallelEdge3D(string fromVertexId, Edge3D refEdge, Func<string> labelGen)
        {
            var m = GetV(fromVertexId);
            var u1 = GetV(refEdge.V1Id); var u2 = GetV(refEdge.V2Id);
            if (m == null || u1 == null || u2 == null) return null;

            Vec3 dir = (u2.Pos - u1.Pos).Normalized();
            Vertex3D? bestHit = null;
            double bestDist = 0.09;

            foreach (var e in Edges)
            {
                if (e == refEdge || e.V1Id == fromVertexId || e.V2Id == fromVertexId) continue;
                var a = GetV(e.V1Id); var b = GetV(e.V2Id);
                if (a == null || b == null) continue;

                Vec3 d2 = b.Pos - a.Pos, w0 = m.Pos - a.Pos;
                double aDot = Vec3.Dot(dir, dir), bDot = Vec3.Dot(dir, d2), cDot = Vec3.Dot(d2, d2);
                double dDot = Vec3.Dot(dir, w0), eDot = Vec3.Dot(d2, w0);
                double denom = aDot * cDot - bDot * bDot;
                if (Math.Abs(denom) < 1e-6) continue;

                double s = (bDot * eDot - cDot * dDot) / denom;
                double t = (aDot * eDot - bDot * dDot) / denom;
                if (t < 0.02 || t > 0.98 || Math.Abs(s) < 0.05) continue;

                double gap = ((m.Pos + dir * s) - (a.Pos + d2 * t)).Length;
                if (gap < bestDist)
                {
                    bestDist = gap;
                    Vec3 pSeg = a.Pos + d2 * t;
                    bestHit = new Vertex3D
                    {
                        Id = $"v3_{NextIdx++}", Label = labelGen(),
                        X = pSeg.X, Y = pSeg.Y, Z = pSeg.Z,
                        HostEdgeV1 = e.V1Id, HostEdgeV2 = e.V2Id, HostT = t
                    };
                }
            }

            if (bestHit != null)
            {
                Vertices.Add(bestHit);
                Edges.Add(new Edge3D { V1Id = fromVertexId, V2Id = bestHit.Id, IsConstruction = true });
            }
            return bestHit;
        }

        public static Solid3DScene CreatePreset(string kind, Point center)
        {
            var s = new Solid3DScene { CenterScreen = center };
            void AddV(string id, string lbl, double x, double y, double z) =>
                s.Vertices.Add(new Vertex3D { Id = id, Label = lbl, X = x, Y = y, Z = z });
            void AddE(string a, string b) => s.Edges.Add(new Edge3D { V1Id = a, V2Id = b });
            void AddF(params string[] vs) => s.Faces.Add(new Face3D { VertexIds = vs.ToList() });

            if (kind is "pyramid_para" or "pyramid_perp")
            {
                s.Name = kind == "pyramid_perp" ? "Chóp S.ABCD (SA ⊥ đáy)" : "Chóp S.ABCD (đáy hình bình hành)";
                double sx = kind == "pyramid_perp" ? -1.05 : -0.35;
                double sz = kind == "pyramid_perp" ? 0.65 : 0.05;
                AddV("S", "S", sx, 1.35, sz);
                AddV("A", "A", -1.05, -0.65, 0.65); AddV("B", "B", 0.65, -0.65, 0.65);
                AddV("C", "C", 1.15, -0.65, -0.65); AddV("D", "D", -0.55, -0.65, -0.65);
                foreach (var (a, b) in new[] { ("S","A"), ("S","B"), ("S","C"), ("S","D"), ("A","B"), ("B","C"), ("C","D"), ("D","A") }) AddE(a, b);
                AddF("A", "D", "C", "B"); AddF("S", "A", "B"); AddF("S", "B", "C"); AddF("S", "C", "D"); AddF("S", "D", "A");
            }
            else if (kind == "tetra")
            {
                s.Name = "Tứ diện S.ABC";
                AddV("S", "S", -0.15, 1.35, 0.0);
                AddV("A", "A", -1.0, -0.65, 0.65); AddV("B", "B", 0.95, -0.65, 0.65); AddV("C", "C", 0.10, -0.65, -0.95);
                foreach (var (a, b) in new[] { ("S","A"), ("S","B"), ("S","C"), ("A","B"), ("B","C"), ("C","A") }) AddE(a, b);
                AddF("A", "C", "B"); AddF("S", "A", "B"); AddF("S", "B", "C"); AddF("S", "C", "A");
            }
            else if (kind is "cube" or "cuboid")
            {
                s.Name = kind == "cube" ? "Hình lập phương ABCD.A'B'C'D'" : "Hình hộp chữ nhật ABCD.A'B'C'D'";
                double rx = kind == "cube" ? 0.85 : 1.15, ry = 0.85, rz = kind == "cube" ? 0.85 : 0.70;
                AddV("A", "A", -rx, -ry, rz);  AddV("B", "B", rx, -ry, rz);  AddV("C", "C", rx, -ry, -rz);  AddV("D", "D", -rx, -ry, -rz);
                AddV("A1", "A'", -rx, ry, rz); AddV("B1", "B'", rx, ry, rz); AddV("C1", "C'", rx, ry, -rz); AddV("D1", "D'", -rx, ry, -rz);
                foreach (var (a, b) in new[] { ("A","B"),("B","C"),("C","D"),("D","A"),("A1","B1"),("B1","C1"),("C1","D1"),("D1","A1"),("A","A1"),("B","B1"),("C","C1"),("D","D1") }) AddE(a, b);
                AddF("A","D","C","B"); AddF("A1","B1","C1","D1"); AddF("A","B","B1","A1"); AddF("B","C","C1","B1"); AddF("C","D","D1","C1"); AddF("D","A","A1","D1");
            }
            else if (kind == "prism_tri")
            {
                s.Name = "Lăng trụ tam giác đứng ABC.A'B'C'";
                AddV("A", "A", -0.95, -0.85, 0.65); AddV("B", "B", 0.95, -0.85, 0.65); AddV("C", "C", 0.0, -0.85, -0.85);
                AddV("A1", "A'", -0.95, 0.95, 0.65); AddV("B1", "B'", 0.95, 0.95, 0.65); AddV("C1", "C'", 0.0, 0.95, -0.85);
                foreach (var (a, b) in new[] { ("A","B"),("B","C"),("C","A"),("A1","B1"),("B1","C1"),("C1","A1"),("A","A1"),("B","B1"),("C","C1") }) AddE(a, b);
                AddF("A","C","B"); AddF("A1","B1","C1"); AddF("A","B","B1","A1"); AddF("B","C","C1","B1"); AddF("C","A","A1","C1");
            }
            else
            {
                bool isCone = kind == "cone";
                s.Name = isCone ? "Hình nón 3D" : "Hình trụ 3D";
                AddV("O", "O", 0, -0.85, 0);
                if (isCone) AddV("S", "S", 0, 1.25, 0); else AddV("O1", "O'", 0, 0.95, 0);
                int n = 16;
                for (int i = 0; i < n; i++)
                {
                    double ang = 2.0 * Math.PI * i / n, x = 0.95 * Math.Cos(ang), z = 0.95 * Math.Sin(ang);
                    AddV($"b{i}", "", x, -0.85, z);
                    if (!isCone) AddV($"t{i}", "", x, 0.95, z);
                }
                for (int i = 0; i < n; i++)
                {
                    int nxt = (i + 1) % n;
                    AddE($"b{i}", $"b{nxt}");
                    if (isCone) { if (i % 4 == 0) AddE("S", $"b{i}"); AddF("S", $"b{i}", $"b{nxt}"); }
                    else { AddE($"t{i}", $"t{nxt}"); if (i % 4 == 0) AddE($"b{i}", $"t{i}"); AddF($"b{i}", $"b{nxt}", $"t{nxt}", $"t{i}"); }
                }
                if (isCone) AddE("S", "O"); else AddE("O", "O1");
            }
            return s;
        }
    }
}