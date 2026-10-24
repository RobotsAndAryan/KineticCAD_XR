using System.Globalization;
using System.IO;
using System.Text;
using KineticCore.Schema;

namespace KineticCore.Export
{
    public static class UrdfSerializer
    {
        public static string SerializeToUrdfXml(MechanismSpec spec)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine($"<robot name=\"{spec.robotName}\">");

            if (spec.links != null)
            {
                foreach (LinkSpec link in spec.links)
                {
                    double m = link.massKg > 0 ? link.massKg : 0.1;
                    double lx = link.lengthM > 0 ? link.lengthM : 0.05;
                    double ly = link.heightM > 0 ? link.heightM : 0.02;
                    double lz = link.widthM > 0 ? link.widthM : 0.02;

                    double ixx = (m / 12.0) * (ly * ly + lz * lz);
                    double iyy = (m / 12.0) * (lx * lx + lz * lz);
                    double izz = (m / 12.0) * (lx * lx + ly * ly);

                    sb.AppendLine($"  <link name=\"{link.name}\">");
                    sb.AppendLine("    <inertial>");
                    sb.AppendLine(string.Format(inv, "      <origin xyz=\"{0:F4} {1:F4} {2:F4}\" rpy=\"0 0 0\"/>", link.centerX, link.centerY, link.centerZ));
                    sb.AppendLine(string.Format(inv, "      <mass value=\"{0:F4}\"/>", m));
                    sb.AppendLine(string.Format(inv, "      <inertia ixx=\"{0:E4}\" ixy=\"0\" ixz=\"0\" iyy=\"{1:E4}\" iyz=\"0\" izz=\"{2:E4}\"/>", ixx, iyy, izz));
                    sb.AppendLine("    </inertial>");
                    sb.AppendLine("    <visual>");
                    sb.AppendLine(string.Format(inv, "      <origin xyz=\"{0:F4} {1:F4} {2:F4}\" rpy=\"0 0 0\"/>", link.centerX, link.centerY, link.centerZ));
                    sb.AppendLine("      <geometry>");
                    sb.AppendLine(string.Format(inv, "        <box size=\"{0:F4} {1:F4} {2:F4}\"/>", lx, ly, lz));
                    sb.AppendLine("      </geometry>");
                    sb.AppendLine("    </visual>");
                    sb.AppendLine("    <collision>");
                    sb.AppendLine(string.Format(inv, "      <origin xyz=\"{0:F4} {1:F4} {2:F4}\" rpy=\"0 0 0\"/>", link.centerX, link.centerY, link.centerZ));
                    sb.AppendLine("      <geometry>");
                    sb.AppendLine(string.Format(inv, "        <box size=\"{0:F4} {1:F4} {2:F4}\"/>", lx, ly, lz));
                    sb.AppendLine("      </geometry>");
                    sb.AppendLine("    </collision>");
                    sb.AppendLine("  </link>");
                }
            }

            sb.AppendLine("  <link name=\"ee_tip\"/>");

            if (spec.joints != null)
            {
                for (int i = 0; i < spec.joints.Length; i++)
                {
                    JointSpec j = spec.joints[i];
                    double relX = i == 0 ? j.originX : j.originX - spec.joints[i - 1].originX;
                    double relY = i == 0 ? j.originY : j.originY - spec.joints[i - 1].originY;
                    double relZ = i == 0 ? j.originZ : j.originZ - spec.joints[i - 1].originZ;

                    sb.AppendLine($"  <joint name=\"{j.name}\" type=\"{j.jointType}\">");
                    sb.AppendLine($"    <parent link=\"{j.parentLink}\"/>");
                    sb.AppendLine($"    <child link=\"{j.childLink}\"/>");
                    sb.AppendLine(string.Format(inv, "    <origin xyz=\"{0:F4} {1:F4} {2:F4}\" rpy=\"0 0 0\"/>", relX, relY, relZ));
                    sb.AppendLine(string.Format(inv, "    <axis xyz=\"{0:F4} {1:F4} {2:F4}\"/>", j.axisX, j.axisY, j.axisZ));
                    sb.AppendLine(string.Format(inv, "    <limit lower=\"{0:F4}\" upper=\"{1:F4}\" effort=\"15.0\" velocity=\"3.14\"/>", j.minLimitRad, j.maxLimitRad));
                    sb.AppendLine("  </joint>");
                }
            }

            sb.AppendLine("</robot>");
            return sb.ToString();
        }

        public static string SaveUrdfToFile(MechanismSpec spec, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string filePath = Path.Combine(directoryPath, $"{spec.robotName}.urdf");
            File.WriteAllText(filePath, SerializeToUrdfXml(spec));
            return filePath;
        }
    }
}