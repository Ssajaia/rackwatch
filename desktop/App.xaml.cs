using Microsoft.Maui.Controls.Shapes;

namespace RackWatch;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        Mqtt.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new LoginPage()) { Title = "RackWatch", Width = 1200, Height = 780, MinimumWidth = 980, MinimumHeight = 660 };
}

// Colors and building blocks shared by both pages
public static class Ui
{
    public static readonly Color
        Background = Color.FromArgb("#F4F6FA"), Surface = Colors.White, Outline = Color.FromArgb("#E5E7EB"),
        Ink = Color.FromArgb("#111827"), Muted = Color.FromArgb("#6B7280"), Blue = Color.FromArgb("#2563EB"),
        Green = Color.FromArgb("#16A34A"), Amber = Color.FromArgb("#D97706"), Red = Color.FromArgb("#DC2626"),
        Gray = Color.FromArgb("#9CA3AF");

    public static Label Label(string text = "", double size = 14, Color? color = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = color ?? Ink,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
    };

    public static Border Card(View content, double padding = 20) => new()
    {
        Content = content,
        Padding = padding,
        BackgroundColor = Surface,
        Stroke = Outline,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 14 }
    };

    public static Button Button(string text, Action onClick, bool primary = true)
    {
        var button = new Button
        {
            Text = text,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            Padding = new Thickness(18, 12),
            BackgroundColor = primary ? Ink : Surface,
            TextColor = primary ? Colors.White : Ink,
            BorderColor = Outline,
            BorderWidth = primary ? 0 : 1
        };
        button.Clicked += (_, _) => onClick();
        return button;
    }
}
