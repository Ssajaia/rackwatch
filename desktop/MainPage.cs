namespace RackWatch;

public class MainPage : ContentPage
{
    readonly Label temp = Value(), hum = Value(), vent = Value(), power = Value(), fire = Value(), leak = Value();
    readonly Label stateText = Ui.Label(size: 13, color: Colors.White, bold: true);
    readonly Border statePill;
    readonly Grid tiles = new() { ColumnSpacing = 16 };
    readonly VerticalStackLayout alerts = new() { Spacing = 12 };
    readonly Chart chart = new();
    readonly GraphicsView chartView;
    string state = "normal";
    bool active = true;

    public MainPage()
    {
        BackgroundColor = Ui.Background;
        statePill = Ui.Card(stateText, padding: 0);
        statePill.Padding = new Thickness(14, 6);
        statePill.StrokeThickness = 0;
        statePill.VerticalOptions = LayoutOptions.Center;
        chartView = new GraphicsView { Drawable = chart };

        var header = Columns(12,
            (new VerticalStackLayout { Children = { Ui.Label("RackWatch", 26, bold: true), Ui.Label("Server room · room1", color: Ui.Muted) } }, GridLength.Star),
            (statePill, GridLength.Auto),
            (Ui.Button("Sign out", SignOut, primary: false), GridLength.Auto));

        foreach (var (title, value) in new[] { ("Temperature", temp), ("Humidity", hum), ("Ventilation", vent), ("Server power", power), ("Fire", fire), ("Water leak", leak) })
        {
            tiles.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            tiles.Add(Ui.Card(new VerticalStackLayout { Spacing = 6, Children = { Ui.Label(title, 13, Ui.Muted), value } }), tiles.ColumnDefinitions.Count - 1);
        }

        var chartCard = Ui.Card(Rows(16,
            new VerticalStackLayout { Spacing = 2, Children = { Ui.Label("Temperature", 16, bold: true), Ui.Label("Last 5 minutes · limits 27 °C and 30 °C", 13, Ui.Muted) } },
            chartView));

        var controls = Ui.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Ui.Label("Controls", 16, bold: true),
                Ui.Button("Restore power", () => Mqtt.Send("reset")),
                Ui.Button("Test ventilation (3 s)", () => Mqtt.Send("test_vent"), primary: false),
                Ui.Label("Power comes back only when there is no fire or leak.", 12, Ui.Muted)
            }
        });

        var alertCard = Ui.Card(Rows(12, Ui.Label("Alerts", 16, bold: true), new ScrollView { Content = alerts }));

        var page = Rows(20, header, tiles, Columns(20,
            (chartCard, new GridLength(2, GridUnitType.Star)),
            (Rows(20, controls, alertCard), GridLength.Star)));
        page.Padding = 28;
        Content = page;

        Mqtt.OnStatus = s => MainThread.BeginInvokeOnMainThread(() =>
        {
            Show(s);
            if (s.Temp is double t) chart.Add(t);
            chartView.Invalidate();
        });
        Mqtt.OnAlert = a => MainThread.BeginInvokeOnMainThread(() => AddAlert(a));

        foreach (var t in Db.RecentTemps(TimeSpan.FromMinutes(5))) chart.Add(t);
        foreach (var a in Db.RecentAlerts(50)) AddAlert(a);
        if (Mqtt.Last is { } last) Show(last);
        UpdateState();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            UpdateState();
            return active;
        });
    }

    void Show(Status s)
    {
        state = s.State;
        Set(temp, s.Temp is double t ? $"{t:0.0} °C" : "—", s.Temp > 30 ? Ui.Red : s.Temp > 27 ? Ui.Amber : Ui.Ink);
        Set(hum, s.Hum is double h ? $"{h:0} %" : "—", s.Hum is < 30 or > 60 ? Ui.Amber : Ui.Ink);
        Set(vent, s.Vent ? "On" : "Off", s.Vent ? Ui.Blue : Ui.Ink);
        Set(power, s.Power ? "On" : "Off", s.Power ? Ui.Green : Ui.Red);
        Set(fire, s.Flame ? "Detected" : "OK", s.Flame ? Ui.Red : Ui.Green);
        Set(leak, s.Leak ? "Detected" : "OK", s.Leak ? Ui.Red : Ui.Green);
        UpdateState();
    }

    // Header pill: broker and board connection first, then the state decided by the board
    void UpdateState()
    {
        var online = Mqtt.BoardOnline;
        (stateText.Text, statePill.BackgroundColor) =
            !Mqtt.Connected ? ("No broker connection", Ui.Gray) :
            !online ? ("Board offline", Ui.Gray) :
            state switch { "normal" => ("Normal", Ui.Green), "warning" => ("Warning", Ui.Amber), _ => ("Critical", Ui.Red) };
        tiles.Opacity = online ? 1 : 0.5;
    }

    void AddAlert(Alert a)
    {
        var time = a.Time.ToString(a.Time.Date == DateTime.Today ? "HH:mm:ss" : "d MMM HH:mm");
        alerts.Children.Insert(0, Columns(10,
            (Ui.Label("●", 12, a.Critical ? Ui.Red : Ui.Amber), GridLength.Auto),
            (Ui.Label(a.Title, bold: true), GridLength.Star),
            (Ui.Label(time, 13, Ui.Muted), GridLength.Auto)));
        if (alerts.Children.Count > 50) alerts.Children.RemoveAt(50);
    }

    void SignOut()
    {
        active = false;
        Mqtt.OnStatus = null;
        Mqtt.OnAlert = null;
        Application.Current!.Windows[0].Page = new LoginPage();
    }

    static Label Value() => Ui.Label("—", 26, bold: true);

    static void Set(Label label, string text, Color color)
    {
        label.Text = text;
        label.TextColor = color;
    }

    // Rows fit their content, the last row fills the remaining height
    static Grid Rows(double spacing, params View[] views)
    {
        var grid = new Grid { RowSpacing = spacing };
        for (var i = 0; i < views.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(i == views.Length - 1 ? GridLength.Star : GridLength.Auto));
            grid.Add(views[i], 0, i);
        }
        return grid;
    }

    static Grid Columns(double spacing, params (View View, GridLength Width)[] cells)
    {
        var grid = new Grid { ColumnSpacing = spacing };
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(cells[i].Width));
            grid.Add(cells[i].View, i);
        }
        return grid;
    }
}

