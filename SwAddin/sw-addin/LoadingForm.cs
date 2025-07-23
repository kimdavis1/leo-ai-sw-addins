using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace sw_addin
{
    public partial class LoadingForm : Form
    {
        private Label messageLabel;
        private PictureBox gifBox;
        private int borderRadius = 20;
        private Color borderColor = Color.Black;

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
        );

        public LoadingForm(string message, string gifPath = null)
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 90);
            this.BackColor = Color.White;

            // Set the form shape
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, borderRadius, borderRadius));

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.UserPaint |
                          ControlStyles.AllPaintingInWmPaint, true);

            messageLabel = new Label
            {
                Text = message,
                Font = new Font("DM Sans", 15, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x72, 0x6D, 0x77),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.None,
                AutoSize = true,
                MaximumSize = new Size(280, 0), // Adjust this width as needed
                AutoEllipsis = true,
            };

            gifBox = new PictureBox
            {
                Size = new Size(100, 110),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.None,
            };

            if (!string.IsNullOrEmpty(gifPath))
            {
                try
                {
                    gifBox.Image = Image.FromFile(gifPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading GIF: {ex.Message}");
                }
            }

            this.Controls.Add(gifBox);
            this.Controls.Add(messageLabel);

            gifBox.Location = new Point(1, (this.ClientSize.Height - gifBox.Height) / 3);
            messageLabel.Location = new Point(gifBox.Right + 2, (this.ClientSize.Height - messageLabel.Height) / 2);

            this.Paint += (sender, e) => DrawBorder(e.Graphics);
        }

        private void DrawBorder(Graphics g)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(0, 0, borderRadius, borderRadius, 180, 90);
                path.AddArc(this.Width - borderRadius - 1, 0, borderRadius, borderRadius, 270, 90);
                path.AddArc(this.Width - borderRadius - 1, this.Height - borderRadius - 1, borderRadius, borderRadius, 0, 90);
                path.AddArc(0, this.Height - borderRadius - 1, borderRadius, borderRadius, 90, 90);
                path.CloseAllFigures();

                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (Pen pen = new Pen(borderColor, 10))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, borderRadius, borderRadius));
        }

        public void SetBorderRadius(int radius)
        {
            borderRadius = radius;
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, borderRadius, borderRadius));
            this.Invalidate();
        }
    }
}