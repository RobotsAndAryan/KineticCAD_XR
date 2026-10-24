using System;

namespace KineticCore.Math
{
    public class PoEChain
    {
        public readonly ScrewAxis[] Joints;
        public readonly Mat4 HomeM;

        public PoEChain(ScrewAxis[] joints, Mat4 homeM)
        {
            Joints = joints;
            HomeM = homeM;
        }

        public Mat4 ForwardKinematics(double[] theta)
        {
            Mat4 T = Mat4.Identity;
            for (int i = 0; i < Joints.Length; i++)
            {
                T = T * Joints[i].Exp(theta[i]);
            }
            return T * HomeM;
        }

        public Vec3 EndEffectorPosition(double[] theta)
        {
            return ForwardKinematics(theta).GetTranslation();
        }

        public Mat4[] ComputeCumulativeJointTransforms(double[] theta)
        {
            int n = Joints.Length;
            Mat4[] transforms = new Mat4[n];
            Mat4 T = Mat4.Identity;
            for (int i = 0; i < n; i++)
            {
                T = T * Joints[i].Exp(theta[i]);
                transforms[i] = T;
            }
            return transforms;
        }

        public Vec3[] ComputeLinearJacobian(double[] theta, out Vec3 pEe)
        {
            int n = Joints.Length;
            Vec3[] jCols = new Vec3[n];
            Mat4[] prefixes = new Mat4[n];

            Mat4 T = Mat4.Identity;
            for (int i = 0; i < n; i++)
            {
                prefixes[i] = T;
                T = T * Joints[i].Exp(theta[i]);
            }

            Mat4 totalT = T * HomeM;
            pEe = totalT.GetTranslation();

            for (int i = 0; i < n; i++)
            {
                Mat4 Tpre = prefixes[i];
                Vec3 pPre = Tpre.GetTranslation();

                Vec3 omegaPrime = Tpre.TransformVector(Joints[i].Omega);
                Vec3 vPrime = Vec3.Cross(pPre, omegaPrime) + Tpre.TransformVector(Joints[i].V);

                jCols[i] = Vec3.Cross(omegaPrime, pEe) + vPrime;
            }

            return jCols;
        }
    }
}