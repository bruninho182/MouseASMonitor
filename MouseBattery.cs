using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Windows.Forms;
using System.Drawing;

static class MouseBattery {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    static volatile int level = -1;
    static volatile bool receiver;
    static volatile bool wired;
    static volatile int wiredLevel = -1;
    static long lastWiredReport;
    static long lastReport;
    static bool probe;

    static void Read(string path, bool fromCable) {
        try {
            using (var handle = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero)) {
                if (handle.IsInvalid) return;
                using (var stream = new FileStream(handle, FileAccess.Read, 256, true)) {
                    var report = new byte[256];
                    int count;
                    while ((count = stream.Read(report, 0, report.Length)) > 0) {
                        if (probe && report[0] == 3) Console.WriteLine(BitConverter.ToString(report, 0, count));
                        if (count < 5 || report[0] != 3 || report[1] != 0x10 || report[2] != 0x40 || report[4] > 10) continue;
                        int value = report[4] * 10;
                        long now = Environment.TickCount & 0xFFFFFFFFL;
                        if (fromCable) {
                            wiredLevel = value;
                            Interlocked.Exchange(ref lastWiredReport, now);
                        } else {
                            level = value;
                            Interlocked.Exchange(ref lastReport, now);
                        }
                        if (probe) Console.WriteLine("{0}: {1}%", fromCable ? "USB" : "2.4 GHz", value);
                    }
                }
            }
        } catch (IOException ex) { if (probe) Console.WriteLine(ex.Message); }
        catch (UnauthorizedAccessException) { }
    }

    static void Listen() {
        var readers = new System.Collections.Generic.Dictionary<string, Thread>(StringComparer.OrdinalIgnoreCase);
        while (true) {
            var start = new ProcessStartInfo("pnputil.exe", "/enum-interfaces /enabled /class \"{4d1e55b2-f16f-11cf-88cb-001111000030}\"");
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.CreateNoWindow = true;
            using (var process = Process.Start(start)) {
                var output = process.StandardOutput.ReadToEnd();
                var matches = Regex.Matches(output, Regex.Escape(@"\\?\HID#VID_1D57&PID_FA") + @"6[01]&MI_02\S+", RegexOptions.IgnoreCase);
                bool hasReceiver = false;
                bool hasCable = false;
                foreach (Match match in matches) {
                    string path = match.Value;
                    bool fromCable = path.IndexOf("PID_FA61", StringComparison.OrdinalIgnoreCase) >= 0;
                    hasCable |= fromCable;
                    hasReceiver |= !fromCable;
                    Thread reader;
                    if (readers.TryGetValue(path, out reader) && reader.IsAlive) continue;
                    reader = new Thread(() => Read(path, fromCable)) { IsBackground = true };
                    readers[path] = reader;
                    reader.Start();
                }
                receiver = hasReceiver;
                wired = hasCable;
            }
            Thread.Sleep(3000);
        }
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    static readonly Color Background = Color.FromArgb(12, 12, 12);
    static readonly Color Track = Color.FromArgb(53, 53, 53);
    static readonly Color Muted = Color.FromArgb(174, 174, 174);
    static readonly Color Lime = Color.FromArgb(193, 247, 69);

    static Color BatteryColor(int battery) {
        return battery < 0 ? Muted : battery <= 15 ? Color.FromArgb(255, 83, 90) : Lime;
    }

    static Icon BatteryIcon(int battery, bool cable = false, bool stale = false) {
        Color color = stale ? Muted : battery < 0 && cable ? Lime : BatteryColor(battery);
        using (var image = new Bitmap(32, 32))
        using (var graphics = Graphics.FromImage(image))
        using (var pen = new Pen(color, 2.4f))
        using (var brush = new SolidBrush(color))
        using (var mouse = new System.Drawing.Drawing2D.GraphicsPath()) {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            mouse.AddArc(7, 3, 17, 16, 180, 180);
            mouse.AddArc(7, 13, 17, 16, 0, 180);
            mouse.CloseFigure();
            graphics.DrawPath(pen, mouse);
            if (cable) {
                graphics.FillPolygon(brush, new[] {
                    new Point(17, 7), new Point(11, 17), new Point(15, 17),
                    new Point(14, 25), new Point(21, 14), new Point(17, 14)
                });
            } else {
                graphics.DrawLine(pen, 15.5f, 4, 15.5f, 13);
            }
            IntPtr handle = image.GetHicon();
            try {
                using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone();
            } finally {
                DestroyIcon(handle);
            }
        }
    }

    static Bitmap PanelLogo(Image source) {
        var logo = new Bitmap(source.Width, source.Height);
        using (var graphics = Graphics.FromImage(logo))
        using (var attributes = new System.Drawing.Imaging.ImageAttributes()) {
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[] {
                new float[5], new float[5], new float[5],
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0.80f, 0.80f, 0.80f, 0f, 1f }
            }));
            graphics.DrawImage(source, new Rectangle(0, 0, logo.Width, logo.Height),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        return logo;
    }

    static Bitmap GitHubMark() {
        // GitHub mark from Primer Octicons (MIT), https://github.com/primer/octicons.
        var geometry = System.Windows.Media.Geometry.Parse(
            "M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.65 7.65 0 0 1 2-.27c.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z");
        var visual = new System.Windows.Media.DrawingVisual();
        using (var drawing = visual.RenderOpen()) {
            drawing.PushTransform(new System.Windows.Media.ScaleTransform(3, 3));
            drawing.DrawGeometry(System.Windows.Media.Brushes.White, null, geometry);
        }
        var rendered = new System.Windows.Media.Imaging.RenderTargetBitmap(48, 48, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rendered.Render(visual);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
        using (var stream = new MemoryStream()) {
            png.Save(stream);
            stream.Position = 0;
            using (var image = Image.FromStream(stream)) return new Bitmap(image);
        }
    }

    static Label AddLabel(Control window, string text, int size, FontStyle style, Color color, Rectangle bounds) {
        var label = new Label { Text = text, Font = new Font("Bahnschrift", size, style), ForeColor = color, Bounds = bounds };
        window.Controls.Add(label);
        return label;
    }

    [STAThread]
    static void Main(string[] args) {
        probe = args.Length > 0 && args[0] == "--probe";
        if (probe) {
            new Thread(Listen) { IsBackground = true }.Start();
            Thread.Sleep(10000);
            return;
        }
        using (var instance = new Mutex(false, @"Local\MouseBattery.AttackSharkR1")) {
            if (!instance.WaitOne(0)) {
                MessageBox.Show("O monitor já está na área de notificação. Procure o ícone da bateria perto do relógio.", "Attack Shark R1");
                return;
            }
            try {
                new Thread(Listen) { IsBackground = true }.Start();
                RunTray(Array.IndexOf(args, "--tray") >= 0);
            } finally {
                instance.ReleaseMutex();
            }
        }
    }

    static void RunTray(bool startHidden) {
        Application.EnableVisualStyles();
        using (var context = new ApplicationContext())
        using (var window = new Form {
            Text = "Bateria do Attack Shark R1", ClientSize = new Size(432, 322),
            AutoScaleMode = AutoScaleMode.Dpi, BackColor = Background,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            StartPosition = FormStartPosition.CenterScreen
        })
        using (var logoStream = typeof(MouseBattery).Assembly.GetManifestResourceStream("AttackShark.png"))
        using (var logoSource = Image.FromStream(logoStream))
        using (var logo = PanelLogo(logoSource))
        using (var githubMark = GitHubMark())
        using (var windowIcon = BatteryIcon(100))
        using (var tray = new NotifyIcon())
        using (var menu = new ContextMenuStrip())
        using (var timer = new System.Windows.Forms.Timer { Interval = 500 }) {
            window.Icon = windowIcon;
            AddLabel(window, "Attack Shark R1", 21, FontStyle.Bold, Color.White, new Rectangle(23, 20, 384, 42));
            var card = new Panel { BackColor = Color.FromArgb(29, 29, 29), Bounds = new Rectangle(24, 77, 384, 178) };
            window.Controls.Add(card);
            AddLabel(card, "BATERIA DO MOUSE", 10, FontStyle.Bold, Muted, new Rectangle(19, 14, 240, 22));
            var percentage = AddLabel(card, "—", 46, FontStyle.Bold, Lime, new Rectangle(14, 34, 252, 77));
            var status = AddLabel(card, "", 10, FontStyle.Regular, Muted, new Rectangle(19, 139, 348, 31));
            var gauge = new Panel { BackColor = Track, Bounds = new Rectangle(20, 114, 344, 11), AccessibleName = "Nível da bateria" };
            card.Controls.Add(gauge);
            card.Controls.Add(new PictureBox {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = card.BackColor,
                Bounds = new Rectangle(272, 14, 92, 92),
                AccessibleName = "Logo da Attack Shark"
            });
            var credit = new LinkLabel {
                Text = "Dev : Bruno Ferreira", Font = new Font("Bahnschrift", 11),
                Bounds = new Rectangle(24, 270, 384, 36), LinkColor = Lime,
                ActiveLinkColor = Color.White, VisitedLinkColor = Lime,
                Padding = new Padding(32, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
                AccessibleDescription = "Abre o perfil de Bruno Ferreira no GitHub"
            };
            credit.Paint += delegate(object sender, PaintEventArgs e) {
                int size = (int)(20 * e.Graphics.DpiX / 96);
                e.Graphics.DrawImage(githubMark, 2, (credit.Height - size) / 2, size, size);
            };
            credit.Links.Add(0, credit.Text.Length, "https://github.com/bruninho182");
            credit.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e) {
                try {
                    Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true });
                } catch (System.ComponentModel.Win32Exception) {
                    MessageBox.Show(window, "Não foi possível abrir o GitHub no navegador.", "Attack Shark R1");
                }
            };
            window.Controls.Add(credit);

            int displayed = -2;
            bool displayedCable = false;
            bool displayedStale = false;
            gauge.Paint += delegate(object sender, PaintEventArgs e) {
                if (displayed <= 0) return;
                using (var brush = new SolidBrush(displayedStale ? Muted : BatteryColor(displayed)))
                    e.Graphics.FillRectangle(brush, 0, 0, gauge.ClientSize.Width * displayed / 100f, gauge.ClientSize.Height);
            };

            Icon trayIcon = null;
            bool exiting = false;
            Action show = delegate {
                window.Show();
                window.WindowState = FormWindowState.Normal;
                window.Activate();
            };
            string tooltip = "Attack Shark R1";
            var summary = menu.Items.Add(tooltip);
            summary.Enabled = false;
            menu.Items.Add("Abrir monitor", null, delegate { show(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, delegate {
                exiting = true;
                tray.Visible = false;
                window.Close();
                context.ExitThread();
            });
            tray.ContextMenuStrip = menu;
            MouseEventHandler openFromTray = delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) show();
            };
            tray.MouseClick += openFromTray;
            window.FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (exiting || e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                window.Hide();
            };
            window.Resize += delegate {
                if (window.WindowState == FormWindowState.Minimized) window.Hide();
            };

            EventHandler refresh = delegate {
                long now = Environment.TickCount & 0xFFFFFFFFL;
                bool connected = receiver;
                bool cable = wired;
                long wirelessAge = (now - Interlocked.Read(ref lastReport)) & 0xFFFFFFFFL;
                long wiredAge = (now - Interlocked.Read(ref lastWiredReport)) & 0xFFFFFFFFL;
                int wireless = connected && wirelessAge < 15000 ? level : -1;
                int current = cable && wiredAge < 15000 && wiredLevel >= 0 ? wiredLevel : wireless;
                bool stale = cable && current < 0 && (level >= 0 || wiredLevel >= 0);
                if (stale) {
                    current = wiredLevel >= 0 && (level < 0 || wiredAge < wirelessAge) ? wiredLevel : level;
                }
                string cableStatus = current == 100 && !stale ? "100% no cabo USB" : "Carregando via USB (estimado)";
                if (stale) cableStatus += " · Última leitura";
                else if (cable && current < 0) cableStatus = "Carregando via USB (est.) · Sem leitura";
                status.Text = cable ? cableStatus
                    : current >= 0 ? "Receptor USB de 2,4 GHz conectado"
                    : connected ? "Aguardando leitura. Mova ou clique no mouse."
                    : "Conecte o receptor USB de 2,4 GHz.";
                if (current != displayed || cable != displayedCable || stale != displayedStale) {
                    displayed = current;
                    displayedCable = cable;
                    displayedStale = stale;
                    percentage.Text = current >= 0 ? current + "%" : "—";
                    percentage.ForeColor = stale ? Muted : BatteryColor(current);
                    gauge.Invalidate();
                    Icon next = BatteryIcon(current, cable, stale);
                    tray.Icon = next;
                    if (trayIcon != null) trayIcon.Dispose();
                    trayIcon = next;
                }
                tooltip = cable ? "Attack Shark R1 • " + (stale ? "USB • Última leitura " + current + "%" : "USB • " + (current >= 0 ? current + "%" : "Sem leitura"))
                    : current >= 0 ? "Attack Shark R1 • Bateria " + current + "%"
                    : connected ? "Attack Shark R1 • Aguardando leitura"
                    : "Attack Shark R1 • Receptor desconectado";
                tray.Text = tooltip;
                summary.Text = tooltip;
            };

            try {
                timer.Tick += refresh;
                refresh(null, EventArgs.Empty);
                tray.Visible = true;
                timer.Start();
                if (!startHidden) show();
                Application.Run(context);
            } finally {
                tray.Visible = false;
                if (trayIcon != null) trayIcon.Dispose();
            }
        }
    }
}













