using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MoonRocket
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")]
        static extern short GetKeyState(int nVirtKey);

        private Timer _timer = new Timer();

        const double _G = 6.673e-11;
        const double _tickLen = 2;
        readonly PointD _earthP = new PointD(0, 0), _moonP = new PointD(384399000, 0);
        const double _earthR = 6378100, _earthM = 5.9742e24;
        const double _moonR = 1737100, _moonM = 7.3477e22;
        const double _crashVN = 20, _crashVO = 2, _crashVA = Math.PI / 36;
        const double _rocketT = 20, _rocketStartAngle = Math.PI, _rocketAT = Math.PI / 18000;
        const double _landAngleTolerance = Math.PI / 6;

        class State
        {
            public PointD _rocketP, _rocketV;
            public double _rocketA, _rocketVA;
        }

        bool _thrust, _turnCW, _turnCCW, _path;
        RectangleF _screen
        {
            get { return _screens[_nScreen]; }
        }
        RectangleF[] _screens = new RectangleF[4];
        int _nScreen = 0;

        Form2 _info;
        State _s
        {
            get { return _next[_cur]; }
            set { _next[_cur] = value; }
        }
        int _cur = 0;
        State[] _next = new State[5000];
        object _locker = new object();

        public Form1()
        {
            InitializeComponent();

            _s = new State();
            _s._rocketV = new PointD(0, 0);
            _s._rocketP = new PointD(_earthP.X + _earthR * Math.Cos(_rocketStartAngle), _earthP.Y + _earthR * Math.Sin(_rocketStartAngle));
            _s._rocketA = _rocketStartAngle;
            _s._rocketVA = 0;
            _thrust = false;
            _turnCW = false;
            _turnCCW = false;
            _path = false;

            Activated += new EventHandler(Form1_Activated);
            Paint += new PaintEventHandler(Form1_Paint);
            SizeChanged += new EventHandler(Form1_SizeChanged);

            _info = new Form2();
            _info.Paint += new PaintEventHandler(_info_Paint);

            _timer.Tick += new EventHandler(_timer_Tick);
            _timer.Interval = 50;

            this.DoubleBuffered = true;
        }

        void Form1_SizeChanged(object sender, EventArgs e)
        {
            if (DisplayRectangle.Width < DisplayRectangle.Height)
            {
                Height -= (DisplayRectangle.Height - DisplayRectangle.Width);
            }
            else if (DisplayRectangle.Height < DisplayRectangle.Width)
            {
                Width -= (DisplayRectangle.Width - DisplayRectangle.Height);
            }
        }

        Point Transform(double x, double y)
        {
            float fx = (float)x;
            float fy = -(float)y;
            float rx = (float)DisplayRectangle.Width * ((fx - _screen.Left) / _screen.Width);
            float ry = (float)DisplayRectangle.Height * ((fy - _screen.Top) / _screen.Height);
            return new Point((int)rx, (int)ry);
        }

        Bitmap bm;
        void Form1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = Graphics.FromImage(bm);

            g.Clear(Color.Black);
            int earthRad = Transform(_screen.Left + _earthR, 0).X;
            int moonRad = Transform(_screen.Left + _moonR, 0).X;
            Point earthP1 = Transform(_earthP.X - _earthR, _earthP.Y - _earthR);
            Point earthP2 = Transform(_earthP.X + _earthR, _earthP.Y + _earthR);
            Point moonP = Transform(_moonP.X, _moonP.Y);

            Pen pEarth = new Pen(Brushes.Blue);
            Pen pMoon = new Pen(Brushes.Gray);
            Pen p1 = new Pen(Brushes.LightGray);
            Pen p2 = new Pen(Brushes.Red);

            Rectangle earthR = new Rectangle(earthP1.X, earthP1.Y, earthP2.X - earthP1.X, earthP2.Y - earthP1.Y);
            g.DrawRectangle(pEarth, earthR);
            g.DrawEllipse(pEarth, earthR);

            moonP.Offset(-moonRad, -moonRad);
            Rectangle moonR = new Rectangle(moonP, new Size(2 * moonRad, 2 * moonRad));
            g.DrawEllipse(pMoon, moonR);

            lock (_locker)
            {
                Point rocketP, rocketP2;
                for (int i = _next.Length - 6; _path && i > _cur; i -= 5)
                {
                    if (_next[i + 5] == null)
                        continue;
                    rocketP = Transform(_next[i]._rocketP.X, _next[i]._rocketP.Y);
                    rocketP2 = Transform(_next[i + 5]._rocketP.X, _next[i + 5]._rocketP.Y);
                    g.DrawLine(p1, rocketP, rocketP2);
                }
                if (_cur >= _next.Length || _next[_cur] == null)
                    return;
                rocketP = Transform(_next[_cur]._rocketP.X, _next[_cur]._rocketP.Y);
                if (_cur + 1 >= _next.Length || _next[_cur + 1] == null)
                    rocketP2 = new Point(rocketP.X + 1, rocketP.Y);
                else
                    rocketP2 = Transform(_next[_cur + 1]._rocketP.X, _next[_cur + 1]._rocketP.Y);
                g.DrawLine(p2, rocketP, rocketP2);
            }

            e.Graphics.DrawImage(bm, 0, 0);
        }

        void Form1_Activated(object sender, EventArgs e)
        {
            RecomputeAll();
            {
                double width = _moonP.X + 3 * (_earthR + _moonR);
                double ratio = (double)DisplayRectangle.Height / (double)DisplayRectangle.Width;
                double height = ratio * width;
                _screens[0] = new RectangleF(-3f * (float)_earthR, -0.5f * (float)height, (float)width, (float)height);
            }
            {
                _screens[1] = new RectangleF(-2f * (float)_earthR, -2f * (float)_earthR, 4f * (float)_earthR, 4f * (float)_earthR);
            }
            {
                _screens[2] = new RectangleF((float)_moonP.X - 2f * (float)_moonR, (float)_moonP.Y - 2f * (float)_moonR, 4f * (float)_moonR, 4f * (float)_moonR);
            }

            bm = new Bitmap(DisplayRectangle.Width, DisplayRectangle.Height);

            _info.Show();
            _timer.Start();
        }

        Point InfoTransform(double x, double y)
        {
            double rx = (double)_info.DisplayRectangle.Width * ((x + 1) / 2);
            double ry = (double)_info.DisplayRectangle.Height * ((-y + 1) / 2);
            return new Point((int)rx, (int)ry);
        }
        
        void _info_Paint(object sender, PaintEventArgs e)
        {
            double vel = Math.Sqrt(_s._rocketV.X * _s._rocketV.X + _s._rocketV.Y * _s._rocketV.Y);
            Point o = InfoTransform(0, 0);
            Point moveLine = (vel == 0) ? o : InfoTransform(_s._rocketV.X / 10000, _s._rocketV.Y / 10000);
            Point dirLine = InfoTransform(Math.Cos(_s._rocketA), Math.Sin(_s._rocketA));
            PointF textP = InfoTransform(-1, -1);

            e.Graphics.FillRectangle(Brushes.Black, _info.DisplayRectangle);
            e.Graphics.DrawLine(new Pen(Brushes.Red, 3), o, moveLine);
            e.Graphics.DrawLine(new Pen(Brushes.Cyan), o, dirLine);

            //e.Graphics.DrawString()
        }

        struct PointD
        {
            public PointD(double x, double y) { X = x; Y = y; }
            public double X;
            public double Y;
        }

        State FindNext(State s, bool thrust, bool turnCCW, bool turnCW)
        {
            double earthDX = _earthP.X - s._rocketP.X;
            double earthDY = _earthP.Y - s._rocketP.Y;
            double earthD2 = earthDX * earthDX + earthDY * earthDY;
            double aFac = _G * _earthM / (earthD2 * Math.Sqrt(earthD2));
            double earthAX = aFac * earthDX;
            double earthAY = aFac * earthDY;

            double moonDX = _moonP.X - s._rocketP.X;
            double moonDY = _moonP.Y - s._rocketP.Y;
            double moonD2 = moonDX * moonDX + moonDY * moonDY;
            aFac = _G * _moonM / (moonD2 * Math.Sqrt(moonD2));
            double moonAX = aFac * moonDX;
            double moonAY = aFac * moonDY;

            double AX = earthAX + moonAX;
            double AY = earthAY + moonAY;
            double A = s._rocketA;
            double VA = s._rocketVA;
            if (turnCCW)
            {
                VA += _rocketAT * _tickLen;
            }
            else if (turnCW)
            {
                VA -= _rocketAT * _tickLen;
            }
            A += VA * _tickLen + 2 * Math.PI;
            A %= (2 * Math.PI);
            if (thrust)
            {
                AX += _rocketT * Math.Cos(A);
                AY += _rocketT * Math.Sin(A);
            }

            double VX = s._rocketV.X + AX * _tickLen;
            double VY = s._rocketV.Y + AY * _tickLen;

            double X = s._rocketP.X + VX * _tickLen;
            double Y = s._rocketP.Y + VY * _tickLen;

            earthDX = X - _earthP.X;
            earthDY = Y - _earthP.Y;
            moonDX = X - _moonP.X;
            moonDY = Y - _moonP.Y;
            if (Math.Sqrt(earthDX * earthDX + earthDY * earthDY) < _earthR)
            {
                double locAngle = Math.Atan2(earthDY, earthDX);
                if (Math.Abs(locAngle - A) > _landAngleTolerance)
                {
                    return null;
                }
                else
                {
                    double trajAngle = Math.Atan2(VY, VX);
                    double impAngle = (trajAngle - locAngle + 4 * Math.PI) % (2 * Math.PI);

                    double vel = Math.Sqrt(VX * VX + VY * VY);
                    double velN = vel * Math.Abs(Math.Cos(impAngle));
                    double velO = vel * Math.Abs(Math.Sin(impAngle));
                    if (velN > _crashVN || velO > _crashVO)
                        return null;
                    else
                    {
                        X = s._rocketP.X;
                        Y = s._rocketP.Y;
                        VX = 0;
                        VY = 0;
                    }
                }
            }
            else if (Math.Sqrt(moonDX * moonDX + moonDY * moonDY) < _moonR)
            {
                double locAngle = Math.Atan2(moonDY, moonDX);
                if (Math.Abs(locAngle - A) > _landAngleTolerance)
                {
                    return null;
                }
                else
                {
                    double trajAngle = Math.Atan2(VY, VX);
                    double impAngle = (trajAngle - locAngle + 4 * Math.PI) % (2 * Math.PI);

                    double vel = Math.Sqrt(VX * VX + VY * VY);
                    double velN = vel * Math.Abs(Math.Cos(impAngle));
                    double velO = vel * Math.Abs(Math.Sin(impAngle));
                    if (velN > _crashVN || velO > _crashVO)
                        return null;
                    else
                    {
                        X = s._rocketP.X;
                        Y = s._rocketP.Y;
                        VX = 0;
                        VY = 0;
                    }
                }
            }

            State ss = new State();
            ss._rocketP.X = X;
            ss._rocketP.Y = Y;
            ss._rocketV.X = VX;
            ss._rocketV.Y = VY;
            ss._rocketA = A;
            ss._rocketVA = VA;
            return ss;
        }

        void RecomputeAll()
        {
            _next[0] = FindNext(_s, _thrust, _turnCCW, _turnCW);
            for (int i = 1; i < _next.Length; i++)
                _next[i] = null;
            _cur = 0;
            if (_next[0] == null)
                return;

            for (int i = 1; i < _next.Length; i++)
            {
                _next[i] = FindNext(_next[i - 1], false, false, false);
                if (_next[i] == null)
                    return;
            }
        }

        float _zoomWidth = (float)Math.Pow(2, 15);
        void SetupRocketScreen()
        {
            _screens[3] = new RectangleF((float)_s._rocketP.X - _zoomWidth / 2, -(float)_s._rocketP.Y - _zoomWidth / 2, _zoomWidth, _zoomWidth);
        }

        const int VK_LEFT = 0x25;
        const int VK_UP = 0x26;
        const int VK_RIGHT = 0x27;
        const int VK_SPACE = 0x20;
        const int VK_ADD = 0x6B;
        const int VK_SUBTRACT = 0x6D;
        const int cP = 0x50;
        int _counter = 0;
        void _timer_Tick(object sender, EventArgs e)
        {
            short up = GetKeyState(VK_UP);
            short left = GetKeyState(VK_LEFT);
            short right = GetKeyState(VK_RIGHT);
            short space = GetKeyState(VK_SPACE);
            short plus = GetKeyState(VK_ADD);
            short minus = GetKeyState(VK_SUBTRACT);
            short P = GetKeyState(cP);
            _thrust = (up < 0);
            _turnCCW = (left < 0);
            _turnCW = (right < 0);
            _path ^= (P < 0);
            if (plus < 0)
            {
                _zoomWidth /= 2;
            }
            if (minus < 0)
            {
                _zoomWidth *= 2;
            }

            lock (_locker)
            {
                if (_thrust || _turnCCW || _turnCW || _cur + 1 >= _next.Length)
                    RecomputeAll();
                else
                    _cur++;

                _thrust = false;
                _turnCCW = false;
                _turnCW = false;

                if (_s == null)
                {
                    _timer.Stop();
                    return;
                }

            }

            if (_counter++ % 4 == 0)
            {
                if (space < 0)
                {
                    _nScreen++;
                    _nScreen %= _screens.Length;
                }
                SetupRocketScreen();
                Invalidate();
                _info.Invalidate();
            }
        }
    }
}
