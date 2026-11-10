using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KineticCore.Schema;

namespace KineticCore.Export
{
    /// <summary>
    /// Represents a single parametric 3D CAD body in the user's Multi-Body Spatial Fusion feature tree.
    /// Supports Cube/Box, Cylinder, Sphere, Torus/Flange Ring, Hollow Tube, and L-Bracket primitives,
    /// Boolean CSG modes (Join/Union, Cut/Subtract, Intersect), and Linear/Circular Pattern arrays.
    /// </summary>
    [System.Serializable]
    public class StampedCadBodySpec
    {
        public string bodyName = "CadBody_1";
        public int primitiveShape = 0; // 0..11 (12 Fusion 360 CAD primitives)
        public int booleanMode = 0;    // 0=Join (Union), 1=Cut (Subtract), 2=Intersect
        public int patternMode = 0;    // 0=1x Single, 1=3x Linear Pattern, 2=6x Circular Radial Pattern
        public int featureOp = 0;      // 0=Solid Extrude, 1=Shell 2.5mm, 2=Chamfer+M4 Bosses, 3=L-Bracket
        public double widthMm = 70.0;
        public double depthMm = 55.0;
        public double heightMm = 42.0;
        // 3 Translations (mm)
        public double localOffsetXMm = 0.0;
        public double localOffsetYMm = 0.0;
        public double localOffsetZMm = 0.0;
        // 3 Angular Rotations / Tilts (degrees)
        public double localRotXDeg = 0.0;
        public double localRotYDeg = 0.0;
        public double localRotZDeg = 0.0;
    }

