namespace Almutamakkin.DatabaseBridge.App;

public static class UITheme
{
    // Light Theme Colors (Modern Light Mode)
    public static readonly Color BackgroundColor = Color.FromArgb(243, 247, 252);
    public static readonly Color PanelColor = Color.White;
    public static readonly Color PanelHighlight = Color.FromArgb(247, 250, 253);
    public static readonly Color TextColor = Color.FromArgb(16, 36, 76);
    public static readonly Color TextMuted = Color.FromArgb(99, 113, 137);
    public static readonly Color AccentColor = Color.FromArgb(22, 42, 95);
    public static readonly Color AccentHoverColor = Color.FromArgb(31, 61, 132);
    public static readonly Color ButtonDisabledColor = Color.FromArgb(229, 231, 235); // Gray 200
    public static readonly Color ButtonDisabledTextColor = Color.FromArgb(156, 163, 175); // Gray 400
    public static readonly Color BorderColor = Color.FromArgb(221, 230, 241);
    public static readonly Color ErrorColor = Color.FromArgb(190, 51, 65);
    public static readonly Color SuccessColor = Color.FromArgb(12, 144, 128);
    
    public static readonly Font TitleFont = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold);
    public static readonly Font HeaderFont = new Font("Segoe UI Variable Display", 12F, FontStyle.Bold);
    public static readonly Font NormalFont = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular);
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
                btn.Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold);
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
