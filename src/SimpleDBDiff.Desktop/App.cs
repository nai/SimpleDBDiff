using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Desktop;

public sealed class App(PasswordVault vault) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage())
        {
            Title = "SimpleDBDiff", Width = 1440, Height = 900,
            MinimumWidth = 900, MinimumHeight = 650
        };
        window.Destroying += (_, _) => vault.Close();
        return window;
    }
}
