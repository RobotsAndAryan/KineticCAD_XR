namespace KineticCore.Kinematics
{
    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 Zero => new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X*s, a.Y*s, a.Z*s);

        public double Magnitude() => System.Math.Sqrt(X*X + Y*Y + Z*Z);
        public Vec3 Normalized() { double m = Magnitude(); return m > 1e-12 ? this * (1.0/m) : Zero; }

        public static double Dot(Vec3 a, Vec3 b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y*b.Z - a.Z*b.Y,
            a.Z*b.X - a.X*b.Z,
            a.X*b.Y - a.Y*b.X
        );
    }
}   