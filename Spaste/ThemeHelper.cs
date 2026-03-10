namespace Spaste;

/// <summary>
/// WinForms 컨트롤에 공통 라이트/다크 테마를 적용하는 헬퍼다.
/// 고대비 모드에서는 운영체제 색을 우선하도록 커스텀 테마를 건너뛴다.
/// </summary>
internal static class ThemeHelper
{
    private static readonly Font GridFont = new("Segoe UI", 12F, FontStyle.Regular);
    private static readonly ThemePalette DarkPalette = new(
        Back: Color.FromArgb(250, 250, 252),
        Surface: Color.White,
        SurfaceAlt: Color.FromArgb(245, 246, 248),
        Text: Color.FromArgb(34, 38, 44),
        Accent: Color.FromArgb(0, 122, 255),
        AccentSoft: Color.FromArgb(231, 241, 255),
        Border: Color.FromArgb(225, 228, 233));

    private static readonly ThemePalette LightPalette = new(
        Back: Color.FromArgb(250, 250, 252),
        Surface: Color.White,
        SurfaceAlt: Color.FromArgb(245, 246, 248),
        Text: Color.FromArgb(34, 38, 44),
        Accent: Color.FromArgb(0, 122, 255),
        AccentSoft: Color.FromArgb(231, 241, 255),
        Border: Color.FromArgb(225, 228, 233));

    /// <summary>
    /// 루트 컨트롤부터 재귀적으로 색상을 적용한다.
    /// </summary>
    public static void Apply(Control root, bool darkMode)
    {
        if (SystemInformation.HighContrast)
        {
            return;
        }

        var palette = darkMode ? DarkPalette : LightPalette;
        ApplyRecursive(root, palette);
    }

    /// <summary>
    /// 기본 버튼 스타일을 일관되게 맞춘다.
    /// </summary>
    public static void StyleActionButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Tag = primary ? "primary" : "secondary";
    }

    /// <summary>
    /// 각 컨트롤 타입별 색상을 적용한다.
    /// </summary>
    private static void ApplyRecursive(Control control, ThemePalette palette)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = palette.Back;
                form.ForeColor = palette.Text;
                form.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
                break;

            case DataGridView grid:
                ApplyGridPalette(grid, palette);
                break;

            case Button button:
                ApplyButtonPalette(button, palette);
                break;

            case TextBox textBox:
                textBox.BackColor = palette.SurfaceAlt;
                textBox.ForeColor = palette.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;

            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = palette.SurfaceAlt;
                numericUpDown.ForeColor = palette.Text;
                break;

            case Label label:
                label.ForeColor = palette.Text;
                break;

            case CheckBox checkBox:
                checkBox.ForeColor = palette.Text;
                checkBox.BackColor = Color.Transparent;
                break;

            default:
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyRecursive(child, palette);
        }
    }

    /// <summary>
    /// DataGridView 헤더/셀 색상을 일괄 적용한다.
    /// </summary>
    private static void ApplyGridPalette(DataGridView grid, ThemePalette palette)
    {
        grid.BackgroundColor = palette.Surface;
        grid.GridColor = palette.Border;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        grid.DefaultCellStyle.BackColor = palette.Surface;
        grid.DefaultCellStyle.ForeColor = palette.Text;
        grid.DefaultCellStyle.Font = GridFont;
        grid.DefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.SelectionBackColor = palette.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = palette.Back;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        grid.AlternatingRowsDefaultCellStyle.Font = GridFont;
        grid.AlternatingRowsDefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        grid.AlternatingRowsDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.AlternatingRowsDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.RowsDefaultCellStyle.SelectionBackColor = palette.Accent;
        grid.RowsDefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = palette.Accent;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowTemplate.Height = 36;
        grid.RowHeadersVisible = false;
    }

    /// <summary>
    /// 버튼 역할에 따라 강조색을 분기한다.
    /// </summary>
    private static void ApplyButtonPalette(Button button, ThemePalette palette)
    {
        var isPrimary = string.Equals(button.Tag as string, "primary", StringComparison.Ordinal);
        button.FlatAppearance.BorderColor = palette.Border;
        button.BackColor = isPrimary ? palette.Accent : palette.Surface;
        button.ForeColor = isPrimary ? Color.White : palette.Text;
    }

    /// <summary>
    /// 테마 색상 집합이다.
    /// </summary>
    private sealed record ThemePalette(
        Color Back,
        Color Surface,
        Color SurfaceAlt,
        Color Text,
        Color Accent,
        Color AccentSoft,
        Color Border);
}
