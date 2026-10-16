using Microsoft.AspNetCore.Components.WebView.Maui;

namespace SimpleDBDiff.Desktop;

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(DesktopRoutes) });
        Content = view;
    }
}
