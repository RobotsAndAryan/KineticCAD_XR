using System;

namespace KineticCore.Kinematics
{
    public struct ScrewAxis
    {
        public Vec3 Omega;
        public Vec3 V;

        public ScrewAxis(Vec3 omega, Vec3 v)
        {
            Omega = omega;
            V = v;
        }

        /// <summary>
        /// Creates a revolute screw axis for unit rotation about axis 'omega' passing through point 'q'.
        /// v = -omega x q = q x omega.
        /// </summary>
        public static ScrewAxis Revolute(Vec3 omega, Vec3 q)
        {
            Vec3 unitOmega = omega.Normalized();
            Vec3 v = Vec3.Cross(q, unitOmega);
            return new ScrewAxis(unitOmega, v);
        }

        /// <summary>
        /// Creates a prismatic screw axis for linear translation along unit axis 'v'.
        /// </summary>
        public static ScrewAxis Prismatic(Vec3 v)
        {
            return new ScrewAxis(new Vec3(0.0, 0.0, 0.0), v.Normalized());
        }

        /// <summary>
        /// Returns the 4x4 se(3) matrix representation [S] of the screw axis.
        /// </summary>
        public Mat4 Hat()
        {
            Mat4 m = new Mat4();
            m.SetM(0, 0, 0.0);      m.SetM(0, 1, -Omega.Z); m.SetM(0, 2, Omega.Y);  m.SetM(0, 3, V.X);
            m.SetM(1, 0, Omega.Z);  m.SetM(1, 1, 0.0);      m.SetM(1, 2, -Omega.X); m.SetM(1, 3, V.Y);
            m.SetM(2, 0, -Omega.Y); m.SetM(2, 1, Omega.X);  m.SetM(2, 2, 0.0);      m.SetM(2, 3, V.Z);
            m.SetM(3, 0, 0.0);      m.SetM(3, 1, 0.0);      m.SetM(3, 2, 0.0);      m.SetM(3, 3, 0.0);
            return m;
        }

        /// <summary>
        /// Computes the closed-form 4x4 SE(3) matrix exponential exp([S]*theta).
        /// </summary>
        public Mat4 Exp(double theta)
        {
            double omegaMag = Omega.Magnitude();

            // Pure prismatic joint (||omega|| == 0): T = [I, v * theta; 0, 1]
            if (omegaMag < 1e-9)
            {
                Mat4 pMat = Mat4.Identity;
                pMat.SetM(0, 3, V.X * theta);
                pMat.SetM(1, 3, V.Y * theta);
                pMat.SetM(2, 3, V.Z * theta);
                return pMat;
            }

            // Revolute / Helical joint (||omega|| == 1): Rodrigues' formula + G(theta)*v
            Vec3 w = Omega.Normalized();
            double s = System.Math.Sin(theta);
            double c = System.Math.Cos(theta);
            double oneMinusC = 1.0 - c;

            double wx = w.X, wy = w.Y, wz = w.Z;

            // Rotation matrix R = I + sin(theta)[w] + (1 - cos(theta))[w]^2
            double r00 = 1.0 + oneMinusC * (-wy * wy - wz * wz);
            double r01 = -s * wz + oneMinusC * (wx * wy);
            double r02 = s * wy + oneMinusC * (wx * wz);

            double r10 = s * wz + oneMinusC * (wx * wy);
            double r11 = 1.0 + oneMinusC * (-wx * wx - wz * wz);
            double r12 = -s * wx + oneMinusC * (wy * wz);

            double r20 = -s * wy + oneMinusC * (wx * wz);
            double r21 = s * wx + oneMinusC * (wy * wz);
            double r22 = 1.0 + oneMinusC * (-wx * wx - wy * wy);

            // Translation vector p = G(theta)v = theta*v + (1 - cos(theta))(w x v) + (theta - sin(theta))(w x (w x v))
            Vec3 wCrossV = Vec3.Cross(w, V);
            Vec3 wCrossWCrossV = Vec3.Cross(w, wCrossV);
            Vec3 p = (V * theta) + (wCrossV * oneMinusC) + (wCrossWCrossV * (theta - s));

            Mat4 result = Mat4.Identity;
            result.SetM(0, 0, r00); result.SetM(0, 1, r01); result.SetM(0, 2, r02); result.SetM(0, 3, p.X);
            result.SetM(1, 0, r10); result.SetM(1, 1, r11); result.SetM(1, 2, r12); result.SetM(1, 3, p.Y);
            result.SetM(2, 0, r20); result.SetM(2, 1, r21); result.SetM(2, 2, r22); result.SetM(2, 3, p.Z);
            return result;
        }
    }
}