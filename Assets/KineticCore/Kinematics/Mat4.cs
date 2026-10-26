namespace KineticCore.Kinematics
{
    public struct Mat4
    {
        public double m00, m01, m02, m03;
        public double m10, m11, m12, m13;
        public double m20, m21, m22, m23;
        public double m30, m31, m32, m33;

        public static Mat4 Identity => new Mat4 { m00=1, m11=1, m22=1, m33=1 };

        public static Mat4 operator *(Mat4 a, Mat4 b)
        {
            var r = new Mat4();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                {
                    double s = 0;
                    for (int k = 0; k < 4; k++)
                        s += a.M(row, k) * b.M(k, col);
                    r.SetM(row, col, s);
                }
            return r;
        }

        public double M(int row, int col)
        {
            switch (row*4 + col)
            {
                case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03;
                case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13;
                case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23;
                case 12: return m30; case 13: return m31; case 14: return m32; case 15: return m33;
                default: return 0;
            }
        }

        public void SetM(int row, int col, double val)
        {
            switch (row*4 + col)
            {
                case 0: m00=val; break; case 1: m01=val; break; case 2: m02=val; break; case 3: m03=val; break;
                case 4: m10=val; break; case 5: m11=val; break; case 6: m12=val; break; case 7: m13=val; break;
                case 8: m20=val; break; case 9: m21=val; break; case 10: m22=val; break; case 11: m23=val; break;
                case 12: m30=val; break; case 13: m31=val; break; case 14: m32=val; break; case 15: m33=val; break;
            }
        }

        public Vec3 TransformPoint(Vec3 p) => new Vec3(
            m00*p.X + m01*p.Y + m02*p.Z + m03,
            m10*p.X + m11*p.Y + m12*p.Z + m13,
            m20*p.X + m21*p.Y + m22*p.Z + m23
        );
    
        public Vec3 TransformVector(Vec3 v)
        {
            return new Vec3(
                M(0,0)*v.X + M(0,1)*v.Y + M(0,2)*v.Z,
                M(1,0)*v.X + M(1,1)*v.Y + M(1,2)*v.Z,
                M(2,0)*v.X + M(2,1)*v.Y + M(2,2)*v.Z);
        }

        public Vec3 GetTranslation()
        {
            return new Vec3(M(0,3), M(1,3), M(2,3));
        }

        public void ToQuaternion(out double qx, out double qy, out double qz, out double qw)
        {
            double r00=M(0,0),r01=M(0,1),r02=M(0,2);
            double r10=M(1,0),r11=M(1,1),r12=M(1,2);
            double r20=M(2,0),r21=M(2,1),r22=M(2,2);
            double trace=r00+r11+r22;
            if(trace>0.0){double s=System.Math.Sqrt(trace+1.0)*2.0;qw=0.25*s;qx=(r21-r12)/s;qy=(r02-r20)/s;qz=(r10-r01)/s;}
            else if(r00>r11&&r00>r22){double s=System.Math.Sqrt(1.0+r00-r11-r22)*2.0;qw=(r21-r12)/s;qx=0.25*s;qy=(r01+r10)/s;qz=(r02+r20)/s;}
            else if(r11>r22){double s=System.Math.Sqrt(1.0+r11-r00-r22)*2.0;qw=(r02-r20)/s;qx=(r01+r10)/s;qy=0.25*s;qz=(r12+r21)/s;}
            else{double s=System.Math.Sqrt(1.0+r22-r00-r11)*2.0;qw=(r10-r01)/s;qx=(r02+r20)/s;qy=(r12+r21)/s;qz=0.25*s;}
        } 
    }  
}   