using System;
using KineticCore.Kinematics;

namespace KineticCore.Schema
{
    [Serializable]
    public class MechanismSpec
    {
        public string robotName = "kinetic_cad_mechanism";
        public double targetCaliperWidthMm = 65.0;
        public LinkSpec[] links;
        public JointSpec[] joints;
        public double[] endEffectorHomePosition;

        public PoEChain ToPoEChain(out double[] minLimits, out double[] maxLimits)
        {
            int n = joints != null ? joints.Length : 0;
            ScrewAxis[] axes = new ScrewAxis[n];
            minLimits = new double[n];
            maxLimits = new double[n];

            for (int i = 0; i < n; i++)
            {
                JointSpec j = joints[i];
                Vec3 axis = new Vec3(j.axisX, j.axisY, j.axisZ);
                Vec3 origin = new Vec3(j.originX, j.originY, j.originZ);

                if (string.Equals(j.jointType, "prismatic", StringComparison.OrdinalIgnoreCase))
                    axes[i] = ScrewAxis.Prismatic(axis.Normalized());
                else
                    axes[i] = ScrewAxis.Revolute(axis, origin);

                minLimits[i] = j.minLimitRad;
                maxLimits[i] = j.maxLimitRad;
            }

            Mat4 homeM = Mat4.Identity;
            if (endEffectorHomePosition != null && endEffectorHomePosition.Length == 3)
            {
                homeM.SetM(0, 3, endEffectorHomePosition[0]);
                homeM.SetM(1, 3, endEffectorHomePosition[1]);
                homeM.SetM(2, 3, endEffectorHomePosition[2]);
            }

            return new PoEChain(axes, homeM);
        }
    }

    [Serializable]
    public class LinkSpec
    {
        public string name;
        public double lengthM;
        public double widthM;
        public double heightM;
        public double massKg;
        public double centerX;
        public double centerY;
        public double centerZ;
    }

    [Serializable]
    public class JointSpec
    {
        public string name;
        public string jointType;
        public string parentLink;
        public string childLink;
        public double originX;
        public double originY;
        public double originZ;
        public double axisX;
        public double axisY;
        public double axisZ;
        public double minLimitRad;
        public double maxLimitRad;
    }
}