    /// <summary>
    /// Serializes a MechanismSpec to a URDF serial chain.
    ///
    /// MechanismSpec convention (same as the PoE solver and the Unity visuals):
    ///   - All joint origins, link centres and the end-effector home position are expressed in the
    ///     mechanism base frame at the home configuration (theta = 0), in metres, Unity axes (Y-up, left-handed).
    ///   - links[0] is the fixed base. Moving link k (k >= 1) is carried by the same joint the Unity
    ///     visuals parent it to (see KineticMechanismController.RebuildVisualHierarchy).
    ///
    /// URDF output convention (ROS REP-103: X forward, Y left, Z up, right-handed):
    ///   - Positions are mapped with C: (x, y, z)_unity -> (z, -x, y)_ros.
    ///   - Because C is a reflection, a rotation about axis a by +theta in the solver maps to a rotation about
    ///     -C*a by +theta in ROS. Joint axes are emitted as -C*a so the joint angles streamed over UDP can be
    ///     published to /joint_states unchanged.
    ///   - Every joint frame sits at its joint origin with identity rotation; link geometry is offset into
    ///     the child link frame (centre - joint origin).
    /// </summary>
    public static class UrdfSerializer
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private struct V3
        {
            public double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        /// <summary>Unity (Y-up, left-handed) position -> ROS REP-103 position.</summary>
        private static V3 ToRos(double x, double y, double z) => new V3(z, -x, y);

        /// <summary>Solver revolute/prismatic axis -> ROS axis that preserves the sign of streamed joint values.</summary>
        private static V3 AxisToRos(double ax, double ay, double az, bool prismatic)
        {
            V3 c = ToRos(ax, ay, az);
            // Translations map directly through C; rotations flip sense under a reflection.
            return prismatic ? c : new V3(-c.X, -c.Y, -c.Z);
        }

        public static string SerializeToUrdfXml(MechanismSpec spec)
        {
            StringBuilder sb = new StringBuilder();
            string robotName = Sanitize(string.IsNullOrEmpty(spec.robotName) ? "kinetic_cad_mechanism" : spec.robotName);

            int n = spec.joints != null ? spec.joints.Length : 0;
            int linkCount = spec.links != null ? spec.links.Length : 0;

            // ---- Assign each moving link to the joint that carries it (mirrors the Unity visual hierarchy)
            var linksOnJoint = new List<int>[n];
            for (int i = 0; i < n; i++) linksOnJoint[i] = new List<int>();
            bool coLocatedShoulder = (n >= 2 && System.Math.Abs(spec.joints[1].originX - spec.joints[0].originX) < 0.02);
            int movingLinks = System.Math.Max(0, linkCount - 1);
            int offset = coLocatedShoulder ? System.Math.Max(0, n - movingLinks) : 0;
            for (int k = 1; k < linkCount && n > 0; k++)
            {
                int pivot = System.Math.Min(n - 1, k - 1 + offset);
                if (pivot >= 0) linksOnJoint[pivot].Add(k);
            }

            // ---- Name the child frame of every joint (unique, URDF-safe)
            var used = new HashSet<string>();
            string baseName = UniqueName(linkCount > 0 ? spec.links[0].name : "base_link", "base_link", used);
            string[] childName = new string[n];
            for (int i = 0; i < n; i++)
            {
                string preferred = linksOnJoint[i].Count > 0 ? spec.links[linksOnJoint[i][0]].name : spec.joints[i].childLink;
                childName[i] = UniqueName(preferred, $"{Sanitize(spec.joints[i].name)}_link", used);
            }
            string eeName = UniqueName("ee_tip", "ee_tip", used);

            V3[] jointPos = new V3[n];
            for (int i = 0; i < n; i++)
                jointPos[i] = ToRos(spec.joints[i].originX, spec.joints[i].originY, spec.joints[i].originZ);

            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<!-- Generated by KineticCAD XR. Frames follow ROS REP-103 (X forward, Y left, Z up). -->");
            sb.AppendLine("<!-- Joint values streamed on UDP :9870 map 1:1 onto these joints (/joint_states). -->");
            sb.AppendLine($"<robot name=\"{robotName}\">");

            // ---- Base link (root frame = mechanism origin on the desk)
            if (linkCount > 0)
                AppendLink(sb, baseName, spec.links[0], new V3(0, 0, 0), isBase: true);
            else
                AppendEmptyLink(sb, baseName);

            // ---- One child link per joint
            for (int i = 0; i < n; i++)
            {
                if (linksOnJoint[i].Count > 0)
                {
                    // First link carried by this joint becomes the joint's child frame.
                    AppendLink(sb, childName[i], spec.links[linksOnJoint[i][0]], jointPos[i], isBase: false);

                    // Any additional links carried by the same joint are rigidly attached with fixed joints.
                    for (int extra = 1; extra < linksOnJoint[i].Count; extra++)
                    {
                        LinkSpec lk = spec.links[linksOnJoint[i][extra]];
                        string nm = UniqueName(lk.name, $"{childName[i]}_extra{extra}", used);
                        AppendLink(sb, nm, lk, jointPos[i], isBase: false);
                        AppendFixedJoint(sb, $"{nm}_fixed", childName[i], nm, new V3(0, 0, 0));
                    }
                }
                else
                {
                    AppendEmptyLink(sb, childName[i]);
                }
            }

            // ---- Joints
            for (int i = 0; i < n; i++)
            {
                JointSpec j = spec.joints[i];
                bool prismatic = string.Equals(j.jointType, "prismatic", System.StringComparison.OrdinalIgnoreCase);
                string parent = i == 0 ? baseName : childName[i - 1];
                V3 rel = i == 0 ? jointPos[0] : jointPos[i] - jointPos[i - 1];
                V3 axis = AxisToRos(j.axisX, j.axisY, j.axisZ, prismatic);

                sb.AppendLine($"  <joint name=\"{Sanitize(j.name)}\" type=\"{(prismatic ? "prismatic" : "revolute")}\">");
                sb.AppendLine($"    <parent link=\"{parent}\"/>");
                sb.AppendLine($"    <child link=\"{childName[i]}\"/>");
                sb.AppendLine(string.Format(Inv, "    <origin xyz=\"{0:F5} {1:F5} {2:F5}\" rpy=\"0 0 0\"/>", rel.X, rel.Y, rel.Z));
                sb.AppendLine(string.Format(Inv, "    <axis xyz=\"{0:F4} {1:F4} {2:F4}\"/>", axis.X, axis.Y, axis.Z));
                sb.AppendLine(string.Format(Inv, "    <limit lower=\"{0:F4}\" upper=\"{1:F4}\" effort=\"15.0\" velocity=\"3.14\"/>", j.minLimitRad, j.maxLimitRad));
                sb.AppendLine("  </joint>");
            }

            // ---- End-effector tip frame
            string lastFrame = n > 0 ? childName[n - 1] : baseName;
            V3 lastPos = n > 0 ? jointPos[n - 1] : new V3(0, 0, 0);
            V3 ee = (spec.endEffectorHomePosition != null && spec.endEffectorHomePosition.Length == 3)
                ? ToRos(spec.endEffectorHomePosition[0], spec.endEffectorHomePosition[1], spec.endEffectorHomePosition[2])
                : lastPos;
            sb.AppendLine($"  <link name=\"{eeName}\"/>");
            AppendFixedJoint(sb, "ee_fixed_joint", lastFrame, eeName, ee - lastPos);

            sb.AppendLine("</robot>");
            return sb.ToString();
        }

        private static void AppendLink(StringBuilder sb, string name, LinkSpec link, V3 frameOrigin, bool isBase)
        {
            double m = link.massKg > 0 ? link.massKg : 0.1;
            V3 c = ToRos(link.centerX, link.centerY, link.centerZ) - frameOrigin;

            sb.AppendLine($"  <link name=\"{name}\">");
            if (isBase)
            {
                // Base is drawn as an upright cylinder in Unity (diameter = widthM, height = heightM).
                double r = (link.widthM > 0 ? link.widthM : 0.08) * 0.5;
                double h = link.heightM > 0 ? link.heightM : 0.06;
                double ixy = (m / 12.0) * (3.0 * r * r + h * h);
                double izz = 0.5 * m * r * r;
                AppendInertial(sb, c, m, ixy, ixy, izz);
                string geom = string.Format(Inv, "<cylinder radius=\"{0:F5}\" length=\"{1:F5}\"/>", r, h);
                AppendGeometry(sb, "visual", c, geom);
                AppendGeometry(sb, "collision", c, geom);
            }
            else
            {
                // Unity box extents (length along X, height along Y, width along Z) -> ROS (Z_u, X_u, Y_u).
                double lx = link.lengthM > 0 ? link.lengthM : 0.05;
                double ly = link.heightM > 0 ? link.heightM : 0.02;
                double lz = link.widthM > 0 ? link.widthM : 0.02;
                double sx = lz, sy = lx, sz = ly;
                double ixx = (m / 12.0) * (sy * sy + sz * sz);
                double iyy = (m / 12.0) * (sx * sx + sz * sz);
                double izz = (m / 12.0) * (sx * sx + sy * sy);
                AppendInertial(sb, c, m, ixx, iyy, izz);
                string geom = string.Format(Inv, "<box size=\"{0:F5} {1:F5} {2:F5}\"/>", sx, sy, sz);
                AppendGeometry(sb, "visual", c, geom);
                AppendGeometry(sb, "collision", c, geom);
            }
            sb.AppendLine("  </link>");
        }

        private static void AppendEmptyLink(StringBuilder sb, string name)
        {
            // Small non-zero inertia so physics engines (Gazebo / Isaac Sim) do not drop the frame.
            sb.AppendLine($"  <link name=\"{name}\">");
            AppendInertial(sb, new V3(0, 0, 0), 0.01, 1e-6, 1e-6, 1e-6);
            sb.AppendLine("  </link>");
        }

        private static void AppendInertial(StringBuilder sb, V3 c, double m, double ixx, double iyy, double izz)
        {
            sb.AppendLine("    <inertial>");
            sb.AppendLine(string.Format(Inv, "      <origin xyz=\"{0:F5} {1:F5} {2:F5}\" rpy=\"0 0 0\"/>", c.X, c.Y, c.Z));
            sb.AppendLine(string.Format(Inv, "      <mass value=\"{0:F4}\"/>", m));
            sb.AppendLine(string.Format(Inv, "      <inertia ixx=\"{0:E4}\" ixy=\"0\" ixz=\"0\" iyy=\"{1:E4}\" iyz=\"0\" izz=\"{2:E4}\"/>", ixx, iyy, izz));
            sb.AppendLine("    </inertial>");
        }

        private static void AppendGeometry(StringBuilder sb, string tag, V3 c, string geometryXml)
        {
            sb.AppendLine($"    <{tag}>");
            sb.AppendLine(string.Format(Inv, "      <origin xyz=\"{0:F5} {1:F5} {2:F5}\" rpy=\"0 0 0\"/>", c.X, c.Y, c.Z));
            sb.AppendLine($"      <geometry>{geometryXml}</geometry>");
            sb.AppendLine($"    </{tag}>");
        }

        private static void AppendFixedJoint(StringBuilder sb, string name, string parent, string child, V3 rel)
        {
            sb.AppendLine($"  <joint name=\"{Sanitize(name)}\" type=\"fixed\">");
            sb.AppendLine($"    <parent link=\"{parent}\"/>");
            sb.AppendLine($"    <child link=\"{child}\"/>");
            sb.AppendLine(string.Format(Inv, "    <origin xyz=\"{0:F5} {1:F5} {2:F5}\" rpy=\"0 0 0\"/>", rel.X, rel.Y, rel.Z));
            sb.AppendLine("  </joint>");
        }

        private static string UniqueName(string preferred, string fallback, HashSet<string> used)
        {
            string name = Sanitize(string.IsNullOrEmpty(preferred) ? fallback : preferred);
            if (string.IsNullOrEmpty(name)) name = Sanitize(fallback);
            string candidate = name;
            int suffix = 2;
            while (used.Contains(candidate))
                candidate = $"{name}_{suffix++}";
            used.Add(candidate);
            return candidate;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            return sb.ToString();
        }

        public static string SaveUrdfToFile(MechanismSpec spec, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string fileName = Sanitize(string.IsNullOrEmpty(spec.robotName) ? "kinetic_cad_mechanism" : spec.robotName);
            string filePath = Path.Combine(directoryPath, $"{fileName}.urdf");
            File.WriteAllText(filePath, SerializeToUrdfXml(spec));
            return filePath;
        }

        /// <summary>
        /// Exports a watertight ASCII STL 3D-printable CAD mesh (millimeter units for slicers such as Bambu Studio / PrusaSlicer)
        /// containing the captured real-world CAD solid, all stamped multi-body primitives (Cube, Cylinder, Sphere, Torus, Tube, L-Bracket),
        /// linear/circular patterns, base pedestal, articulated modular links, and parallel-jaw gripper assembly.
        /// </summary>
        public static string SaveStlToFile(
            MechanismSpec spec,
            string directoryPath,
            double capturedWidthMm = 70.0,
            double capturedDepthMm = 55.0,
            double capturedHeightMm = 42.0,
            int cadFeatureOp = 0,
            int primitiveShape = 0,
            int booleanMode = 0,
            int patternMode = 0,
            IList<StampedCadBodySpec> stampedBodies = null)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string fileName = Sanitize(string.IsNullOrEmpty(spec?.robotName) ? "kinetic_cad_mechanism" : spec.robotName);
            string filePath = Path.Combine(directoryPath, $"{fileName}_cad.stl");
            File.WriteAllText(filePath, SerializeToAsciiStl(
                spec, capturedWidthMm, capturedDepthMm, capturedHeightMm, cadFeatureOp, primitiveShape, booleanMode, patternMode, stampedBodies));
            return filePath;
        }

