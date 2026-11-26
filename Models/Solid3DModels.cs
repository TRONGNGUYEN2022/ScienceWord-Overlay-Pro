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
        public string Kind { get; set; } = "poly"; // poly | cone | cylinder | sphere
        public string Name { get; set; } = "Chóp S.ABCD";
        public Point CenterScreen { get; set; } = new(440, 420);
        public double Scale { get; set; } = 135.0;
        public double YawDeg { get; set; } = -28.0;
        public double PitchDeg { get; set; } = 22.0;
        public double SplitAmount { get; set; } = 0.0;
        public bool AutoHiddenLines { get; set; } = true;

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
            if (Vertices.Count == 0) return new Vec3(0, 0, 0);
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

        public Vec3 GetEffectivePos(Vertex3D v, bool applySplit)
        {
            Vec3 p = v.Pos;
            if (applySplit && SplitAmount > 0.01 && TryGetCutPlane(out Vec3 o, out Vec3 n))
                if (Vec3.Dot(p - o, n) > 0.025) p = p + n * (SplitAmount * 0.85);
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
                // Duong xuyen ben trong da dien hoac canh phu -> kiem tra xem co nam tren mat nao khong
                Vec3 mid = (v1.Pos + v2.Pos) * 0.5;
                var planeFaces = Faces.Where(f => IsPointOnFace(v1.Pos, f) && IsPointOnFace(v2.Pos, f) && IsPointOnFace(mid, f)).ToList();
                if (planeFaces.Count > 0)
                {
                    foreach (var f in planeFaces) if (IsFaceFrontFacing(f)) return false;
                    return true;
                }
                return true; // Doan ben trong khoi dac la net dut
            }
            foreach (var f in adjFaces) if (IsFaceFrontFacing(f)) return false;
            return true;
        }

        // ===================== DỰNG MẶT PHẲNG CẮT (P) & TỰ TÍNH THIẾT DIỆN 3D =====================
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

        public void NudgeCutPlane(double delta)
        {
            if (CutPlane == null) return;
            CutPlane.IsInteractive = true;
            CutPlane.Visible = true;
            Vec3 n = CutPlane.UnitNormal;
            Vec3 nextOrigin = CutPlane.Origin + n * delta;
            double maxLim = Kind == "sphere" ? Radius * 1.15 : (Kind == "poly" ? 1.85 : Math.Max(Height, Radius) * 1.15);
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
            // Xoay vector phap tuyen quanh truc X (nghieng mat phang huong ve phia nguoi nhin)
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

            // 1. Lay cac dinh goc nam san tren mat phang (P)
            var baseEdges = Edges.Where(e => !e.IsConstruction).ToList();
            var baseVertIds = new HashSet<string>();
            foreach (var e in baseEdges) { baseVertIds.Add(e.V1Id); baseVertIds.Add(e.V2Id); }
            foreach (var v in Vertices)
            {
                if (Math.Abs(plane.SignedDistance(v.Pos)) <= 0.025)
                    AddUnique(v);
            }

            // 2. Tinh giao diem cua (P) voi tat ca cac canh con lai cua khoi da dien
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

            // 3. Sap xep cac dinh thiet dien theo vong tron tren mat phang (P)
            Vec3 center = new(0, 0, 0);
            foreach (var v in picked) center = center + v.Pos;
            center = center * (1.0 / picked.Count);
            plane.Origin = center;

            plane.GetBasis(out Vec3 u, out Vec3 vAxis);
            var ordered = picked
                .OrderBy(vt => Math.Atan2(Vec3.Dot(vt.Pos - center, vAxis), Vec3.Dot(vt.Pos - center, u)))
                .ToList();

            // 4. Noi cac canh giao tuyen theo dung thu tu da giac
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

            // 5. To kin da giac thiet dien
            Sections.Add(new Section3D
            {
                VertexIds = ordered.Select(v => v.Id).ToList(),
                ColorHex = plane.SectionColorHex,
                Opacity = 0.48
            });

            string polyName = string.Join("", ordered.Select(v => v.Label));
            return (true, $"Đã dựng (P) {desc} → Thiết diện {polyName} ({ordered.Count} đỉnh, tự thêm {newlyCreated} giao điểm)! Kéo [Tách] để tách khối!");
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
                double dSigned = D; // Tam O = (0,0,0) -> n . (0 - Origin) = -D => H = n * D
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

            if (kind is "pyramid_para" or "pyramid_perp")
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
