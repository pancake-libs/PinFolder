using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 画面の右下（タスクバーの上）に浮かぶ丸いボタン。
    /// 縁をなめらかに描き、2 枚の窓の絵が磁石の上で浮き、ホバーや呼び出し時に吸い付く。
    /// </summary>
    internal sealed class FloatButton : Form
    {
        /// <summary>丸の直径（96dpi の値）</summary>
        private const int Diameter = 48;

        /// <summary>画面の端からの間（96dpi の値）</summary>
        private const int EdgeMargin = 16;

        /// <summary>これより動いたらドラッグとみなす（px）</summary>
        private const int DragThreshold = 4;

        /// <summary>描画の更新間隔（約 30fps）</summary>
        private const int FrameIntervalMs = 33;

        /// <summary>待っている時の上下の 1 周期（ms）</summary>
        private const double IdleCycleMs = 2400.0;

        /// <summary>左右の窓の位相のずれ（ラジアン）</summary>
        private const double RightPhaseOffset = 1.2;

        /// <summary>磁石に吸い付く時間（ms）</summary>
        private const double SnapDurationMs = 120.0;

        /// <summary>吸い付いた状態から浮く動きへ戻る時間（ms）</summary>
        private const double ReleaseDurationMs = 220.0;

        /// <summary>PlaySnap 呼び出し後に吸い付きを保つ時間（ms）</summary>
        private const long PlaySnapHoldMs = 600L;

        /// <summary>描けない時が続いたら動きを止める回数</summary>
        private const int MaxRenderFailures = 5;

        /// <summary>操作が無い時にタイマーを止めるまでの時間（ms）</summary>
        private const long IdleTimeoutMs = 60000L;

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        private const string Caption = "PinFolder";
        private const string MagnetResourceName = "float_part_magnet.png";
        private const string WindowResourceName = "float_part_window.png";

        /// <summary>土台の円の塗り色（濃い灰）</summary>
        private static readonly Color CircleBack = Color.FromArgb(0x26, 0x2A, 0x2E);

        /// <summary>土台の円の外周の線色（円の色に近い色。縁のぎざぎざを隠す）</summary>
        private static readonly Color CircleBorder = Color.FromArgb(0x1E, 0x22, 0x26);

        /// <summary>外周の内側の明るい縁の色（半透明。暗い画面の上で形をはっきりさせる）</summary>
        private static readonly Color RimColor = Color.FromArgb(120, 0xF3, 0xF4, 0xF6);

        /// <summary>明るい縁の太さ（直径 48 の時の値）</summary>
        private const float RimWidth = 1f;

        /// <summary>影の広がり（直径 48 の時の値。円はこの分だけ小さく描く）</summary>
        private const float ShadowSpread = 3f;

        /// <summary>影を下へずらす量（直径 48 の時の値）</summary>
        private const float ShadowOffset = 1.5f;

        /// <summary>影を作る円の数（多いほどやわらかい）</summary>
        private const int ShadowLayers = 4;

        /// <summary>影のいちばん濃い所の濃さ（0〜255）</summary>
        private const int ShadowAlpha = 72;

        private readonly Stopwatch _clock;
        private Timer _timer;
        private Image _magnetImage;
        private Image _windowImage;
        private Bitmap _surface;
        private SolidBrush _circleBrush;
        private Pen _borderPen;
        private Pen _rimPen;
        private SolidBrush _shadowBrush;

        private Point _downScreen;
        private Point _downLocation;
        private bool _pressed;
        private bool _dragging;
        private bool _hovered;
        private bool _disposed;
        private bool _renderErrorLogged;
        private int _renderFailures;

        /// <summary>縮めて覚えた絵（毎フレーム縮めないため。大きさが変わったら作り直す）</summary>
        private Bitmap _magnetScaled;
        private Bitmap _windowScaled;

        private double _floatPhase;
        private double _currentSnap;
        private double _lastRenderedSnap;
        private double _transitionStartSnap;
        private double _transitionTargetSnap;
        private double _transitionDurationMs;
        private long _transitionStartMs;
        private long _playSnapUntilMs;
        private long _lastActivityMs;
        private long _lastTickMs;

        /// <summary>左クリックされた</summary>
        public event EventHandler LeftClicked;

        /// <summary>右クリックされた（引数は押した画面の位置）</summary>
        public event EventHandler<MouseEventArgs> RightClicked;

        /// <summary>ドラッグで動かし終えた（位置を覚えるため）</summary>
        public event EventHandler Moved;

        /// <summary>ボタンを作る。</summary>
        /// <param name="x">左の位置（AppConfig.NoPosition なら右下）</param>
        /// <param name="y">上の位置</param>
        public FloatButton(int x, int y)
        {
            _magnetImage = LoadPartImage(MagnetResourceName);
            _windowImage = LoadPartImage(WindowResourceName);
            _circleBrush = new SolidBrush(CircleBack);
            _clock = Stopwatch.StartNew();

            long nowMs = _clock.ElapsedMilliseconds;
            _lastActivityMs = nowMs;
            _lastTickMs = nowMs;
            _lastRenderedSnap = -1.0;
            _transitionDurationMs = SnapDurationMs;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Text = Caption;
            Cursor = Cursors.Hand;

            int size = Scale(Diameter);
            Size = new Size(size, size);
            Location = ValidLocation(x, y, size);

            // 画面ごとの倍率に合わせる
            DpiWatcher.Attach(this);
            _timer = new Timer();
            _timer.Interval = FrameIntervalMs;
            _timer.Tick += OnTimerTick;
        }

        private bool HasImages
        {
            get { return _magnetImage != null && _windowImage != null; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // Alt+Tab の一覧に出さず、ピクセルごとの透明度（UpdateLayeredWindow）を使う
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>フォルダを開いた時などに、窓の絵が磁石へ吸い付く動きを 1 回見せる。</summary>
        public void PlaySnap()
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            // 呼び出しは画面のスレッドだけ（PinFolderContext の OpenEntry）

            long nowMs = _clock.ElapsedMilliseconds;
            _playSnapUntilMs = nowMs + PlaySnapHoldMs;
            WakeActivity();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                // 押しても他のウィンドウから入力の向きを奪わない
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RenderFrame();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_dragging)
            {
                // ドラッグ中に倍率の違う画面へ入って大きさが変わった時は、起点を取り直す（位置が飛ばないように）
                _downLocation = Location;
                _downScreen = Cursor.Position;
            }
            RenderFrame();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                _hovered = false;
                StopTimer();
                return;
            }
            long nowMs = _clock.ElapsedMilliseconds;
            _lastActivityMs = nowMs;
            _lastTickMs = nowMs;
            RenderFrame();
            StartTimer();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // UpdateLayeredWindow で描くため背景の既定描画は行わない
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // UpdateLayeredWindow で描くため既定の描画は行わない
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            WakeActivity();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            WakeActivity();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            WakeActivity();
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            _pressed = true;
            _dragging = false;
            _downScreen = Cursor.Position;
            _downLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressed)
            {
                return;
            }
            _lastActivityMs = _clock.ElapsedMilliseconds;
            Point now = Cursor.Position;
            int dx = now.X - _downScreen.X;
            int dy = now.Y - _downScreen.Y;
            if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < DragThreshold)
            {
                return;
            }
            _dragging = true;
            Location = new Point(_downLocation.X + dx, _downLocation.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _lastActivityMs = _clock.ElapsedMilliseconds;
            if (e.Button == MouseButtons.Right)
            {
                EventHandler<MouseEventArgs> rightHandler = RightClicked;
                if (rightHandler != null)
                {
                    rightHandler(this, e);
                }
                return;
            }
            if (e.Button != MouseButtons.Left || !_pressed)
            {
                return;
            }
            _pressed = false;
            if (_dragging)
            {
                _dragging = false;
                Location = ValidLocation(Left, Top, Width);
                EventHandler movedHandler = Moved;
                if (movedHandler != null)
                {
                    movedHandler(this, EventArgs.Empty);
                }
                return;
            }
            EventHandler leftHandler = LeftClicked;
            if (leftHandler != null)
            {
                leftHandler(this, EventArgs.Empty);
            }
        }

        /// <summary>操作があった時に動きを再開し、すぐ 1 フレーム描く。</summary>
        private void WakeActivity()
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            long nowMs = _clock.ElapsedMilliseconds;
            _lastActivityMs = nowMs;
            if (!HasImages)
            {
                return;
            }
            bool wantSnapped = _hovered || (nowMs < _playSnapUntilMs);
            UpdateSnapTransition(wantSnapped, nowMs);
            if (Visible)
            {
                RenderFrame();
                StartTimer();
            }
        }

        private void StartTimer()
        {
            if (_disposed || _timer == null || !Visible || !HasImages)
            {
                return;
            }
            if (!_timer.Enabled)
            {
                _lastTickMs = _clock.ElapsedMilliseconds;
                _timer.Start();
            }
        }

        private void StopTimer()
        {
            if (_timer != null && _timer.Enabled)
            {
                _timer.Stop();
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed || !Visible || !HasImages)
            {
                StopTimer();
                return;
            }

            long nowMs = _clock.ElapsedMilliseconds;
            long deltaMs = nowMs - _lastTickMs;
            _lastTickMs = nowMs;
            if (deltaMs < 0L)
            {
                deltaMs = 0L;
            }
            else if (deltaMs > 100L)
            {
                deltaMs = 100L;
            }

            // メニュー表示後などに MouseLeave を取りこぼした場合の保険
            if (_hovered && !_pressed && !Bounds.Contains(Cursor.Position))
            {
                _hovered = false;
                _lastActivityMs = nowMs;
            }

            bool playSnapActive = nowMs < _playSnapUntilMs;
            bool wantSnapped = _hovered || playSnapActive;
            UpdateSnapTransition(wantSnapped, nowMs);

            if (_currentSnap < 0.999)
            {
                _floatPhase += (deltaMs / IdleCycleMs) * (Math.PI * 2.0);
                if (_floatPhase >= Math.PI * 2.0)
                {
                    _floatPhase %= (Math.PI * 2.0);
                }
            }

            bool hasActiveInteraction = _hovered || _pressed || playSnapActive;
            if (hasActiveInteraction)
            {
                _lastActivityMs = nowMs;
            }

            // 吸い付ききって見た目が変わらない間は再描画を省く
            bool needsRedraw = !(_currentSnap >= 0.999 && _lastRenderedSnap >= 0.999);
            if (needsRedraw)
            {
                RenderFrame();
            }

            // 操作の無い状態が 60 秒続いたらタイマーを止める
            if (!hasActiveInteraction && _currentSnap <= 0.001 && (nowMs - _lastActivityMs) >= IdleTimeoutMs)
            {
                StopTimer();
            }
        }

        /// <summary>吸い付き具合（0=浮遊、1=吸着）を ease-out で進める。</summary>
        private void UpdateSnapTransition(bool wantSnapped, long nowMs)
        {
            double target = wantSnapped ? 1.0 : 0.0;
            if (Math.Abs(target - _transitionTargetSnap) > 0.001)
            {
                _transitionStartSnap = _currentSnap;
                _transitionTargetSnap = target;
                _transitionStartMs = nowMs;
                _transitionDurationMs = wantSnapped ? SnapDurationMs : ReleaseDurationMs;
            }

            if (Math.Abs(_currentSnap - _transitionTargetSnap) <= 0.0001)
            {
                _currentSnap = _transitionTargetSnap;
                return;
            }

            double elapsed = nowMs - _transitionStartMs;
            if (elapsed <= 0.0)
            {
                return;
            }
            double progress = elapsed / _transitionDurationMs;
            if (progress >= 1.0)
            {
                _currentSnap = _transitionTargetSnap;
                return;
            }

            // 3 次の ease-out で吸い付き・戻りをなめらかにする
            double inv = 1.0 - progress;
            double eased = 1.0 - (inv * inv * inv);
            _currentSnap = _transitionStartSnap + (_transitionTargetSnap - _transitionStartSnap) * eased;
        }

        /// <summary>透明な 32bpp ビットマップへ描き、UpdateLayeredWindow で反映する。</summary>
        private void RenderFrame()
        {
            if (_disposed || IsDisposed || !IsHandleCreated)
            {
                return;
            }
            int width = Width;
            int height = Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            EnsureSurface(width, height);
            try
            {
                using (Graphics g = Graphics.FromImage(_surface))
                {
                    DrawButton(g, width, height);
                }
                ApplyLayeredBitmap(_surface, width, height);
                _lastRenderedSnap = _currentSnap;
                _renderErrorLogged = false;
                _renderFailures = 0;
            }
            catch (Exception ex)
            {
                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    AppLog.Error("描画", "FloatButton", ex, "このフレームの描画を見送る");
                }
                _renderFailures++;
                if (_renderFailures >= MaxRenderFailures)
                {
                    // 失敗を繰り返して PC に負担をかけないよう、動きを止める（マウスが乗ると再開）
                    StopTimer();
                    AppLog.Warn("描画", "FloatButton", "描けない時が " + MaxRenderFailures + " 回続いたため、動きを止めた", "PinFolder を起動し直してください");
                    _renderFailures = 0;
                }
            }
        }

        private void EnsureSurface(int width, int height)
        {
            if (_surface != null && _surface.Width == width && _surface.Height == height)
            {
                return;
            }
            if (_surface != null)
            {
                _surface.Dispose();
            }
            _surface = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        }

        private void DrawButton(Graphics g, int width, int height)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            float size = Math.Min(width, height);
            float scale = size / Diameter;
            float borderWidth = Math.Max(1f, scale);
            // 影の分だけ円を小さくし、少し上に寄せる（影は下に落とす）
            float shadowPad = ShadowSpread * scale;
            float inset = borderWidth * 0.5f + 0.5f + shadowPad;
            RectangleF circleRect = new RectangleF(
                inset,
                inset - ShadowOffset * scale * 0.5f,
                size - inset * 2f,
                size - inset * 2f);

            DrawShadow(g, circleRect, scale);
            g.FillEllipse(_circleBrush, circleRect);

            if (!HasImages)
            {
                DrawFallbackText(g, circleRect, size);
            }
            else
            {
                DrawMagnetAndWindows(g, circleRect, size);
            }

            if (_borderPen == null || Math.Abs(_borderPen.Width - borderWidth) > 0.01f)
            {
                if (_borderPen != null)
                {
                    _borderPen.Dispose();
                }
                _borderPen = new Pen(CircleBorder, borderWidth);
            }
            // 外の線は円の色に近い細い線にし（二重の輪が重く見えないよう）、形は影と内側の明るい縁で見せる
            g.DrawEllipse(_borderPen, circleRect);

            // 内側の細い明るい縁（暗い画面の上でも、円の形が背景に溶けない。明るい画面の上は影で分ける）
            float rim = RimWidth * scale;
            RectangleF rimRect = RectangleF.Inflate(circleRect, -(borderWidth + rim) / 2f, -(borderWidth + rim) / 2f);
            // 毎フレーム作らないよう、太さが変わった時だけ作り直す
            if (_rimPen == null || Math.Abs(_rimPen.Width - rim) > 0.01f)
            {
                if (_rimPen != null)
                {
                    _rimPen.Dispose();
                }
                _rimPen = new Pen(RimColor, rim);
            }
            g.DrawEllipse(_rimPen, rimRect);
        }

        /// <summary>円の下に、やわらかい影を描く（外へ行くほど薄い円を重ねる）。</summary>
        private void DrawShadow(Graphics g, RectangleF circleRect, float scale)
        {
            if (_shadowBrush == null)
            {
                _shadowBrush = new SolidBrush(Color.FromArgb(ShadowAlpha / ShadowLayers, Color.Black));
            }
            RectangleF shadow = circleRect;
            shadow.Offset(0f, ShadowOffset * scale);
            for (int i = ShadowLayers; i >= 1; i--)
            {
                float grow = ShadowSpread * scale * i / ShadowLayers;
                g.FillEllipse(_shadowBrush, RectangleF.Inflate(shadow, grow, grow));
            }
        }

        private void DrawFallbackText(Graphics g, RectangleF circleRect, float size)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float fontPx = Math.Max(7f, size * 0.19f);
            using (Font font = new Font(Font.FontFamily, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.None;
                g.DrawString(Caption, font, textBrush, circleRect, format);
            }
        }

        private void DrawMagnetAndWindows(Graphics g, RectangleF circleRect, float size)
        {
            GraphicsState state = g.Save();
            try
            {
                using (GraphicsPath clipPath = new GraphicsPath())
                {
                    clipPath.AddEllipse(circleRect);
                    g.SetClip(clipPath);
                }

                // 磁石を円の中央の下寄りに描く
                float magMaxW = size * 0.48f;
                float magMaxH = size * 0.46f;
                float magScale = Math.Min(magMaxW / _magnetImage.Width, magMaxH / _magnetImage.Height);
                float magW = _magnetImage.Width * magScale;
                float magH = _magnetImage.Height * magScale;
                float magX = (size - magW) * 0.5f;
                float magY = size * 0.465f;
                g.DrawImage(Scaled(_magnetImage, ref _magnetScaled, magW, magH), magX, magY, magW, magH);

                // 磁石の左右の銀の先の少し上に 2 枚の窓を描く
                float winMaxW = size * 0.24f;
                float winMaxH = size * 0.20f;
                float winScale = Math.Min(winMaxW / _windowImage.Width, winMaxH / _windowImage.Height);
                float winW = _windowImage.Width * winScale;
                float winH = _windowImage.Height * winScale;

                float leftWinX = (magX + magW * 0.16f) - (winW * 0.5f);
                float rightWinX = (magX + magW * 0.84f) - (winW * 0.5f);

                // 吸い付いた時は銀の先に軽く接する高さ
                float snappedY = magY + (size * 0.015f) - winH;
                float floatLift = size * 0.095f;
                float floatAmplitude = size * 0.05f;

                float leftIdleY = snappedY - floatLift + (float)(Math.Sin(_floatPhase) * floatAmplitude);
                float rightIdleY = snappedY - floatLift + (float)(Math.Sin(_floatPhase + RightPhaseOffset) * floatAmplitude);

                float snap = (float)_currentSnap;
                float leftWinY = leftIdleY + (snappedY - leftIdleY) * snap;
                float rightWinY = rightIdleY + (snappedY - rightIdleY) * snap;

                Image window = Scaled(_windowImage, ref _windowScaled, winW, winH);
                g.DrawImage(window, leftWinX, leftWinY, winW, winH);
                g.DrawImage(window, rightWinX, rightWinY, winW, winH);
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>絵を描く大きさに一度だけ縮めて覚える（大きさが変わった時だけ作り直す）。</summary>
        private static Image Scaled(Image source, ref Bitmap cache, float width, float height)
        {
            int w = Math.Max(1, (int)Math.Ceiling(width));
            int h = Math.Max(1, (int)Math.Ceiling(height));
            if (cache != null && cache.Width == w && cache.Height == h)
            {
                return cache;
            }
            if (cache != null)
            {
                cache.Dispose();
            }
            cache = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(cache))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, w, h);
            }
            return cache;
        }

        private void ApplyLayeredBitmap(Bitmap bitmap, int width, int height)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                memDc = CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                NativeSize size = new NativeSize(width, height);
                NativePoint srcPoint = new NativePoint(0, 0);
                BlendFunction blend = new BlendFunction();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;

                if (!UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    IntPtr.Zero,
                    ref size,
                    memDc,
                    ref srcPoint,
                    0,
                    ref blend,
                    ULW_ALPHA))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
                if (memDc != IntPtr.Zero)
                {
                    DeleteDC(memDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static Image LoadPartImage(string resourceName)
        {
            try
            {
                return AppPaths.LoadImage(resourceName);
            }
            catch (Exception ex)
            {
                AppLog.Error("画像読み込み", resourceName, ex, "文字のみの表示に切り替える");
                return null;
            }
        }

        /// <summary>96dpi の値を、今の画面の倍率に合わせる。</summary>
        private static int Scale(int value)
        {
            return Ui.Scale(value);
        }

        /// <summary>どこかの画面の作業領域に収まる位置にする（無い時は主の画面の右下）。</summary>
        private static Point ValidLocation(int x, int y, int size)
        {
            if (x != AppConfig.NoPosition && y != AppConfig.NoPosition)
            {
                Rectangle wanted = new Rectangle(x, y, size, size);
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle area = screen.WorkingArea;
                    if (area.IntersectsWith(wanted))
                    {
                        int left = Math.Max(area.Left, Math.Min(x, area.Right - size));
                        int top = Math.Max(area.Top, Math.Min(y, area.Bottom - size));
                        return new Point(left, top);
                    }
                }
            }
            Rectangle primary = Screen.PrimaryScreen.WorkingArea;
            int margin = Scale(EdgeMargin);
            return new Point(primary.Right - size - margin, primary.Bottom - size - margin);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    if (_timer != null)
                    {
                        _timer.Stop();
                        _timer.Tick -= OnTimerTick;
                        _timer.Dispose();
                        _timer = null;
                    }
                    if (_surface != null)
                    {
                        _surface.Dispose();
                        _surface = null;
                    }
                    if (_borderPen != null)
                    {
                        _borderPen.Dispose();
                        _borderPen = null;
                    }
                    if (_rimPen != null)
                    {
                        _rimPen.Dispose();
                        _rimPen = null;
                    }
                    if (_shadowBrush != null)
                    {
                        _shadowBrush.Dispose();
                        _shadowBrush = null;
                    }
                    if (_circleBrush != null)
                    {
                        _circleBrush.Dispose();
                        _circleBrush = null;
                    }
                    if (_magnetImage != null)
                    {
                        _magnetImage.Dispose();
                        _magnetImage = null;
                    }
                    if (_windowImage != null)
                    {
                        _windowImage.Dispose();
                        _windowImage = null;
                    }
                    if (_magnetScaled != null)
                    {
                        _magnetScaled.Dispose();
                        _magnetScaled = null;
                    }
                    if (_windowScaled != null)
                    {
                        _windowScaled.Dispose();
                        _windowScaled = null;
                    }
                }
            }
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;

            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            IntPtr pptDst,
            ref NativeSize psize,
            IntPtr hdcSrc,
            ref NativePoint pptSrc,
            int crKey,
            ref BlendFunction pblend,
            int dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