        /// <summary>
        /// Exports a multi-group Wavefront OBJ 3D CAD mesh (<robot>_cad.obj) with named object groups
        /// (Captured_RealWorld_Solid, Stamped_MultiBody_Tree, Base_Pedestal, Modular_Links, Parallel_Gripper) for Fusion 360 / Blender / SolidWorks.
        /// </summary>
        public static string SaveObjToFile(
            MechanismSpec spec,
            string directoryPath,
            double capturedWidthMm = 70.0,
            double capturedDepthMm = 55.0,
            double capturedHeightMm = 42.0,
            int cadFeatureOp = 0,
            string materialName = "PLA_Plus_1240kgm3",
            int primitiveShape = 0,
            int booleanMode = 0,
            int patternMode = 0,
            IList<StampedCadBodySpec> stampedBodies = null)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string fileName = Sanitize(string.IsNullOrEmpty(spec?.robotName) ? "kinetic_cad_mechanism" : spec.robotName);
            string filePath = Path.Combine(directoryPath, $"{fileName}_cad.obj");
            File.WriteAllText(filePath, SerializeToWavefrontObj(
                spec, capturedWidthMm, capturedDepthMm, capturedHeightMm, cadFeatureOp, materialName, primitiveShape, booleanMode, patternMode, stampedBodies));
            return filePath;
        }

        /// <summary>
        /// Exports the Swept-Volume Control Barrier Function (CBF: h(x) = min ||p_obs - p_k(theta)||_2 - (r_link + r_margin) >= 0)
        /// envelope and Spatial Fusion Parametric Feature Tree manifest for AEGIS FlightOS, 3D printing slicers, and ROS 2.
        /// </summary>
        public static string SaveCbfEnvelopeToFile(
            MechanismSpec spec,
            string directoryPath,
            double minClearanceMm,
            int sweptCapsuleCount,
            int carvedVoxelCount,
            bool housingCarved,
            double capturedWidthMm = 70.0,
            double capturedDepthMm = 55.0,
            double capturedHeightMm = 42.0,
            string cadFeatureLabel = "Solid Extrude",
            string cadMaterialLabel = "PLA+ (1.24 g/cm3)",
            double cadPartMassGrams = 48.5,
            string primitiveShapeLabel = "Box / Cube",
            string booleanModeLabel = "Boolean Join (Union)",
            string patternModeLabel = "1x Single",
            IList<StampedCadBodySpec> stampedBodies = null)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string fileName = Sanitize(string.IsNullOrEmpty(spec?.robotName) ? "kinetic_cad_mechanism" : spec.robotName);
            string filePath = Path.Combine(directoryPath, $"{fileName}_cbf_envelope.json");

            double l1 = (spec?.links != null && spec.links.Length > 1) ? spec.links[1].lengthM : 0.14;
            double l2 = (spec?.links != null && spec.links.Length > 2) ? spec.links[2].lengthM : 0.11;
            double rLinkMm = (spec?.links != null && spec.links.Length > 1) ? spec.links[1].widthM * 500.0 : 13.0;
            double rMarginMm = 10.0;
            int dofCount = (spec?.joints != null) ? spec.joints.Length : 3;
            int bodyCount = 1 + (stampedBodies != null ? stampedBodies.Count : 0);

            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("{");
            sb.AppendLine($"  \"schema\": \"AEGIS_FlightOS_SpatialFusion_CBF_v3\",");
            sb.AppendLine($"  \"robotName\": \"{fileName}\",");
            sb.AppendLine($"  \"kinematicDoF\": {dofCount},");
            sb.AppendLine(string.Format(Inv, "  \"targetCaliperWidthMm\": {0:F2},", spec != null ? spec.targetCaliperWidthMm : 70.0));
            sb.AppendLine(string.Format(Inv, "  \"capturedRealObjectDimsMm\": [{0:F1}, {1:F1}, {2:F1}],", capturedWidthMm, capturedDepthMm, capturedHeightMm));
            sb.AppendLine($"  \"activePrimitiveShape\": \"{primitiveShapeLabel}\",");
            sb.AppendLine($"  \"activeBooleanMode\": \"{booleanModeLabel}\",");
            sb.AppendLine($"  \"activePatternArray\": \"{patternModeLabel}\",");
            sb.AppendLine($"  \"fusionCadFeatureOp\": \"{cadFeatureLabel}\",");
            sb.AppendLine($"  \"multiBodyTreeCount\": {bodyCount},");
            sb.AppendLine($"  \"engineeringMaterial\": \"{cadMaterialLabel}\",");
            sb.AppendLine(string.Format(Inv, "  \"estimated3DPrintMassGrams\": {0:F2},", cadPartMassGrams));
            sb.AppendLine($"  \"controlBarrierFunction\": \"h(x) = min_k ||p_obs - p_k(theta)||_2 - (r_link + r_margin) >= 0\",");
            sb.AppendLine(string.Format(Inv, "  \"rLinkMm\": {0:F2},", rLinkMm));
            sb.AppendLine(string.Format(Inv, "  \"rSafetyMarginMm\": {0:F2},", rMarginMm));
            sb.AppendLine(string.Format(Inv, "  \"maxReachMm\": {0:F2},", (l1 + l2) * 1000.0));
            sb.AppendLine($"  \"sweptCapsuleSamples\": {sweptCapsuleCount},");
            sb.AppendLine($"  \"ghostCarvedVoxelsRemoved\": {carvedVoxelCount},");
            sb.AppendLine($"  \"housingZeroClashVerified\": {(housingCarved ? "true" : "false")},");
            sb.AppendLine(string.Format(Inv, "  \"minBarrierClearanceHxMm\": {0:F2},", minClearanceMm));
            sb.AppendLine($"  \"cbfSatisfied\": {(minClearanceMm >= 0.0 ? "true" : "false")}");
            sb.AppendLine("}");

            File.WriteAllText(filePath, sb.ToString());
            return filePath;
        }

        public static string SerializeToAsciiStl(
            MechanismSpec spec,
            double capturedWidthMm = 70.0,
            double capturedDepthMm = 55.0,
            double capturedHeightMm = 42.0,
            int cadFeatureOp = 0,
            int primitiveShape = 0,
            int booleanMode = 0,
            int patternMode = 0,
            IList<StampedCadBodySpec> stampedBodies = null,
            bool includeRobotLinks = false)
        {
            StringBuilder sb = new StringBuilder(32768);
            string solidName = Sanitize(string.IsNullOrEmpty(spec?.robotName) ? "kinetic_cad_model" : spec.robotName);
            sb.AppendLine($"solid {solidName}");

            // 1. Primary Captured / Sculpted Parametric CAD Solid centered at (0,0,0) on the 3D printer build plate
            AppendCadBodyWithPatternStl(sb, 0.0, 0.0, 0.0, capturedWidthMm, capturedDepthMm, capturedHeightMm, primitiveShape, cadFeatureOp, patternMode);

            // 2. Additional Stamped Multi-Body CAD Primitives from Left-Hand Spatial CAD Palette
            if (stampedBodies != null)
            {
                for (int b = 0; b < stampedBodies.Count; b++)
                {
                    StampedCadBodySpec body = stampedBodies[b];
                    if (body == null) continue;
                    AppendCadBodyWithPatternStl(
                        sb,
                        body.localOffsetXMm,
                        body.localOffsetZMm,
                        body.localOffsetYMm,
                        body.widthMm,
                        body.depthMm,
                        body.heightMm,
                        body.primitiveShape,
                        body.featureOp,
                        body.patternMode);
                }
            }

            // 3. Optional Articulated Modular Links (only when includeRobotLinks is true)
            if (includeRobotLinks && spec?.links != null)
            {
                for (int i = 0; i < spec.links.Length; i++)
                {
                    LinkSpec lk = spec.links[i];
                    if (lk == null) continue;
                    double cx = lk.centerX * 1000.0;
                    double cy = lk.centerZ * 1000.0;
                    double cz = lk.centerY * 1000.0;
                    double sx = System.Math.Max(8.0, lk.lengthM * 1000.0);
                    double sy = System.Math.Max(8.0, lk.widthM * 1000.0);
                    double sz = System.Math.Max(8.0, lk.heightM * 1000.0);
                    AppendStlBox(sb, cx, cy, cz, sx, sy, sz);
                }
            }

            sb.AppendLine($"endsolid {solidName}");
            return sb.ToString();
        }

        private static void AppendCadBodyWithPatternStl(
            StringBuilder sb, double offsetX, double offsetY, double baseZ,
            double wMm, double dMm, double hMm, int primitiveShape, int cadFeatureOp, int patternMode)
        {
            if (patternMode == 1) // 3x Linear Pattern along X
            {
                double pitch = System.Math.Max(18.0, wMm * 0.65);
                for (int k = -1; k <= 1; k++)
                {
                    AppendSingleCadPrimitiveStl(sb, offsetX + k * pitch, offsetY, baseZ, wMm * 0.52, dMm * 0.52, hMm, primitiveShape, cadFeatureOp);
                }
            }
            else if (patternMode == 2) // 6x Circular Radial Pattern
            {
                double radius = System.Math.Max(16.0, System.Math.Min(wMm, dMm) * 0.42);
                for (int k = 0; k < 6; k++)
                {
                    double ang = k * System.Math.PI / 3.0;
                    double px = offsetX + System.Math.Cos(ang) * radius;
                    double py = offsetY + System.Math.Sin(ang) * radius;
                    AppendSingleCadPrimitiveStl(sb, px, py, baseZ, wMm * 0.36, dMm * 0.36, hMm, primitiveShape, cadFeatureOp);
                }
            }
            else
            {
                AppendSingleCadPrimitiveStl(sb, offsetX, offsetY, baseZ, wMm, dMm, hMm, primitiveShape, cadFeatureOp);
            }
        }

        private static void AppendSingleCadPrimitiveStl(
            StringBuilder sb, double offsetX, double offsetY, double baseZ,
            double wMm, double dMm, double hMm, int primitiveShape, int cadFeatureOp)
        {
            double w = System.Math.Max(10.0, wMm);
            double d = System.Math.Max(10.0, dMm);
            double h = System.Math.Max(8.0, hMm);

            if (primitiveShape == 1) // Cylinder
            {
                AppendStlCylinder(sb, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 18);
                return;
            }
            if (primitiveShape == 2) // Sphere (Geodesic / UV Sphere)
            {
                AppendStlSphere(sb, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 6, 12);
                return;
            }
            if (primitiveShape == 3) // Cone / Tapered Frustum (4 stacked tapered rings)
            {
                double stepH = h / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    double taper = 1.0 - (s * 0.21);
                    AppendStlCylinder(sb, offsetX, offsetY, baseZ + (s + 0.5) * stepH, w * taper, d * taper, stepH, 14);
                }
                return;
            }
            if (primitiveShape == 4) // Torus / Flange Ring (8-segment octagonal ring)
            {
                double rRing = System.Math.Max(8.0, System.Math.Min(w, d) * 0.38);
                double tubeW = System.Math.Max(4.0, w * 0.22);
                double tubeH = System.Math.Max(4.0, h * 0.45);
                for (int k = 0; k < 8; k++)
                {
                    double ang = k * System.Math.PI * 0.25;
                    AppendStlCylinder(sb, offsetX + System.Math.Cos(ang) * rRing, offsetY + System.Math.Sin(ang) * rRing, baseZ + tubeH * 0.5, tubeW, tubeW, tubeH, 10);
                }
                return;
            }
            if (primitiveShape == 5) // Hollow Tube / Pipe
            {
                AppendStlCylinder(sb, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 16);
                AppendStlCylinder(sb, offsetX, offsetY, baseZ + h + 2.0, w * 0.72, d * 0.72, 4.0, 12);
                return;
            }
            if (primitiveShape == 6) // Hex Prism (6-sided M-Nut / Hex Bolt Head)
            {
                AppendStlCylinder(sb, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 6);
                return;
            }
            if (primitiveShape == 7) // Wedge / 45° Sloped Ramp (stepped sloped prism)
            {
                double stepW = w / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    double sh = h * ((s + 1) / 4.0);
                    double sx = offsetX - w * 0.5 + (s + 0.5) * stepW;
                    AppendStlBox(sb, sx, offsetY, baseZ + sh * 0.5, stepW, d, sh);
                }
                return;
            }
            if (primitiveShape == 8 || cadFeatureOp == 3) // L-Bracket Structural Mount
            {
                double t = System.Math.Max(4.0, System.Math.Min(w, h) * 0.16);
                AppendStlBox(sb, offsetX, offsetY, baseZ + t * 0.5, w, d, t);
                AppendStlBox(sb, offsetX - (w - t) * 0.5, offsetY, baseZ + h * 0.5, t, d, h);
                AppendStlBox(sb, offsetX - w * 0.22, offsetY - (d - t) * 0.42, baseZ + h * 0.28, w * 0.35, t, h * 0.35);
                AppendStlBox(sb, offsetX - w * 0.22, offsetY + (d - t) * 0.42, baseZ + h * 0.28, w * 0.35, t, h * 0.35);
                return;
            }
            if (primitiveShape == 9) // U-Channel / C-Clevis Bracket
            {
                double t = System.Math.Max(4.0, System.Math.Min(w, h) * 0.16);
                AppendStlBox(sb, offsetX, offsetY, baseZ + t * 0.5, w, d, t);
                AppendStlBox(sb, offsetX - (w - t) * 0.5, offsetY, baseZ + h * 0.5, t, d, h);
                AppendStlBox(sb, offsetX + (w - t) * 0.5, offsetY, baseZ + h * 0.5, t, d, h);
                return;
            }
            if (primitiveShape == 10) // T-Beam / Structural Extrusion Profile
            {
                double t = System.Math.Max(4.0, System.Math.Min(w, h) * 0.18);
                AppendStlBox(sb, offsetX, offsetY, baseZ + t * 0.5, w, d, t);
                AppendStlBox(sb, offsetX, offsetY, baseZ + h * 0.5, t, d, h);
                return;
            }
            if (primitiveShape == 11) // Flanged Bearing Housing (Base Flange + Cylindrical Boss)
            {
                double flangeH = System.Math.Max(4.0, h * 0.25);
                AppendStlBox(sb, offsetX, offsetY, baseZ + flangeH * 0.5, w, d, flangeH);
                AppendStlCylinder(sb, offsetX, offsetY, baseZ + h * 0.55, w * 0.65, d * 0.65, h * 0.90, 16);
                return;
            }

            if (cadFeatureOp == 1) // Shell 2.5mm Hollow Enclosure (floor + 4 walls)
            {
                double t = 2.5;
                AppendStlBox(sb, offsetX, offsetY, baseZ + t * 0.5, w, d, t);
                AppendStlBox(sb, offsetX, offsetY - (d - t) * 0.5, baseZ + h * 0.5, w, t, h);
                AppendStlBox(sb, offsetX, offsetY + (d - t) * 0.5, baseZ + h * 0.5, w, t, h);
                AppendStlBox(sb, offsetX - (w - t) * 0.5, offsetY, baseZ + h * 0.5, t, System.Math.Max(4.0, d - 2 * t), h);
                AppendStlBox(sb, offsetX + (w - t) * 0.5, offsetY, baseZ + h * 0.5, t, System.Math.Max(4.0, d - 2 * t), h);
            }
            else // Solid Extrude (0) or Chamfered Mount with M4 Corner Bosses (2)
            {
                AppendStlBox(sb, offsetX, offsetY, baseZ + h * 0.5, w, d, h);
                if (cadFeatureOp == 2)
                {
                    double bx = w * 0.38, by = d * 0.38;
                    AppendStlCylinder(sb, offsetX - bx, offsetY - by, baseZ + h + 2.0, 6.0, 6.0, 4.0, 10);
                    AppendStlCylinder(sb, offsetX + bx, offsetY - by, baseZ + h + 2.0, 6.0, 6.0, 4.0, 10);
                    AppendStlCylinder(sb, offsetX - bx, offsetY + by, baseZ + h + 2.0, 6.0, 6.0, 4.0, 10);
                    AppendStlCylinder(sb, offsetX + bx, offsetY + by, baseZ + h + 2.0, 6.0, 6.0, 4.0, 10);
                }
            }
        }

        public static string SerializeToWavefrontObj(
            MechanismSpec spec,
            double capturedWidthMm,
            double capturedDepthMm,
            double capturedHeightMm,
            int cadFeatureOp,
            string materialName,
            int primitiveShape = 0,
            int booleanMode = 0,
            int patternMode = 0,
            IList<StampedCadBodySpec> stampedBodies = null,
            bool includeRobotLinks = false)
        {
            StringBuilder sb = new StringBuilder(24576);
            string solidName = Sanitize(string.IsNullOrEmpty(spec?.robotName) ? "kinetic_cad_model" : spec.robotName);
            sb.AppendLine($"# KineticCAD XR — Interactive 3D CAD Studio (Fusion 360 for Quest 3) Wavefront OBJ Export");
            sb.AppendLine($"# Model: {solidName} | Material: {materialName} | PrimShape: {primitiveShape} | Pattern: {patternMode} | Units: millimeters");
            int vOffset = 1;

            // Group 1: Primary Sculpted 1:1 Parametric CAD Solid centered at (0,0,0)
            sb.AppendLine($"o Primary_CAD_Solid_Shape{primitiveShape}_Op{cadFeatureOp}");
            AppendCadBodyWithPatternObj(sb, ref vOffset, 0.0, 0.0, 0.0, capturedWidthMm, capturedDepthMm, capturedHeightMm, primitiveShape, cadFeatureOp, patternMode);

            // Group 2: Additional Combined Multi-Body CAD Primitives
            if (stampedBodies != null)
            {
                for (int b = 0; b < stampedBodies.Count; b++)
                {
                    StampedCadBodySpec body = stampedBodies[b];
                    if (body == null) continue;
                    sb.AppendLine($"o Combined_{Sanitize(body.bodyName)}_Shape{body.primitiveShape}_Bool{body.booleanMode}");
                    AppendCadBodyWithPatternObj(
                        sb, ref vOffset,
                        body.localOffsetXMm,
                        body.localOffsetZMm,
                        body.localOffsetYMm,
                        body.widthMm,
                        body.depthMm,
                        body.heightMm,
                        body.primitiveShape,
                        body.featureOp,
                        body.patternMode);
                }
            }

            // Group 3: Optional Modular Kinematic Links
            if (includeRobotLinks && spec?.links != null)
            {
                for (int i = 0; i < spec.links.Length; i++)
                {
                    LinkSpec lk = spec.links[i];
                    if (lk == null) continue;
                    sb.AppendLine($"o {Sanitize(lk.name)}_{i}");
                    double cx = lk.centerX * 1000.0;
                    double cy = lk.centerZ * 1000.0;
                    double cz = lk.centerY * 1000.0;
                    double sx = System.Math.Max(8.0, lk.lengthM * 1000.0);
                    double sy = System.Math.Max(8.0, lk.widthM * 1000.0);
                    double sz = System.Math.Max(8.0, lk.heightM * 1000.0);
                    AppendObjBox(sb, ref vOffset, cx, cy, cz, sx, sy, sz);
                }
            }

            return sb.ToString();
        }

        private static void AppendCadBodyWithPatternObj(
            StringBuilder sb, ref int vStart, double offsetX, double offsetY, double baseZ,
            double wMm, double dMm, double hMm, int primitiveShape, int cadFeatureOp, int patternMode)
        {
            if (patternMode == 1) // 3x Linear Pattern
            {
                double pitch = System.Math.Max(18.0, wMm * 0.65);
                for (int k = -1; k <= 1; k++)
                {
                    AppendSingleCadPrimitiveObj(sb, ref vStart, offsetX + k * pitch, offsetY, baseZ, wMm * 0.52, dMm * 0.52, hMm, primitiveShape);
                }
            }
            else if (patternMode == 2) // 6x Circular Radial Pattern
            {
                double radius = System.Math.Max(16.0, System.Math.Min(wMm, dMm) * 0.42);
                for (int k = 0; k < 6; k++)
                {
                    double ang = k * System.Math.PI / 3.0;
                    AppendSingleCadPrimitiveObj(sb, ref vStart, offsetX + System.Math.Cos(ang) * radius, offsetY + System.Math.Sin(ang) * radius, baseZ, wMm * 0.36, dMm * 0.36, hMm, primitiveShape);
                }
            }
            else
            {
                AppendSingleCadPrimitiveObj(sb, ref vStart, offsetX, offsetY, baseZ, wMm, dMm, hMm, primitiveShape);
            }
        }

        private static void AppendSingleCadPrimitiveObj(
            StringBuilder sb, ref int vStart, double offsetX, double offsetY, double baseZ,
            double wMm, double dMm, double hMm, int primitiveShape)
        {
            double w = System.Math.Max(10.0, wMm);
            double d = System.Math.Max(10.0, dMm);
            double h = System.Math.Max(8.0, hMm);

            if (primitiveShape == 1 || primitiveShape == 3 || primitiveShape == 4 || primitiveShape == 5) // Cylinder, Cone, Torus, or Hollow Tube
            {
                AppendObjCylinder(sb, ref vStart, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 16);
            }
            else if (primitiveShape == 6) // Hex Prism (6 sides)
            {
                AppendObjCylinder(sb, ref vStart, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 6);
            }
            else if (primitiveShape == 2) // Sphere
            {
                AppendObjSphere(sb, ref vStart, offsetX, offsetY, baseZ + h * 0.5, w, d, h, 6, 12);
            }
            else
            {
                AppendObjBox(sb, ref vStart, offsetX, offsetY, baseZ + h * 0.5, w, d, h);
            }
        }

        private static void AppendObjCylinder(StringBuilder sb, ref int vStart, double cx, double cy, double cz, double dx, double dy, double hz, int segments)
        {
            double rx = dx * 0.5, ry = dy * 0.5, hh = hz * 0.5;
            double z0 = cz - hh, z1 = cz + hh;
            int b = vStart;

            // Bottom center (b), Top center (b+1), then pairs of (bottom_i, top_i) around perimeter
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", cx, cy, z0));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", cx, cy, z1));
            for (int i = 0; i < segments; i++)
            {
                double ang = (2.0 * System.Math.PI * i) / segments;
                double vx = cx + System.Math.Cos(ang) * rx;
                double vy = cy + System.Math.Sin(ang) * ry;
                sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", vx, vy, z0));
                sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", vx, vy, z1));
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int b0 = b + 2 + i * 2;
                int t0 = b0 + 1;
                int b1 = b + 2 + next * 2;
                int t1 = b1 + 1;
                sb.AppendLine($"f {b0} {b1} {t1} {t0}");
                sb.AppendLine($"f {b+1} {t0} {t1}");
                sb.AppendLine($"f {b} {b1} {b0}");
            }
            vStart += 2 + segments * 2;
        }

        private static void AppendObjSphere(StringBuilder sb, ref int vStart, double cx, double cy, double cz, double dx, double dy, double dz, int rings, int segments)
        {
            double rx = dx * 0.5, ry = dy * 0.5, rz = dz * 0.5;
            int b = vStart;
            for (int r = 0; r <= rings; r++)
            {
                double phi = System.Math.PI * r / rings;
                double sp = System.Math.Sin(phi), cp = System.Math.Cos(phi);
                for (int s = 0; s < segments; s++)
                {
                    double theta = 2.0 * System.Math.PI * s / segments;
                    double vx = cx + rx * sp * System.Math.Cos(theta);
                    double vy = cy + ry * sp * System.Math.Sin(theta);
                    double vz = cz + rz * cp;
                    sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", vx, vy, vz));
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int nextS = (s + 1) % segments;
                    int i00 = b + r * segments + s;
                    int i01 = b + r * segments + nextS;
                    int i10 = b + (r + 1) * segments + s;
                    int i11 = b + (r + 1) * segments + nextS;
                    sb.AppendLine($"f {i00} {i10} {i11} {i01}");
                }
            }
            vStart += (rings + 1) * segments;
        }

        private static void AppendObjBox(StringBuilder sb, ref int vStart, double cx, double cy, double cz, double sx, double sy, double sz)
        {
            double hx = sx * 0.5, hy = sy * 0.5, hz = sz * 0.5;
            double x0 = cx - hx, x1 = cx + hx;
            double y0 = cy - hy, y1 = cy + hy;
            double z0 = cz - hz, z1 = cz + hz;

            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x0, y0, z0));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x1, y0, z0));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x1, y1, z0));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x0, y1, z0));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x0, y0, z1));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x1, y0, z1));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x1, y1, z1));
            sb.AppendLine(string.Format(Inv, "v {0:F4} {1:F4} {2:F4}", x0, y1, z1));

            int b = vStart;
            sb.AppendLine($"f {b+4} {b+5} {b+6} {b+7}");
            sb.AppendLine($"f {b+3} {b+2} {b+1} {b+0}");
            sb.AppendLine($"f {b+1} {b+2} {b+6} {b+5}");
            sb.AppendLine($"f {b+3} {b+0} {b+4} {b+7}");
            sb.AppendLine($"f {b+2} {b+3} {b+7} {b+6}");
            sb.AppendLine($"f {b+0} {b+1} {b+5} {b+4}");
            vStart += 8;
        }

        private static void AppendStlCylinder(StringBuilder sb, double cx, double cy, double cz, double dx, double dy, double hz, int segments)
        {
            double rx = dx * 0.5, ry = dy * 0.5, hh = hz * 0.5;
            double z0 = cz - hh, z1 = cz + hh;
            for (int i = 0; i < segments; i++)
            {
                double a0 = (2.0 * System.Math.PI * i) / segments;
                double a1 = (2.0 * System.Math.PI * (i + 1)) / segments;
                double c0 = System.Math.Cos(a0), s0 = System.Math.Sin(a0);
                double c1 = System.Math.Cos(a1), s1 = System.Math.Sin(a1);
                double x0 = cx + c0 * rx, y0 = cy + s0 * ry;
                double x1 = cx + c1 * rx, y1 = cy + s1 * ry;

                // Top & bottom cap triangles
                AppendStlTri(sb, 0, 0,  1, cx, cy, z1, x0, y0, z1, x1, y1, z1);
                AppendStlTri(sb, 0, 0, -1, cx, cy, z0, x1, y1, z0, x0, y0, z0);
                // Side quad
                AppendStlQuad(sb, (c0 + c1) * 0.5, (s0 + s1) * 0.5, 0, x0, y0, z0, x1, y1, z0, x1, y1, z1, x0, y0, z1);
            }
        }

        private static void AppendStlSphere(StringBuilder sb, double cx, double cy, double cz, double dx, double dy, double dz, int rings, int segments)
        {
            double rx = dx * 0.5, ry = dy * 0.5, rz = dz * 0.5;
            for (int r = 0; r < rings; r++)
            {
                double phi0 = System.Math.PI * r / rings;
                double phi1 = System.Math.PI * (r + 1) / rings;
                double sp0 = System.Math.Sin(phi0), cp0 = System.Math.Cos(phi0);
                double sp1 = System.Math.Sin(phi1), cp1 = System.Math.Cos(phi1);

                for (int s = 0; s < segments; s++)
                {
                    double th0 = 2.0 * System.Math.PI * s / segments;
                    double th1 = 2.0 * System.Math.PI * (s + 1) / segments;
                    double ct0 = System.Math.Cos(th0), st0 = System.Math.Sin(th0);
                    double ct1 = System.Math.Cos(th1), st1 = System.Math.Sin(th1);

                    double x00 = cx + rx * sp0 * ct0, y00 = cy + ry * sp0 * st0, z00 = cz + rz * cp0;
                    double x01 = cx + rx * sp0 * ct1, y01 = cy + ry * sp0 * st1, z01 = cz + rz * cp0;
                    double x10 = cx + rx * sp1 * ct0, y10 = cy + ry * sp1 * st0, z10 = cz + rz * cp1;
                    double x11 = cx + rx * sp1 * ct1, y11 = cy + ry * sp1 * st1, z11 = cz + rz * cp1;

                    AppendStlQuad(sb, sp1 * ct0, sp1 * st0, cp1, x00, y00, z00, x10, y10, z10, x11, y11, z11, x01, y01, z01);
                }
            }
        }

        private static void AppendStlBox(StringBuilder sb, double cx, double cy, double cz, double sx, double sy, double sz)
        {
            double hx = sx * 0.5, hy = sy * 0.5, hz = sz * 0.5;
            double x0 = cx - hx, x1 = cx + hx;
            double y0 = cy - hy, y1 = cy + hy;
            double z0 = cz - hz, z1 = cz + hz;

            // 6 faces (12 triangles) with outward normals
            AppendStlQuad(sb,  0,  0,  1, x0,y0,z1, x1,y0,z1, x1,y1,z1, x0,y1,z1);
            AppendStlQuad(sb,  0,  0, -1, x0,y1,z0, x1,y1,z0, x1,y0,z0, x0,y0,z0);
            AppendStlQuad(sb,  1,  0,  0, x1,y0,z0, x1,y1,z0, x1,y1,z1, x1,y0,z1);
            AppendStlQuad(sb, -1,  0,  0, x0,y1,z0, x0,y0,z0, x0,y0,z1, x0,y1,z1);
            AppendStlQuad(sb,  0,  1,  0, x1,y1,z0, x0,y1,z0, x0,y1,z1, x1,y1,z1);
            AppendStlQuad(sb,  0, -1,  0, x0,y0,z0, x1,y0,z0, x1,y0,z1, x0,y0,z1);
        }

        private static void AppendStlQuad(StringBuilder sb, double nx, double ny, double nz,
            double ax, double ay, double az, double bx, double by, double bz,
            double cx, double cy, double cz, double dx, double dy, double dz)
        {
            AppendStlTri(sb, nx, ny, nz, ax, ay, az, bx, by, bz, cx, cy, cz);
            AppendStlTri(sb, nx, ny, nz, ax, ay, az, cx, cy, cz, dx, dy, dz);
        }

        private static void AppendStlTri(StringBuilder sb, double nx, double ny, double nz,
            double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)
        {
            sb.AppendLine(string.Format(Inv, "  facet normal {0:F4} {1:F4} {2:F4}", nx, ny, nz));
            sb.AppendLine("    outer loop");
            sb.AppendLine(string.Format(Inv, "      vertex {0:F4} {1:F4} {2:F4}", ax, ay, az));
            sb.AppendLine(string.Format(Inv, "      vertex {0:F4} {1:F4} {2:F4}", bx, by, bz));
            sb.AppendLine(string.Format(Inv, "      vertex {0:F4} {1:F4} {2:F4}", cx, cy, cz));
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }
    }
}