// Temperature line of the last 5 minutes with dashed 27 °C and 30 °C limits
class Chart : IDrawable
{
    public const int Size = 150;    // 150 readings x 2 s = 5 minutes
    readonly List<double> temps = [];

    public void Add(double temp)
    {
        temps.Add(temp);
        if (temps.Count > Size) temps.RemoveAt(0);
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        const double min = 15, max = 40;
        var area = rect.Inflate(-8, -12);
        float Y(double t) => area.Bottom - (float)((Math.Clamp(t, min, max) - min) / (max - min)) * area.Height;

        canvas.FontSize = 11;
        canvas.FontColor = Ui.Muted;
        canvas.SaveState();
        canvas.StrokeSize = 1;
        canvas.StrokeDashPattern = [4, 4];
        foreach (var (limit, color) in new[] { (27, Ui.Amber), (30, Ui.Red) })
        {
            canvas.StrokeColor = color;
            canvas.DrawLine(area.Left, Y(limit), area.Right, Y(limit));
            canvas.DrawString($"{limit} °C", area.Right, Y(limit) - 4, HorizontalAlignment.Right);
        }
        canvas.RestoreState();

        if (temps.Count < 2) return;
        var line = new PathF(area.Left, Y(temps[0]));
        for (var i = 1; i < temps.Count; i++)
            line.LineTo(area.Left + i * area.Width / (Size - 1), Y(temps[i]));
        canvas.StrokeColor = Ui.Blue;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(line);
    }
}
