namespace Almutamakkin.DatabaseBridge.App;

public static class UITheme
{
    // Light Theme Colors (Modern Light Mode)
    public static readonly Color BackgroundColor = Color.FromArgb(245, 245, 247); // Light gray background
    public static readonly Color PanelColor = Color.FromArgb(255, 255, 255); // Pure white cards
    public static readonly Color PanelHighlight = Color.FromArgb(249, 250, 251); // Slight off-white
    public static readonly Color TextColor = Color.FromArgb(17, 24, 39); // Almost black
    public static readonly Color TextMuted = Color.FromArgb(107, 114, 128); // Gray 500
    public static readonly Color AccentColor = Color.FromArgb(14, 165, 233); // Sky 500
    public static readonly Color AccentHoverColor = Color.FromArgb(56, 189, 248); // Sky 400
    public static readonly Color ButtonDisabledColor = Color.FromArgb(229, 231, 235); // Gray 200
    public static readonly Color ButtonDisabledTextColor = Color.FromArgb(156, 163, 175); // Gray 400
    public static readonly Color BorderColor = Color.FromArgb(229, 231, 235); // Gray 200
    public static readonly Color ErrorColor = Color.FromArgb(220, 38, 38); // Red 600
    public static readonly Color SuccessColor = Color.FromArgb(16, 185, 129); // Emerald 500
    
    public static readonly Font TitleFont = new Font("Segoe UI", 16F, FontStyle.Bold);
    public static readonly Font HeaderFont = new Font("Segoe UI", 12F, FontStyle.Bold);
    public static readonly Font NormalFont = new Font("Segoe UI", 10F, FontStyle.Regular);
    public static readonly Font MonoFont = new Font("Consolas", 18F, FontStyle.Bold);

    public static void ApplyTheme(Form form)
    {
        form.BackColor = BackgroundColor;
        form.ForeColor = TextColor;
        form.Font = NormalFont;

        ApplyToControls(form.Controls);
    }

    private static void ApplyToControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                btn.BackColor = btn.Enabled ? AccentColor : ButtonDisabledColor;
                btn.ForeColor = btn.Enabled ? Color.White : ButtonDisabledTextColor;
                btn.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                btn.Cursor = Cursors.Hand;
                
                // Add soft hover effects
                btn.MouseEnter += delegate { if (btn.Enabled) btn.BackColor = AccentHoverColor; };
                btn.MouseLeave += delegate { if (btn.Enabled) btn.BackColor = AccentColor; };
                btn.EnabledChanged += delegate
                {
                    if (btn.Enabled)
                    {
                        btn.BackColor = AccentColor;
                        btn.ForeColor = Color.White;
                    }
                    else
                    {
                        btn.BackColor = ButtonDisabledColor;
                        btn.ForeColor = ButtonDisabledTextColor;
                    }
                };
            }
            else if (control is TextBox txt)
            {
                txt.BackColor = PanelHighlight;
                txt.ForeColor = TextColor;
                txt.BorderStyle = BorderStyle.FixedSingle;
                txt.Font = NormalFont;
            }
            else if (control is ComboBox cmb)
            {
                cmb.BackColor = PanelHighlight;
                cmb.ForeColor = TextColor;
                cmb.FlatStyle = FlatStyle.Flat;
                cmb.Font = NormalFont;
            }
            else if (control is ListBox lst)
            {
                lst.BackColor = PanelHighlight;
                lst.ForeColor = TextColor;
                lst.BorderStyle = BorderStyle.None;
                lst.Font = NormalFont;
            }
            else if (control is DataGridView dgv)
            {
                dgv.BackgroundColor = PanelHighlight;
                dgv.GridColor = BorderColor;
                dgv.EnableHeadersVisualStyles = false;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = BackgroundColor;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
                dgv.ColumnHeadersDefaultCellStyle.Font = HeaderFont;
                dgv.DefaultCellStyle.BackColor = PanelColor;
                dgv.DefaultCellStyle.ForeColor = TextColor;
                dgv.DefaultCellStyle.SelectionBackColor = AccentColor;
                dgv.BorderStyle = BorderStyle.None;
            }
            else if (control is RichTextBox rtb)
            {
                rtb.BackColor = BackgroundColor;
                rtb.ForeColor = TextColor;
                rtb.BorderStyle = BorderStyle.None;
            }

            if (control.HasChildren)
            {
                ApplyToControls(control.Controls);
            }
        }
    }
}
