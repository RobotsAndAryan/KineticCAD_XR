using System;

namespace KineticCore.Kinematics
{
    public static class DlsInverseKinematics
    {
        public static bool SolvePosition(
            PoEChain chain,
            Vec3 targetPosition,
            double[] theta,
            double[] minLimits = null,
            double[] maxLimits = null,
            double lambda = 0.06,
            int maxIterations = 35,
            double tolerance = 1e-4,
            double maxStepRad = 0.20)
        {
            int n = chain.Joints.Length;
            double lambdaSq = lambda * lambda;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                Vec3 pEe;
                Vec3[] jCols = chain.ComputeLinearJacobian(theta, out pEe);
                Vec3 error = targetPosition - pEe;

                if (error.Magnitude() <= tolerance)
                    return true;

                double a00 = lambdaSq, a01 = 0.0, a02 = 0.0;
                double a11 = lambdaSq, a12 = 0.0, a22 = lambdaSq;

                for (int i = 0; i < n; i++)
                {
                    Vec3 col = jCols[i];
                    a00 += col.X * col.X;
                    a01 += col.X * col.Y;
                    a02 += col.X * col.Z;
                    a11 += col.Y * col.Y;
                    a12 += col.Y * col.Z;
                    a22 += col.Z * col.Z;
                }

                Vec3 w = SolveSymmetric3x3(a00, a01, a02, a11, a12, a22, error);

                double maxDelta = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double dTheta = Vec3.Dot(jCols[i], w);
                    if (dTheta > maxStepRad) dTheta = maxStepRad;
                    if (dTheta < -maxStepRad) dTheta = -maxStepRad;

                    theta[i] += dTheta;

                    if (minLimits != null && i < minLimits.Length && theta[i] < minLimits[i])
                        theta[i] = minLimits[i];
                    if (maxLimits != null && i < maxLimits.Length && theta[i] > maxLimits[i])
                        theta[i] = maxLimits[i];

                    if (System.Math.Abs(dTheta) > maxDelta)
                        maxDelta = System.Math.Abs(dTheta);
                }

                if (maxDelta < 1e-6)
                    break;
            }

            Vec3 finalPos = chain.EndEffectorPosition(theta);
            return (targetPosition - finalPos).Magnitude() <= tolerance;
        }

        public static double ComputeManipulabilityIndex(PoEChain chain, double[] theta)
        {
            Vec3 pEe;
            Vec3[] jCols = chain.ComputeLinearJacobian(theta, out pEe);
            double a00 = 1e-6, a01 = 0.0, a02 = 0.0;
            double a11 = 1e-6, a12 = 0.0, a22 = 1e-6;

            for (int i = 0; i < jCols.Length; i++)
            {
                Vec3 c = jCols[i];
                a00 += c.X * c.X;
                a01 += c.X * c.Y;
                a02 += c.X * c.Z;
                a11 += c.Y * c.Y;
                a12 += c.Y * c.Z;
                a22 += c.Z * c.Z;
            }

            double c00 = a11 * a22 - a12 * a12;
            double c01 = -(a01 * a22 - a12 * a02);
            double c02 = a01 * a12 - a11 * a02;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            return det > 0.0 ? System.Math.Sqrt(det) : 0.0;
        }

        private static Vec3 SolveSymmetric3x3(
            double a00, double a01, double a02,
            double a11, double a12, double a22,
            Vec3 b)
        {
            double c00 = a11 * a22 - a12 * a12;
            double c01 = -(a01 * a22 - a12 * a02);
            double c02 = a01 * a12 - a11 * a02;
            double c11 = a00 * a22 - a02 * a02;
            double c12 = -(a00 * a12 - a01 * a02);
            double c22 = a00 * a11 - a01 * a01;

            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (System.Math.Abs(det) < 1e-12)
                return new Vec3(0, 0, 0);

            double invDet = 1.0 / det;
            return new Vec3(
                (c00 * b.X + c01 * b.Y + c02 * b.Z) * invDet,
                (c01 * b.X + c11 * b.Y + c12 * b.Z) * invDet,
                (c02 * b.X + c12 * b.Y + c22 * b.Z) * invDet
            );
        }
    }
}