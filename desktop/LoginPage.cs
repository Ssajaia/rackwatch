namespace RackWatch;

public class LoginPage : ContentPage
{
    readonly Entry nameEntry = new() { Placeholder = "Username" };
    readonly Entry passwordEntry = new() { Placeholder = "Password", IsPassword = true };
    readonly Label error = Ui.Label(size: 13, color: Ui.Red);

    string UserName => nameEntry.Text?.Trim() ?? "";
    string Password => passwordEntry.Text ?? "";

    public LoginPage()
    {
        BackgroundColor = Ui.Background;
        error.IsVisible = false;
        passwordEntry.Completed += (_, _) => SignIn();

        var card = Ui.Card(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Margin = new Thickness(0, 0, 0, 12),
                    Children = { Ui.Label("RackWatch", 28, bold: true), Ui.Label("Server room monitoring", color: Ui.Muted) }
                },
                nameEntry,
                passwordEntry,
                error,
                Ui.Button("Sign in", SignIn),
                Ui.Button("Create account", Register, primary: false)
            }
        }, padding: 32);
        card.WidthRequest = 380;
        card.HorizontalOptions = card.VerticalOptions = LayoutOptions.Center;
        Content = card;
    }

    void SignIn()
    {
        if (Db.Login(UserName, Password)) Open();
        else Fail("Wrong username or password");
    }

    void Register()
    {
        if (UserName.Length < 3 || Password.Length < 4) Fail("Use at least 3 letters for the name and 4 for the password");
        else if (!Db.Register(UserName, Password)) Fail("This username already exists");
        else Open();
    }

    void Fail(string message)
    {
        error.Text = message;
        error.IsVisible = true;
    }

    static void Open() => Application.Current!.Windows[0].Page = new MainPage();
}
