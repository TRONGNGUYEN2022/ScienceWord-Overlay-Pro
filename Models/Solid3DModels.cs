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
        public double Opacity { get; set; } = 0.45;
    }

    public class CutPlane3D
    {
        public string Label { get; set; } = "(P)";
        public Vec3 Origin { get; set; } = new(0, 0, 0);
        public Vec3 Normal { get; set; } = new(0, 1, 0);
        public double TiltDeg { get; set; } = 0.0;
        public bool IsVertical90 { get; set; } = false;
        public bool Visible { get; set; } = true;
        public bool IsInteractive { get; set; } = false;
        public string PlaneColorHex { get; set; } = "#38BDF8";
        public string SectionColorHex { get; set; } = "#FACC15";
        public double HalfExtent { get; set; } = 1.65;
        public List<Vec3> DefiningPoints3D { get; set; } = new();

        public Vec3 UnitNormal => Normal.Normalized();

        public double SignedDistance(Vec3 p) => Vec3.Dot(p - Origin, UnitNormal);

        public void GetBasis(out Vec3 u, out Vec3 v)
        {
            Vec3 n = UnitNormal;
            Vec3 refVec = Math.Abs(n.Y) < 0.85 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            u = Vec3.Cross(refVec, n).Normalized();
            v = Vec3.Cross(n, u).Normalized();
        }

        public Vec3[] GetQuadCorners()
        {
            GetBasis(out Vec3 u, out Vec3 v);
            double e = HalfExtent;
            return new[]
            {
                Origin - u * e - v * e,
                Origin + u * e - v * e,
                Origin + u * e + v * e,
                Origin - u * e + v * e
            };
        }
    }

    public class AnalyticalSectionResult
    {
        public bool HasIntersection { get; set; }
        public string ShapeName { get; set; } = "";
        public List<Vec3> Boundary3D { get; set; } = new();
        public bool IsSphereCircle { get; set; }
        public Vec3 SphereFootH { get; set; }
        public double SphereDistD { get; set; }
        public double SphereSectionR { get; set; }
    }

    public class LivePolySectionResult
    {
        public List<Vec3> Points3D { get; set; } = new();
        public List<bool> EdgeVisible { get; set; } = new();
    }

    public class Solid3DScene
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Kind { get; set; } = "poly"; // poly | cone | cylinder | sphere | plane_only
        public string Name { get; set; } = "Chóp S.ABCD";
        public Point CenterScreen { get; set; } = new(440, 420);
        public double Scale { get; set; } = 135.0;
        public double YawDeg { get; set; } = -28.0;
        public double PitchDeg { get; set; } = 22.0;

        // Tach khoi theo mat phang (P): ho tro ca thanh SplitAmount lan keo rieng tung nua (Above / Below)
        public double SplitAmount { get; set; } = 0.0;
        public Vec3 PartOffsetAbove { get; set; } = new(0, 0, 0);
        public Vec3 PartOffsetBelow { get; set; } = new(0, 0, 0);

        public bool AutoHiddenLines { get; set; } = true;
        public bool HasActiveSplit => CutPlane != null && (SplitAmount > 0.01 || PartOffsetAbove.Length > 0.01 || PartOffsetBelow.Length > 0.01);

        // Tinh nang Net (Khai trien trai phang khoi 3D giong GeoGebra Net)
        public double UnfoldProgress { get; set; } = 0.0; // 0.0 (dong) -> 1.0 (trai phang hoan toan)
        public int TopCapHingeIdx { get; set; } = 2;       // Canh ban le de lat nap tren (0..n-1)
        public bool IsNetUnfolded => UnfoldProgress > 0.005;

        public bool IsSolidShaded { get; set; } = true;
        public string SolidColorHex { get; set; } = "#38BDF8";
        public double SolidOpacity { get; set; } = 0.50;
        public double Radius { get; set; } = 0.92;
        public double Height { get; set; } = 1.95;

        public CutPlane3D? CutPlane { get; set; }

        public List<Vertex3D> Vertices { get; set; } = new();
        public List<Edge3D> Edges { get; set; } = new();
        public List<Face3D> Faces { get; set; } = new();
        public List<Section3D> Sections { get; set; } = new();
        public int NextIdx { get; set; } = 1;

        public Vertex3D? GetV(string id) => Vertices.FirstOrDefault(v => v.Id == id);

        // Chuyen 1 diem man hinh (DIP) ve toa do local 3D (x, y, z) tai mat phang z = 0 quanh CenterScreen
        public Vec3 ScreenToLocalApprox(Point screenPt, double zLocal = 0.0)
        {
            double persp = Math.Max(0.35, 1.0 - zLocal * 0.085);
            double sc = Math.Max(20.0, Scale * persp);
            double sx = (screenPt.X - CenterScreen.X) / sc;
            double sy = -(screenPt.Y - CenterScreen.Y) / sc;

            double ry = YawDeg * Math.PI / 180.0;
            double rx = PitchDeg * Math.PI / 180.0;
            // Nghich dao Pitch (rx)
            double cy = sy * Math.Cos(-rx) - zLocal * Math.Sin(-rx);
            double cz = sy * Math.Sin(-rx) + zLocal * Math.Cos(-rx);
            // Nghich dao Yaw (ry)
            double lx = sx * Math.Cos(-ry) + cz * Math.Sin(-ry);
            double lz = -sx * Math.Sin(-ry) + cz * Math.Cos(-ry);
            return new Vec3(lx, cy, lz);
        }

        public Vec3 RotatePoint(Vec3 p)
        {
            double ry = YawDeg * Math.PI / 180.0;
            double rx = PitchDeg * Math.PI / 180.0;
            double x1 = p.X * Math.Cos(ry) + p.Z * Math.Sin(ry);
            double y1 = p.Y;
            double z1 = -p.X * Math.Sin(ry) + p.Z * Math.Cos(ry);
            return new Vec3(x1, y1 * Math.Cos(rx) - z1 * Math.Sin(rx), y1 * Math.Sin(rx) + z1 * Math.Cos(rx));
        }

        public Vec3 GetCameraLocalDir()
        {
            double ry = YawDeg * Math.PI / 180.0;
            double rx = PitchDeg * Math.PI / 180.0;
            return new Vec3(-Math.Cos(rx) * Math.Sin(ry), Math.Sin(rx), Math.Cos(rx) * Math.Cos(ry)).Normalized();
        }

        public Point Project(Vec3 p, out double depthZ)
        {
            Vec3 r = RotatePoint(p);
            double persp = 1.0 / (1.0 - r.Z * 0.085);
            depthZ = r.Z;
            return new Point(CenterScreen.X + r.X * Scale * persp, CenterScreen.Y - r.Y * Scale * persp);
        }

        public Vec3 ComputeCentroid()
        {
            if (Vertices.Count == 0)
            {
                if (CutPlane != null) return CutPlane.Origin;
                return new Vec3(0, 0, 0);
            }
            double sx = 0, sy = 0, sz = 0;
            foreach (var v in Vertices) { sx += v.X; sy += v.Y; sz += v.Z; }
            return new Vec3(sx / Vertices.Count, sy / Vertices.Count, sz / Vertices.Count);
        }

        public Vec3 GetOutwardFaceNormal(Face3D f)
        {
            if (f.VertexIds.Count < 3) return new Vec3(0, 0, 1);
            var v0 = GetV(f.VertexIds[0]); var v1 = GetV(f.VertexIds[1]); var v2 = GetV(f.VertexIds[2]);
            if (v0 == null || v1 == null || v2 == null) return new Vec3(0, 0, 1);
            Vec3 n = Vec3.Cross(v1.Pos - v0.Pos, v2.Pos - v0.Pos).Normalized();
            Vec3 fc = new(0, 0, 0); int cnt = 0;
            foreach (var id in f.VertexIds) { var v = GetV(id); if (v != null) { fc = fc + v.Pos; cnt++; } }
            if (cnt > 0) fc = fc * (1.0 / cnt);
            if (Vec3.Dot(n, fc - ComputeCentroid()) < 0) n = n * -1.0;
            return n;
        }

        public bool IsFaceFrontFacing(Face3D f) => RotatePoint(GetOutwardFaceNormal(f)).Z > 0.0;

        public bool IsVertexOnFace(Vertex3D v, Face3D f)
        {
            if (f.VertexIds.Contains(v.Id)) return true;
            if (!string.IsNullOrEmpty(v.HostEdgeV1) && !string.IsNullOrEmpty(v.HostEdgeV2))
                return f.VertexIds.Contains(v.HostEdgeV1) && f.VertexIds.Contains(v.HostEdgeV2);
            return false;
        }

        public bool IsPointOnFace(Vec3 p, Face3D f, double tol = 0.03)
        {
            if (f.VertexIds.Count < 3) return false;
            var v0 = GetV(f.VertexIds[0]);
            if (v0 == null) return false;
            Vec3 n = GetOutwardFaceNormal(f);
            return Math.Abs(Vec3.Dot(p - v0.Pos, n)) <= tol;
        }

        public bool TryGetCutPlane(out Vec3 origin, out Vec3 normal)
        {
            if (CutPlane != null)
            {
                origin = CutPlane.Origin;
                normal = CutPlane.UnitNormal;
                if (normal.Y < -0.001) normal = normal * -1.0;
                return normal.Length > 0.5;
            }
            origin = new Vec3(0, 0, 0); normal = new Vec3(0, 1, 0);
            var sec = Sections.LastOrDefault();
            if (sec == null || sec.VertexIds.Count < 3) return false;
            var a = GetV(sec.VertexIds[0]); var b = GetV(sec.VertexIds[1]); var c = GetV(sec.VertexIds[2]);
            if (a == null || b == null || c == null) return false;
            origin = a.Pos;
            normal = Vec3.Cross(b.Pos - a.Pos, c.Pos - a.Pos).Normalized();
            if (normal.Y < 0) normal = normal * -1.0;
            return normal.Length > 0.5;
        }

        // Xac dinh 1 diem thuoc nua tren (+1) hay nua duoi (-1) cua mat phang cat (P)
        public int GetPartSideSign(Vec3 p)
        {
            if (!TryGetCutPlane(out Vec3 o, out Vec3 n)) return 0;
            double d = Vec3.Dot(p - o, n);
            if (Math.Abs(d) <= 0.015) return 0;
            return d > 0 ? 1 : -1;
        }

        public Vec3 GetPartOffset(int sideSign)
        {
            Vec3 off = sideSign > 0 ? PartOffsetAbove : (sideSign < 0 ? PartOffsetBelow : new Vec3(0, 0, 0));
            if (sideSign > 0 && SplitAmount > 0.005 && TryGetCutPlane(out _, out Vec3 n))
                off = off + n * (SplitAmount * 0.85);
            return off;
        }

        public void NudgePartByScreenDelta(int sideSign, double dxDip, double dyDip)
        {
            if (sideSign == 0) return;
            double persp = 1.0;
            double sc = Math.Max(20.0, Scale * persp);
            double sx = dxDip / sc;
            double sy = -dyDip / sc;
            double ry = YawDeg * Math.PI / 180.0;
            double rx = PitchDeg * Math.PI / 180.0;
            double cy = sy * Math.Cos(-rx);
            double cz = sy * Math.Sin(-rx);
            double lx = sx * Math.Cos(-ry) + cz * Math.Sin(-ry);
            double lz = -sx * Math.Sin(-ry) + cz * Math.Cos(-ry);
            Vec3 deltaLocal = new(lx, cy, lz);
            if (sideSign > 0) PartOffsetAbove = PartOffsetAbove + deltaLocal;
            else PartOffsetBelow = PartOffsetBelow + deltaLocal;
        }

        public void ResetPartOffsets()
        {
            SplitAmount = 0.0;
            PartOffsetAbove = new Vec3(0, 0, 0);
            PartOffsetBelow = new Vec3(0, 0, 0);
        }

        public Vec3 GetEffectivePos(Vertex3D v, bool applySplit)
        {
            Vec3 p = v.Pos;
            if (!applySplit) return p;
            int side = GetPartSideSign(p);
            if (side != 0) p = p + GetPartOffset(side);
            return p;
        }

        public bool IsEdgeHidden(Edge3D edge)
        {
            if (!AutoHiddenLines) return false;
            var v1 = GetV(edge.V1Id); var v2 = GetV(edge.V2Id);
            if (v1 == null || v2 == null) return false;

            var adjFaces = Faces.Where(f => IsVertexOnFace(v1, f) && IsVertexOnFace(v2, f)).ToList();
            if (adjFaces.Count == 0)
            {
                Vec3 mid = (v1.Pos + v2.Pos) * 0.5;
                var planeFaces = Faces.Where(f => IsPointOnFace(v1.Pos, f) && IsPointOnFace(v2.Pos, f) && IsPointOnFace(mid, f)).ToList();
                if (planeFaces.Count > 0)
                {
                    foreach (var f in planeFaces) if (IsFaceFrontFacing(f)) return false;
                    return true;
                }
                return true;
            }
            foreach (var f in adjFaces) if (IsFaceFrontFacing(f)) return false;
            return true;
        }

        // ===================== DỰNG MẶT PHẲNG BẤT KỲ & TỰ TÍNH THIẾT DIỆN 3D =====================
        public void InitInteractiveCutPlane(string sectionColorHex)
        {
            Vec3 origin = Kind == "poly" ? ComputeCentroid() : new Vec3(0, 0, 0);
            double extent = Kind == "poly" ? 1.65 : Math.Max(Radius * 1.48, 1.38);
            CutPlane = new CutPlane3D
            {
                Label = "(P)",
                Origin = origin,
                Normal = new Vec3(0, 1, 0),
                TiltDeg = 0.0,
                IsVertical90 = false,
                Visible = true,
                IsInteractive = true,
                PlaneColorHex = "#38BDF8",
                SectionColorHex = sectionColorHex,
                HalfExtent = extent
            };
        }

        // Tao mat phang (P) bat ky tu 3 diem 3D (co the nam ngoai khoi 3D hoac do click cham tu do tren khong gian)
        public (bool ok, string msg) CreatePlaneFromArbitrary3Points(Vec3 p1, Vec3 p2, Vec3 p3, string sectionColorHex)
        {
            Vec3 u = p2 - p1, v = p3 - p1;
            Vec3 n = Vec3.Cross(u, v);
            if (n.Length < 1e-4) return (false, "3 điểm thẳng hàng, không xác định được mặt phẳng (P)!");
            n = n.Normalized();
            if (n.Y < -0.001) n = n * -1.0;

            Vec3 centroid = (p1 + p2 + p3) * (1.0 / 3.0);
            double maxDist = Math.Max(1.35, Math.Max((p1 - centroid).Length, Math.Max((p2 - centroid).Length, (p3 - centroid).Length)) * 1.25);

            var plane = new CutPlane3D
            {
                Label = "(P)",
                Origin = centroid,
                Normal = n,
                Visible = true,
                IsInteractive = true,
                PlaneColorHex = "#38BDF8",
                SectionColorHex = sectionColorHex,
                HalfExtent = Math.Clamp(maxDist, 1.35, 2.8)
            };
            plane.DefiningPoints3D.AddRange(new[] { p1, p2, p3 });
            CutPlane = plane;
            ResetPartOffsets();

            if (Kind == "plane_only")
            {
                Name = "Mặt phẳng (P) tự do";
                return (true, "Đã dựng Mặt phẳng (P) qua 3 điểm tự do! Dùng ⬆/⬇, ⟲/⟳ hoặc kéo khối để dịch chuyển.");
            }
            return (true, "Đã dựng Mặt phẳng (P) qua 3 điểm & tự tính thiết diện cắt khối 3D! Có thể kéo rời từng phần hoặc kéo thanh [Tách].");
        }

        public void NudgeCutPlane(double delta)
        {
            if (CutPlane == null) return;
            CutPlane.IsInteractive = true;
            CutPlane.Visible = true;
            Vec3 n = CutPlane.UnitNormal;
            Vec3 nextOrigin = CutPlane.Origin + n * delta;
            double maxLim = Kind == "sphere" ? Radius * 1.15 : (Kind == "plane_only" ? 3.5 : (Kind == "poly" ? 1.85 : Math.Max(Height, Radius) * 1.15));
            Vec3 center = Kind == "poly" ? ComputeCentroid() : new Vec3(0, 0, 0);
            if ((nextOrigin - center).Length <= maxLim)
                CutPlane.Origin = nextOrigin;
        }

        public void TiltCutPlane(double deltaDeg)
        {
            if (CutPlane == null) return;
            CutPlane.IsInteractive = true;
            CutPlane.Visible = true;
            CutPlane.TiltDeg = (CutPlane.TiltDeg + deltaDeg) % 360.0;
            double rad = deltaDeg * Math.PI / 180.0;
            Vec3 n = CutPlane.UnitNormal;
            double ny = n.Y * Math.Cos(rad) - n.Z * Math.Sin(rad);
            double nz = n.Y * Math.Sin(rad) + n.Z * Math.Cos(rad);
            CutPlane.Normal = new Vec3(n.X, ny, nz).Normalized();
            CutPlane.IsVertical90 = Math.Abs(CutPlane.Normal.Y) < 0.05;
        }

        public void SetCutPlaneVertical90()
        {
            if (CutPlane == null) InitInteractiveCutPlane("#FACC15");
            CutPlane!.IsInteractive = true;
            CutPlane.Visible = true;
            CutPlane.Origin = Kind == "poly" ? ComputeCentroid() : new Vec3(0, 0, 0);
            CutPlane.Normal = new Vec3(0, 0, 1);
            CutPlane.TiltDeg = 90.0;
            CutPlane.IsVertical90 = true;
        }

        private Vertex3D? FindExistingVertexNear(Vec3 p, double tol = 0.04)
        {
            Vertex3D? best = null;
            double bestD = tol;
            foreach (var v in Vertices)
            {
                double d = (v.Pos - p).Length;
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        private bool HasEdgeBetween(string aId, string bId) =>
            Edges.Any(e => (e.V1Id == aId && e.V2Id == bId) || (e.V1Id == bId && e.V2Id == aId));

        public (bool ok, string msg) CreatePlaneFrom3Vertices(string v1Id, string v2Id, string v3Id, Func<string> labelGen, string sectionColorHex)
        {
            var a = GetV(v1Id); var b = GetV(v2Id); var c = GetV(v3Id);
            if (a == null || b == null || c == null) return (false, "Không tìm thấy đủ 3 đỉnh đã chọn!");
            Vec3 n = Vec3.Cross(b.Pos - a.Pos, c.Pos - a.Pos);
            if (n.Length < 1e-4) return (false, "3 điểm thẳng hàng, không xác định được mặt phẳng (P)!");
            n = n.Normalized();
            if (n.Y < -0.001) n = n * -1.0;

            var plane = new CutPlane3D
            {
                Label = "(P)",
                Origin = (a.Pos + b.Pos + c.Pos) * (1.0 / 3.0),
                Normal = n,
                Visible = true,
                IsInteractive = false,
                PlaneColorHex = "#38BDF8",
                SectionColorHex = sectionColorHex,
                HalfExtent = 1.68
            };
            CutPlane = plane;
            ResetPartOffsets();
            return BuildPolySectionFromPlane(plane, labelGen, $"({a.Label}{b.Label}{c.Label})");
        }

        public (bool ok, string msg) CreatePlaneFromPointParallelFace(string passVertexId, string f1Id, string f2Id, string f3Id, Func<string> labelGen, string sectionColorHex)
        {
            var m = GetV(passVertexId);
            var a = GetV(f1Id); var b = GetV(f2Id); var c = GetV(f3Id);
            if (m == null || a == null || b == null || c == null) return (false, "Không tìm thấy đủ các điểm đã chọn!");
            Vec3 n = Vec3.Cross(b.Pos - a.Pos, c.Pos - a.Pos);
            if (n.Length < 1e-4) return (false, "3 điểm mặt phẳng chuẩn thẳng hàng!");
            n = n.Normalized();
            if (n.Y < -0.001) n = n * -1.0;

            var plane = new CutPlane3D
            {
                Label = "(P)",
                Origin = m.Pos,
                Normal = n,
                Visible = true,
                IsInteractive = false,
                PlaneColorHex = "#38BDF8",
                SectionColorHex = sectionColorHex,
                HalfExtent = 1.68
            };
            CutPlane = plane;
            ResetPartOffsets();
            return BuildPolySectionFromPlane(plane, labelGen, $"qua {m.Label} // ({a.Label}{b.Label}{c.Label})");
        }

        private (bool ok, string msg) BuildPolySectionFromPlane(CutPlane3D plane, Func<string> labelGen, string desc)
        {
            var picked = new List<Vertex3D>();
            void AddUnique(Vertex3D v)
            {
                if (!picked.Any(x => x.Id == v.Id || (x.Pos - v.Pos).Length < 0.03))
                    picked.Add(v);
            }

            var baseEdges = Edges.Where(e => !e.IsConstruction).ToList();
            foreach (var v in Vertices)
            {
                if (Math.Abs(plane.SignedDistance(v.Pos)) <= 0.025)
                    AddUnique(v);
            }

            int newlyCreated = 0;
            foreach (var e in baseEdges)
            {
                var v1 = GetV(e.V1Id); var v2 = GetV(e.V2Id);
                if (v1 == null || v2 == null) continue;
                double d1 = plane.SignedDistance(v1.Pos);
                double d2 = plane.SignedDistance(v2.Pos);
                if ((d1 > 0.02 && d2 < -0.02) || (d1 < -0.02 && d2 > 0.02))
                {
                    double t = d1 / (d1 - d2);
                    Vec3 pInter = v1.Pos + (v2.Pos - v1.Pos) * t;
                    var existing = FindExistingVertexNear(pInter, 0.045);
                    if (existing != null)
                    {
                        if (string.IsNullOrEmpty(existing.HostEdgeV1))
                        {
                            existing.HostEdgeV1 = e.V1Id;
                            existing.HostEdgeV2 = e.V2Id;
                            existing.HostT = t;
                        }
                        AddUnique(existing);
                    }
                    else
                    {
                        var nv = new Vertex3D
                        {
                            Id = $"v3_{NextIdx++}",
                            Label = labelGen(),
                            X = pInter.X,
                            Y = pInter.Y,
                            Z = pInter.Z,
                            HostEdgeV1 = e.V1Id,
                            HostEdgeV2 = e.V2Id,
                            HostT = t
                        };
                        Vertices.Add(nv);
                        AddUnique(nv);
                        newlyCreated++;
                    }
                }
            }

            if (picked.Count < 3)
                return (true, $"Đã dựng mặt phẳng (P) {desc} (chưa cắt đủ 3 cạnh để tạo đa giác thiết diện).");

            Vec3 center = new(0, 0, 0);
            foreach (var v in picked) center = center + v.Pos;
            center = center * (1.0 / picked.Count);
            plane.Origin = center;

            plane.GetBasis(out Vec3 u, out Vec3 vAxis);
            var ordered = picked
                .OrderBy(vt => Math.Atan2(Vec3.Dot(vt.Pos - center, vAxis), Vec3.Dot(vt.Pos - center, u)))
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var cur = ordered[i];
                var nxt = ordered[(i + 1) % ordered.Count];
                if (!HasEdgeBetween(cur.Id, nxt.Id))
                {
                    Edges.Add(new Edge3D
                    {
                        V1Id = cur.Id,
                        V2Id = nxt.Id,
                        IsConstruction = true,
                        CustomColorHex = plane.SectionColorHex
                    });
                }
            }

            Sections.Add(new Section3D
            {
                VertexIds = ordered.Select(v => v.Id).ToList(),
                ColorHex = plane.SectionColorHex,
                Opacity = 0.48
            });

            string polyName = string.Join("", ordered.Select(v => v.Label));
            return (true, $"Đã dựng (P) {desc} → Thiết diện {polyName} ({ordered.Count} đỉnh, tự thêm {newlyCreated} giao điểm)! Kéo từng phần hoặc [Tách]!");
        }

        public LivePolySectionResult ComputeLivePolySection(CutPlane3D plane)
        {
            var res = new LivePolySectionResult();
            var pts = new List<Vec3>();
            void AddUniquePt(Vec3 p)
            {
                if (!pts.Any(x => (x - p).Length < 0.02)) pts.Add(p);
            }

            var baseEdges = Edges.Where(e => !e.IsConstruction).ToList();
            foreach (var e in baseEdges)
            {
                var v1 = GetV(e.V1Id); var v2 = GetV(e.V2Id);
                if (v1 == null || v2 == null) continue;
                double d1 = plane.SignedDistance(v1.Pos);
                double d2 = plane.SignedDistance(v2.Pos);
                if (Math.Abs(d1) <= 0.015) AddUniquePt(v1.Pos);
                if (Math.Abs(d2) <= 0.015) AddUniquePt(v2.Pos);
                if ((d1 > 0.01 && d2 < -0.01) || (d1 < -0.01 && d2 > 0.01))
                {
                    double t = d1 / (d1 - d2);
                    AddUniquePt(v1.Pos + (v2.Pos - v1.Pos) * t);
                }
            }

            if (pts.Count < 3) return res;

            Vec3 center = new(0, 0, 0);
            foreach (var p in pts) center = center + p;
            center = center * (1.0 / pts.Count);
            plane.GetBasis(out Vec3 u, out Vec3 vAxis);
            var ordered = pts
                .OrderBy(p => Math.Atan2(Vec3.Dot(p - center, vAxis), Vec3.Dot(p - center, u)))
                .ToList();

            res.Points3D = ordered;
            for (int i = 0; i < ordered.Count; i++)
            {
                Vec3 a = ordered[i], b = ordered[(i + 1) % ordered.Count];
                Vec3 mid = (a + b) * 0.5;
                var hostFaces = Faces.Where(f => IsPointOnFace(a, f, 0.04) && IsPointOnFace(b, f, 0.04) && IsPointOnFace(mid, f, 0.04)).ToList();
                bool vis = hostFaces.Any(IsFaceFrontFacing);
                res.EdgeVisible.Add(vis);
            }
            return res;
        }

        // ===================== CẮT TÁCH THỰC 3D THÀNH 2 NỬA ĐỘC LẬP (SUTHERLAND-HODGMAN) =====================
        private static List<Vec3> ClipPolygonByHalfSpace(List<Vec3> poly, Vec3 planeOrigin, Vec3 planeNormal, bool keepPositive)
        {
            var output = new List<Vec3>();
            int n = poly.Count;
            if (n < 3) return output;
            const double eps = 1e-5;

            double Eval(Vec3 p)
            {
                double d = Vec3.Dot(p - planeOrigin, planeNormal);
                return keepPositive ? d : -d;
            }

            for (int i = 0; i < n; i++)
            {
                Vec3 curr = poly[i];
                Vec3 next = poly[(i + 1) % n];
                double dC = Eval(curr), dN = Eval(next);
                bool inC = dC >= -eps, inN = dN >= -eps;

                if (inC && inN)
                {
                    output.Add(next);
                }
                else if (inC && !inN)
                {
                    double denom = dC - dN;
                    if (Math.Abs(denom) > 1e-7) output.Add(curr + (next - curr) * (dC / denom));
                }
                else if (!inC && inN)
                {
                    double denom = dC - dN;
                    if (Math.Abs(denom) > 1e-7) output.Add(curr + (next - curr) * (dC / denom));
                    output.Add(next);
                }
            }
            return output;
        }

        public List<(List<Vec3> pts3d, Vec3 outwardN, int partSide, bool isSectionCap)> ComputeSplitPolyHalves3D()
        {
            var result = new List<(List<Vec3>, Vec3, int, bool)>();
            if (Kind != "poly" || CutPlane == null || !TryGetCutPlane(out Vec3 o, out Vec3 n)) return result;

            Vec3 offAbove = GetPartOffset(1);
            Vec3 offBelow = GetPartOffset(-1);

            foreach (var f in Faces)
            {
                var raw = f.VertexIds.Select(GetV).Where(v => v != null).Select(v => v!.Pos).ToList();
                if (raw.Count < 3) continue;
                Vec3 fn = GetOutwardFaceNormal(f);

                var upper = ClipPolygonByHalfSpace(raw, o, n, keepPositive: true);
                if (upper.Count >= 3)
                    result.Add((upper.Select(p => p + offAbove).ToList(), fn, 1, false));

                var lower = ClipPolygonByHalfSpace(raw, o, n, keepPositive: false);
                if (lower.Count >= 3)
                    result.Add((lower.Select(p => p + offBelow).ToList(), fn, -1, false));
            }

            // Them mat nap thiet dien khep kin cho CA 2 NUA (tren & duoi) de khi keo roi thay ro mat cat!
            var sec = ComputeLivePolySection(CutPlane);
            if (sec.Points3D.Count >= 3)
            {
                result.Add((sec.Points3D.Select(p => p + offAbove).ToList(), n * -1.0, 1, true));
                result.Add((sec.Points3D.Select(p => p + offBelow).ToList(), n, -1, true));
            }
            return result;
        }

        // ===================== GIAO 2 KHỐI 3D (SOLID INTERSECTION) =====================
        public static List<Vec3> ComputeSolidIntersectionPoints(Solid3DScene a, Solid3DScene b)
        {
            var pts = new List<Vec3>();
            void AddPt(Vec3 p)
            {
                if (!pts.Any(q => (q - p).Length < 0.04)) pts.Add(p);
            }

            // Quy doi tu khong gian local cua b sang khong gian local cua a
            Vec3 MapBToA(Vec3 pb)
            {
                Point sp = b.Project(pb, out _);
                return a.ScreenToLocalApprox(sp, pb.Z * 0.5);
            }

            bool InsideApproxA(Vec3 p)
            {
                if (a.Kind == "sphere") return (p - new Vec3(0, 0, 0)).Length <= a.Radius + 0.04;
                if (a.Kind == "cylinder")
                {
                    double h2 = a.Height * 0.5;
                    return Math.Abs(p.Y) <= h2 + 0.04 && Math.Sqrt(p.X * p.X + p.Z * p.Z) <= a.Radius + 0.04;
                }
                if (a.Kind == "cone")
                {
                    double h2 = a.Height * 0.5;
                    if (p.Y < -h2 - 0.04 || p.Y > h2 + 0.04) return false;
                    double frac = Math.Clamp((h2 - p.Y) / Math.Max(0.1, a.Height), 0.0, 1.0);
                    return Math.Sqrt(p.X * p.X + p.Z * p.Z) <= a.Radius * frac + 0.04;
                }
                if (a.Vertices.Count == 0) return false;
                Vec3 min = new(a.Vertices.Min(v => v.X) - 0.03, a.Vertices.Min(v => v.Y) - 0.03, a.Vertices.Min(v => v.Z) - 0.03);
                Vec3 max = new(a.Vertices.Max(v => v.X) + 0.03, a.Vertices.Max(v => v.Y) + 0.03, a.Vertices.Max(v => v.Z) + 0.03);
                return p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && p.Z >= min.Z && p.Z <= max.Z;
            }

            // Lay cac canh cua khoi B (hoac vong tron xich dao/day neu la Cau/Non/Tru)
            var segsB = new List<(Vec3 p1, Vec3 p2)>();
            if (b.Kind == "sphere")
            {
                int n = 48;
                for (int i = 0; i < n; i++)
                {
                    double t0 = 2 * Math.PI * i / n, t1 = 2 * Math.PI * (i + 1) / n;
                    segsB.Add((new Vec3(b.Radius * Math.Cos(t0), 0, b.Radius * Math.Sin(t0)), new Vec3(b.Radius * Math.Cos(t1), 0, b.Radius * Math.Sin(t1))));
                    segsB.Add((new Vec3(b.Radius * Math.Cos(t0), b.Radius * Math.Sin(t0), 0), new Vec3(b.Radius * Math.Cos(t1), b.Radius * Math.Sin(t1), 0)));
                }
            }
            else if (b.Kind is "cone" or "cylinder")
            {
                int n = 48;
                double h2 = b.Height * 0.5;
                for (int i = 0; i < n; i++)
                {
                    double t0 = 2 * Math.PI * i / n, t1 = 2 * Math.PI * (i + 1) / n;
                    Vec3 b0 = new(b.Radius * Math.Cos(t0), -h2, b.Radius * Math.Sin(t0));
                    Vec3 b1 = new(b.Radius * Math.Cos(t1), -h2, b.Radius * Math.Sin(t1));
                    segsB.Add((b0, b1));
                    if (b.Kind == "cone") segsB.Add((new Vec3(0, h2, 0), b0));
                    else
                    {
                        Vec3 u0 = new(b.Radius * Math.Cos(t0), h2, b.Radius * Math.Sin(t0));
                        Vec3 u1 = new(b.Radius * Math.Cos(t1), h2, b.Radius * Math.Sin(t1));
                        segsB.Add((u0, u1));
                        if (i % 6 == 0) segsB.Add((b0, u0));
                    }
                }
            }
            else
            {
                foreach (var e in b.Edges)
                {
                    var v1 = b.GetV(e.V1Id); var v2 = b.GetV(e.V2Id);
                    if (v1 != null && v2 != null) segsB.Add((v1.Pos, v2.Pos));
                }
            }

            foreach (var (q1, q2) in segsB)
            {
                Vec3 m1 = MapBToA(q1), m2 = MapBToA(q2);
                bool in1 = InsideApproxA(m1), in2 = InsideApproxA(m2);
                if (in1 && in2) { AddPt(m1); AddPt(m2); }
                else if (in1 != in2)
                {
                    double lo = 0.0, hi = 1.0;
                    for (int k = 0; k < 16; k++)
                    {
                        double mid = 0.5 * (lo + hi);
                        Vec3 pm = m1 + (m2 - m1) * mid;
                        if (InsideApproxA(pm) == in1) lo = mid; else hi = mid;
                    }
                    AddPt(m1 + (m2 - m1) * (0.5 * (lo + hi)));
                }
            }
            if (pts.Count >= 3)
            {
                Vec3 c = new(0, 0, 0);
                foreach (var p in pts) c = c + p;
                c = c * (1.0 / pts.Count);
                pts.Sort((p1, p2) => Math.Atan2(p1.Z - c.Z, p1.X - c.X).CompareTo(Math.Atan2(p2.Z - c.Z, p2.X - c.X)));
            }
            return pts;
        }

        // ===================== THIẾT DIỆN GIẢI TÍCH CHÍNH XÁC (CẦU / TRỤ / NÓN) =====================
        private static List<Vec3> IntersectPlaneWithHorizontalCircle(Vec3 n, double dPlane, double yLevel, double radius)
        {
            var pts = new List<Vec3>();
            double a = n.X, b = n.Z, c = dPlane - n.Y * yLevel;
            double len2 = a * a + b * b;
            if (len2 < 1e-8) return pts;
            double len = Math.Sqrt(len2);
            double dist = Math.Abs(c) / len;
            if (dist > radius + 1e-4) return pts;

            double cx = a * c / len2, cz = b * c / len2;
            double half = Math.Sqrt(Math.Max(0.0, radius * radius - dist * dist));
            double tx = -b / len, tz = a / len;
            pts.Add(new Vec3(cx + tx * half, yLevel, cz + tz * half));
            if (half > 1e-4) pts.Add(new Vec3(cx - tx * half, yLevel, cz - tz * half));
            return pts;
        }

        private static List<Vec3> SortPointsCyclicallyOnPlane(List<Vec3> raw, CutPlane3D plane, double dedupTol = 0.015)
        {
            var unique = new List<Vec3>();
            foreach (var p in raw)
                if (!unique.Any(q => (q - p).Length < dedupTol)) unique.Add(p);
            if (unique.Count < 3) return unique;

            Vec3 center = new(0, 0, 0);
            foreach (var p in unique) center = center + p;
            center = center * (1.0 / unique.Count);
            plane.GetBasis(out Vec3 u, out Vec3 v);
            return unique.OrderBy(p => Math.Atan2(Vec3.Dot(p - center, v), Vec3.Dot(p - center, u))).ToList();
        }

        public AnalyticalSectionResult ComputeAnalyticalRevolutionSection(CutPlane3D plane)
        {
            var res = new AnalyticalSectionResult();
            Vec3 n = plane.UnitNormal;
            double D = Vec3.Dot(plane.Origin, n);

            if (Kind == "sphere")
            {
                double dSigned = D;
                double d = Math.Abs(dSigned);
                if (d >= Radius - 0.005) return res;
                double r = Math.Sqrt(Math.Max(0.0, Radius * Radius - d * d));
                Vec3 H = n * dSigned;
                plane.GetBasis(out Vec3 u, out Vec3 v);
                int segs = 96;
                for (int i = 0; i < segs; i++)
                {
                    double th = 2.0 * Math.PI * i / segs;
                    res.Boundary3D.Add(H + (u * Math.Cos(th) + v * Math.Sin(th)) * r);
                }
                res.HasIntersection = true;
                res.IsSphereCircle = true;
                res.SphereFootH = H;
                res.SphereDistD = d;
                res.SphereSectionR = r;
                res.ShapeName = "Hình tròn";
                return res;
            }

            double h2 = Height * 0.5;
            if (Kind == "cylinder")
            {
                var ptsBot = IntersectPlaneWithHorizontalCircle(n, D, -h2, Radius);
                var ptsTop = IntersectPlaneWithHorizontalCircle(n, D, h2, Radius);
                var raw = new List<Vec3>();
                raw.AddRange(ptsBot);
                raw.AddRange(ptsTop);

                if (Math.Abs(n.Y) < 0.04)
                {
                    if (ptsBot.Count == 2 && ptsTop.Count == 2)
                    {
                        res.Boundary3D = SortPointsCyclicallyOnPlane(raw, plane, 0.01);
                        res.HasIntersection = res.Boundary3D.Count >= 3;
                        res.ShapeName = "Hình chữ nhật";
                    }
                    return res;
                }

                int segs = 144;
                for (int i = 0; i < segs; i++)
                {
                    double th = 2.0 * Math.PI * i / segs;
                    double x = Radius * Math.Cos(th), z = Radius * Math.Sin(th);
                    double y = (D - n.X * x - n.Z * z) / n.Y;
                    if (y >= -h2 - 1e-4 && y <= h2 + 1e-4)
                        raw.Add(new Vec3(x, Math.Clamp(y, -h2, h2), z));
                }

                res.Boundary3D = SortPointsCyclicallyOnPlane(raw, plane, 0.014);
                res.HasIntersection = res.Boundary3D.Count >= 3;
                if (res.HasIntersection)
                {
                    if (Math.Abs(n.Y) > 0.995) res.ShapeName = "Hình tròn";
                    else if (ptsBot.Count > 0 || ptsTop.Count > 0) res.ShapeName = "Elip cụt";
                    else res.ShapeName = "Elip";
                }
                return res;
            }

            if (Kind == "cone")
            {
                Vec3 S = new(0, h2, 0);
                double fS = Vec3.Dot(S, n) - D;
                var ptsBase = IntersectPlaneWithHorizontalCircle(n, D, -h2, Radius);

                if (Math.Abs(fS) < 0.04 && ptsBase.Count == 2)
                {
                    res.Boundary3D = SortPointsCyclicallyOnPlane(new List<Vec3> { S, ptsBase[0], ptsBase[1] }, plane, 0.01);
                    res.HasIntersection = res.Boundary3D.Count >= 3;
                    res.ShapeName = "Tam giác cân";
                    return res;
                }

                var raw = new List<Vec3>();
                raw.AddRange(ptsBase);
                int segs = 160;
                for (int i = 0; i < segs; i++)
                {
                    double th = 2.0 * Math.PI * i / segs;
                    Vec3 B = new(Radius * Math.Cos(th), -h2, Radius * Math.Sin(th));
                    double fB = Vec3.Dot(B, n) - D;
                    if ((fS > 1e-5 && fB <= 1e-5) || (fS < -1e-5 && fB >= -1e-5))
                    {
                        double denom = fS - fB;
                        if (Math.Abs(denom) > 1e-6)
                        {
                            double t = fS / denom;
                            if (t >= 0.0 && t <= 1.0) raw.Add(S + (B - S) * t);
                        }
                    }
                }

                res.Boundary3D = SortPointsCyclicallyOnPlane(raw, plane, 0.014);
                res.HasIntersection = res.Boundary3D.Count >= 3;
                if (res.HasIntersection)
                {
                    if (Math.Abs(n.Y) > 0.995) res.ShapeName = "Hình tròn";
                    else if (ptsBase.Count == 0) res.ShapeName = "Elip";
                    else
                    {
                        double horizN = Math.Sqrt(n.X * n.X + n.Z * n.Z);
                        double slopePlane = horizN / Math.Max(1e-4, Math.Abs(n.Y));
                        double slopeCone = Height / Math.Max(0.1, Radius);
                        if (Math.Abs(slopePlane - slopeCone) < 0.35) res.ShapeName = "Parabol cụt";
                        else if (slopePlane < slopeCone) res.ShapeName = "Elip cụt";
                        else res.ShapeName = "Hypebol cụt";
                    }
                }
                return res;
            }

            return res;
        }

        // ===================== KHAI TRIỂN NET 3D (GIỐNG GEOGEBRA NET) =====================
        private static Vec3 RotatePointAroundAxis(Vec3 p, Vec3 axisOrigin, Vec3 axisUnitDir, double angleRad)
        {
            if (Math.Abs(angleRad) < 1e-6) return p;
            Vec3 v = p - axisOrigin;
            double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
            Vec3 kCrossV = Vec3.Cross(axisUnitDir, v);
            double kDotV = Vec3.Dot(axisUnitDir, v);
            Vec3 vRot = v * cosA + kCrossV * sinA + axisUnitDir * (kDotV * (1.0 - cosA));
            return axisOrigin + vRot;
        }

        private static double ComputeDihedralUnfoldAngle(Vec3 baseOutwardN, Vec3 sideOutwardN, Vec3 hingeVec)
        {
            // Quay phap tuyen huong ngoai cua mat ben trung khit voi phap tuyen huong ngoai cua mat day -> tu dong mo ra PHIA NGOAI 100%!
            double dot = Math.Clamp(Vec3.Dot(sideOutwardN, baseOutwardN), -1.0, 1.0);
            double theta = Math.Acos(dot);
            if (theta < 1e-4) return 0.0;

            Vec3 hDir = hingeVec.Normalized();
            Vec3 nPlus = RotatePointAroundAxis(sideOutwardN, new Vec3(0, 0, 0), hDir, theta);
            Vec3 nMinus = RotatePointAroundAxis(sideOutwardN, new Vec3(0, 0, 0), hDir, -theta);
            return Vec3.Dot(nPlus, baseOutwardN) >= Vec3.Dot(nMinus, baseOutwardN) ? theta : -theta;
        }

        public List<(List<Vec3> pts3d, string label, bool isBase, bool isTopCap)> ComputeNetFacePolygons3D()
        {
            var result = new List<(List<Vec3> pts3d, string label, bool isBase, bool isTopCap)>();
            double t = Math.Clamp(UnfoldProgress, 0.0, 1.0);

            if (Kind == "poly" && Faces.Count >= 4)
            {
                // Face[0] la mat day chuan
                var baseFace = Faces[0];
                var basePts = baseFace.VertexIds.Select(GetV).Where(v => v != null).Select(v => v!.Pos).ToList();
                if (basePts.Count < 3) return result;

                Vec3 baseN = GetOutwardFaceNormal(baseFace);
                Vec3 baseCenter = new(0, 0, 0);
                foreach (var p in basePts) baseCenter = baseCenter + p;
                baseCenter = baseCenter * (1.0 / basePts.Count);

                result.Add((basePts, string.Join("", baseFace.VertexIds.Select(id => GetV(id)?.Label ?? "")), true, false));

                int m = baseFace.VertexIds.Count;
                // Xac dinh xem day la Chop (1 dinh chung S) hay Lang tru / Hop / Lap phuong (co nap tren Faces[1])
                bool isPrismLike = Faces.Count >= m + 2 && Faces[1].VertexIds.Count == m;
                int sideStartIdx = isPrismLike ? 2 : 1;
                int topCapIdx = isPrismLike ? 1 : -1;

                var sideRotations = new Dictionary<int, (Vec3 hA, Vec3 hDir, double fullAngle, double curAngle)>();

                for (int edgeIdx = 0; edgeIdx < m; edgeIdx++)
                {
                    string b1Id = baseFace.VertexIds[edgeIdx];
                    string b2Id = baseFace.VertexIds[(edgeIdx + 1) % m];
                    var vb1 = GetV(b1Id); var vb2 = GetV(b2Id);
                    if (vb1 == null || vb2 == null) continue;

                    int foundFaceIdx = -1;
                    for (int fIdx = sideStartIdx; fIdx < Faces.Count; fIdx++)
                    {
                        var fv = Faces[fIdx].VertexIds;
                        if (fv.Contains(b1Id) && fv.Contains(b2Id)) { foundFaceIdx = fIdx; break; }
                    }
                    if (foundFaceIdx < 0) continue;

                    var sideFace = Faces[foundFaceIdx];
                    var rawSidePts = sideFace.VertexIds.Select(GetV).Where(v => v != null).Select(v => v!.Pos).ToList();
                    Vec3 sideN = GetOutwardFaceNormal(sideFace);
                    Vec3 hingeVec = vb2.Pos - vb1.Pos;
                    double fullAngle = ComputeDihedralUnfoldAngle(baseN, sideN, hingeVec);
                    double curAngle = fullAngle * t;
                    Vec3 hDir = hingeVec.Normalized();

                    sideRotations[edgeIdx] = (vb1.Pos, hDir, fullAngle, curAngle);
                    var unfoldedSide = rawSidePts.Select(p => RotatePointAroundAxis(p, vb1.Pos, hDir, curAngle)).ToList();
                    string fLbl = string.Join("", sideFace.VertexIds.Select(id => GetV(id)?.Label ?? ""));
                    result.Add((unfoldedSide, fLbl, false, false));
                }

                // Neu la Lang tru / Hop CN / Lap phuong: tinh goc fullAngle2 o trang thai t=1 truoc, sau do noi suy muot theo t (khong giat goc)!
                if (isPrismLike && topCapIdx >= 0)
                {
                    var topFace = Faces[topCapIdx];
                    var rawTopPts = topFace.VertexIds.Select(GetV).Where(v => v != null).Select(v => v!.Pos).ToList();
                    int hingeEdge = ((TopCapHingeIdx % m) + m) % m;

                    if (sideRotations.TryGetValue(hingeEdge, out var rot1))
                    {
                        string b1Id = baseFace.VertexIds[hingeEdge];
                        string b2Id = baseFace.VertexIds[(hingeEdge + 1) % m];
                        int fSideIdx = -1;
                        for (int fIdx = sideStartIdx; fIdx < Faces.Count; fIdx++)
                            if (Faces[fIdx].VertexIds.Contains(b1Id) && Faces[fIdx].VertexIds.Contains(b2Id)) { fSideIdx = fIdx; break; }

                        if (fSideIdx >= 0)
                        {
                            var sideIds = Faces[fSideIdx].VertexIds;
                            var topSharedIds = sideIds.Where(id => topFace.VertexIds.Contains(id)).ToList();
                            if (topSharedIds.Count >= 2)
                            {
                                var vt1 = GetV(topSharedIds[0]); var vt2 = GetV(topSharedIds[1]);
                                if (vt1 != null && vt2 != null)
                                {
                                    Vec3 topN = GetOutwardFaceNormal(topFace);
                                    Vec3 topNFull1 = RotatePointAroundAxis(topN, new Vec3(0, 0, 0), rot1.hDir, rot1.fullAngle);
                                    Vec3 pTop1Full = RotatePointAroundAxis(vt1.Pos, rot1.hA, rot1.hDir, rot1.fullAngle);
                                    Vec3 pTop2Full = RotatePointAroundAxis(vt2.Pos, rot1.hA, rot1.hDir, rot1.fullAngle);
                                    double fullAngle2 = ComputeDihedralUnfoldAngle(baseN, topNFull1, pTop2Full - pTop1Full);

                                    // Ap dung quay muot theo t: quay quanh ban le day (rot1.curAngle), sau do quay quanh ban le tren (fullAngle2 * t)
                                    Vec3 pTop1Cur = RotatePointAroundAxis(vt1.Pos, rot1.hA, rot1.hDir, rot1.curAngle);
                                    Vec3 pTop2Cur = RotatePointAroundAxis(vt2.Pos, rot1.hA, rot1.hDir, rot1.curAngle);
                                    Vec3 h2DirCur = (pTop2Cur - pTop1Cur).Normalized();
                                    var step1Pts = rawTopPts.Select(p => RotatePointAroundAxis(p, rot1.hA, rot1.hDir, rot1.curAngle)).ToList();
                                    var finalTopPts = step1Pts.Select(p => RotatePointAroundAxis(p, pTop1Cur, h2DirCur, fullAngle2 * t)).ToList();

                                    string topLbl = string.Join("", topFace.VertexIds.Select(id => GetV(id)?.Label ?? ""));
                                    result.Add((finalTopPts, topLbl, false, true));
                                }
                            }
                        }
                    }
                }
                return result;
            }

            if (Kind is "cone" or "cylinder")
            {
                // Khai trien Net cho Hinh Non (hinh quat tron + day tron) & Hinh Tru (hinh chu nhat + 2 day tron)
                bool isCone = Kind == "cone";
                double R = Radius, h = Height, h2 = h * 0.5;
                int n = 48;

                // Day duoi (nam ngang tai y = -h2)
                var botPoly = new List<Vec3>(n);
                for (int i = 0; i < n; i++)
                {
                    double th = 2 * Math.PI * i / n;
                    botPoly.Add(new Vec3(R * Math.Cos(th), -h2, R * Math.Sin(th)));
                }
                result.Add((botPoly, "Đáy (O)", true, false));

                if (isCone)
                {
                    double slant = Math.Sqrt(R * R + h * h);
                    double sectorAngle = 2.0 * Math.PI * R / Math.Max(0.1, slant);
                    // Khi t = 0: hinh non dong; khi t = 1: hinh quat ban kinh slant trai phang tren mat phang y = -h2!
                    int segs = 40;
                    var sectorPts = new List<Vec3>(segs + 2);
                    Vec3 pivot = new(0, -h2, R); // Diem tiep xuc giua day va hinh quat
                    Vec3 apex3D = new(0, h2, 0);
                    Vec3 apexFlat = new(0, -h2, R + slant);
                    Vec3 apexCur = apex3D * (1.0 - t) + apexFlat * t;
                    sectorPts.Add(apexCur);

                    for (int i = 0; i <= segs; i++)
                    {
                        double u = -0.5 + (double)i / segs; // -0.5 .. 0.5
                        double thCone = u * 2.0 * Math.PI;
                        Vec3 pCone = new(R * Math.Sin(thCone), -h2, R * Math.Cos(thCone));
                        double thFlat = u * sectorAngle;
                        Vec3 pFlat = apexFlat + new Vec3(slant * Math.Sin(thFlat), -h2 + h2, -slant * Math.Cos(thFlat));
                        pFlat.Y = -h2;
                        sectorPts.Add(pCone * (1.0 - t) + pFlat * t);
                    }
                    result.Add((sectorPts, "Mặt xung quanh (Quạt tròn)", false, false));
                }
                else
                {
                    // Hinh tru: mat xung quanh trai thanh hinh chu nhat (2*pi*R x H) + day tren lat phang
                    double circ = 2.0 * Math.PI * R;
                    int segs = 40;
                    var latPts = new List<Vec3>(segs * 2 + 2);
                    for (int i = 0; i <= segs; i++)
                    {
                        double u = -0.5 + (double)i / segs;
                        double th = u * 2.0 * Math.PI;
                        Vec3 pCyl = new(R * Math.Sin(th), -h2, R * Math.Cos(th));
                        Vec3 pFlat = new(u * circ, -h2, R);
                        latPts.Add(pCyl * (1.0 - t) + pFlat * t);
                    }
                    for (int i = segs; i >= 0; i--)
                    {
                        double u = -0.5 + (double)i / segs;
                        double th = u * 2.0 * Math.PI;
                        Vec3 pCyl = new(R * Math.Sin(th), h2, R * Math.Cos(th));
                        Vec3 pFlat = new(u * circ, -h2, R + h);
                        latPts.Add(pCyl * (1.0 - t) + pFlat * t);
                    }
                    result.Add((latPts, "Mặt xung quanh (HCN)", false, false));

                    // Day tren lat xuong tai z = R + h + R
                    var topPoly = new List<Vec3>(n);
                    Vec3 topCenterCyl = new(0, h2, 0);
                    Vec3 topCenterFlat = new(0, -h2, R + h + R);
                    Vec3 curCenter = topCenterCyl * (1.0 - t) + topCenterFlat * t;
                    double tiltAngle = (Math.PI * 0.5) * t;
                    for (int i = 0; i < n; i++)
                    {
                        double th = 2 * Math.PI * i / n;
                        double lx = R * Math.Cos(th);
                        double lz = -R * Math.Sin(th);
                        double ly = lz * Math.Sin(tiltAngle);
                        double lzRot = lz * Math.Cos(tiltAngle);
                        topPoly.Add(curCenter + new Vec3(lx, ly, lzRot));
                    }
                    result.Add((topPoly, "Đáy trên (O')", false, true));
                }
            }
            return result;
        }

        public Vertex3D? ConstructParallelEdge3D(string fromVertexId, Edge3D refEdge, Func<string> labelGen)
        {
            var m = GetV(fromVertexId); var u1 = GetV(refEdge.V1Id); var u2 = GetV(refEdge.V2Id);
            if (m == null || u1 == null || u2 == null) return null;
            Vec3 dir = (u2.Pos - u1.Pos).Normalized();
            Vertex3D? bestHit = null; double bestDist = 0.09;
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
                    bestDist = gap; Vec3 pSeg = a.Pos + d2 * t;
                    bestHit = new Vertex3D { Id = $"v3_{NextIdx++}", Label = labelGen(), X = pSeg.X, Y = pSeg.Y, Z = pSeg.Z, HostEdgeV1 = e.V1Id, HostEdgeV2 = e.V2Id, HostT = t };
                }
            }
            if (bestHit != null) { Vertices.Add(bestHit); Edges.Add(new Edge3D { V1Id = fromVertexId, V2Id = bestHit.Id, IsConstruction = true }); }
            return bestHit;
        }

        public static Solid3DScene CreatePreset(string kind, Point center)
        {
            var s = new Solid3DScene { CenterScreen = center };
            void AddV(string id, string lbl, double x, double y, double z) => s.Vertices.Add(new Vertex3D { Id = id, Label = lbl, X = x, Y = y, Z = z });
            void AddE(string a, string b) => s.Edges.Add(new Edge3D { V1Id = a, V2Id = b });
            void AddF(params string[] vs) => s.Faces.Add(new Face3D { VertexIds = vs.ToList() });

            if (kind == "plane_only")
            {
                s.Kind = "plane_only"; s.Name = "Mặt phẳng (P)"; s.IsSolidShaded = true; s.SolidColorHex = "#38BDF8";
                s.CutPlane = new CutPlane3D
                {
                    Label = "(P)",
                    Origin = new Vec3(0, 0, 0),
                    Normal = new Vec3(0, 1, 0),
                    Visible = true,
                    IsInteractive = true,
                    PlaneColorHex = "#38BDF8",
                    SectionColorHex = "#FACC15",
                    HalfExtent = 1.65
                };
            }
            else if (kind is "pyramid_para" or "pyramid_perp")
            {
                s.Kind = "poly"; s.IsSolidShaded = false;
                s.Name = kind == "pyramid_perp" ? "Chóp S.ABCD (SA ⊥ đáy)" : "Chóp S.ABCD (đáy hình bình hành)";
                double sx = kind == "pyramid_perp" ? -1.05 : -0.35, sz = kind == "pyramid_perp" ? 0.65 : 0.05;
                AddV("S", "S", sx, 1.35, sz);
                AddV("A", "A", -1.05, -0.65, 0.65); AddV("B", "B", 0.65, -0.65, 0.65);
                AddV("C", "C", 1.15, -0.65, -0.65); AddV("D", "D", -0.55, -0.65, -0.65);
                foreach (var (a, b) in new[] { ("S","A"), ("S","B"), ("S","C"), ("S","D"), ("A","B"), ("B","C"), ("C","D"), ("D","A") }) AddE(a, b);
                AddF("A", "B", "C", "D"); AddF("S", "A", "B"); AddF("S", "B", "C"); AddF("S", "C", "D"); AddF("S", "D", "A");
            }
            else if (kind == "tetra")
            {
                s.Kind = "poly"; s.IsSolidShaded = false; s.Name = "Tứ diện S.ABC";
                AddV("S", "S", -0.15, 1.35, 0.0);
                AddV("A", "A", -1.0, -0.65, 0.65); AddV("B", "B", 0.95, -0.65, 0.65); AddV("C", "C", 0.10, -0.65, -0.95);
                foreach (var (a, b) in new[] { ("S","A"), ("S","B"), ("S","C"), ("A","B"), ("B","C"), ("C","A") }) AddE(a, b);
                AddF("A", "B", "C"); AddF("S", "A", "B"); AddF("S", "B", "C"); AddF("S", "C", "A");
            }
            else if (kind is "cube" or "cuboid")
            {
                s.Kind = "poly"; s.IsSolidShaded = false;
                s.Name = kind == "cube" ? "Hình lập phương ABCD.A'B'C'D'" : "Hình hộp chữ nhật ABCD.A'B'C'D'";
                double rx = kind == "cube" ? 0.85 : 1.15, ry = 0.85, rz = kind == "cube" ? 0.85 : 0.70;
                AddV("A", "A", -rx, -ry, rz);  AddV("B", "B", rx, -ry, rz);  AddV("C", "C", rx, -ry, -rz);  AddV("D", "D", -rx, -ry, -rz);
                AddV("A1", "A'", -rx, ry, rz); AddV("B1", "B'", rx, ry, rz); AddV("C1", "C'", rx, ry, -rz); AddV("D1", "D'", -rx, ry, -rz);
                foreach (var (a, b) in new[] { ("A","B"),("B","C"),("C","D"),("D","A"),("A1","B1"),("B1","C1"),("C1","D1"),("D1","A1"),("A","A1"),("B","B1"),("C","C1"),("D","D1") }) AddE(a, b);
                AddF("A","B","C","D"); AddF("A1","B1","C1","D1"); AddF("A","B","B1","A1"); AddF("B","C","C1","B1"); AddF("C","D","D1","C1"); AddF("D","A","A1","D1");
            }
            else if (kind == "prism_tri")
            {
                s.Kind = "poly"; s.IsSolidShaded = false; s.Name = "Lăng trụ tam giác đứng ABC.A'B'C'";
                AddV("A", "A", -0.95, -0.85, 0.65); AddV("B", "B", 0.95, -0.85, 0.65); AddV("C", "C", 0.0, -0.85, -0.85);
                AddV("A1", "A'", -0.95, 0.95, 0.65); AddV("B1", "B'", 0.95, 0.95, 0.65); AddV("C1", "C'", 0.0, 0.95, -0.85);
                foreach (var (a, b) in new[] { ("A","B"),("B","C"),("C","A"),("A1","B1"),("B1","C1"),("C1","A1"),("A","A1"),("B","B1"),("C","C1") }) AddE(a, b);
                AddF("A","B","C"); AddF("A1","B1","C1"); AddF("A","B","B1","A1"); AddF("B","C","C1","B1"); AddF("C","A","A1","C1");
            }
            else if (kind == "cone")
            {
                s.Kind = "cone"; s.Name = "Hình nón 3D"; s.IsSolidShaded = true; s.SolidColorHex = "#38BDF8"; s.Radius = 0.95; s.Height = 2.05;
                AddV("S", "S", 0, s.Height * 0.5, 0); AddV("O", "O", 0, -s.Height * 0.5, 0);
            }
            else if (kind == "cylinder")
            {
                s.Kind = "cylinder"; s.Name = "Hình trụ 3D"; s.IsSolidShaded = true; s.SolidColorHex = "#34D399"; s.Radius = 0.90; s.Height = 1.90;
                AddV("O1", "O'", 0, s.Height * 0.5, 0); AddV("O", "O", 0, -s.Height * 0.5, 0);
            }
            else if (kind == "sphere")
            {
                s.Kind = "sphere"; s.Name = "Hình cầu 3D"; s.IsSolidShaded = true; s.SolidColorHex = "#F59E0B"; s.Radius = 1.05; s.Height = 2.10;
                AddV("O", "O", 0, 0, 0);
            }
            return s;
        }
    }
}
