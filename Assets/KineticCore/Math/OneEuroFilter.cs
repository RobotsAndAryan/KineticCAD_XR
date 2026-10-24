using System;

namespace KineticCore.Math
{
    public class OneEuroFilter
    {
        private readonly double _minCutoff, _beta, _dcutoff;
        private double _xPrev, _dxPrev, _tPrev;
        private bool _init;

        public OneEuroFilter(double minCutoff = 1.0, double beta = 0.007, double dcutoff = 1.0)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dcutoff = dcutoff;
        }

        public void Reset() { _init = false; }

        public double Filter(double x, double t)
        {
            if (!_init)
            {
                _init = true;
                _xPrev = x;
                _dxPrev = 0.0;
                _tPrev = t;
                return x;
            }

            double dt = t - _tPrev;
            if (dt <= 0.0) dt = 1e-9;

            double dx = (x - _xPrev) / dt;
            double aDx = Alpha(_dcutoff, dt);
            double dxHat = aDx * dx + (1.0 - aDx) * _dxPrev;
            double cutoff = _minCutoff + _beta * System.Math.Abs(dxHat);
            double aX = Alpha(cutoff, dt);
            double xHat = aX * x + (1.0 - aX) * _xPrev;

            _xPrev = xHat;
            _dxPrev = dxHat;
            _tPrev = t;
            return xHat;
        }

        private static double Alpha(double cutoff, double dt)
        {
            double tau = 1.0 / (2.0 * System.Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
    }

    public class OneEuroFilter3
    {
        private readonly OneEuroFilter _fx, _fy, _fz;

        public OneEuroFilter3(double minCutoff = 1.0, double beta = 0.007, double dcutoff = 1.0)
        {
            _fx = new OneEuroFilter(minCutoff, beta, dcutoff);
            _fy = new OneEuroFilter(minCutoff, beta, dcutoff);
            _fz = new OneEuroFilter(minCutoff, beta, dcutoff);
        }

        public void Reset() { _fx.Reset(); _fy.Reset(); _fz.Reset(); }

        public (double x, double y, double z) Filter(double x, double y, double z, double t)
        {
            return (_fx.Filter(x, t), _fy.Filter(y, t), _fz.Filter(z, t));
        }
    }
}   