namespace DiploWalker.Gui

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open DiploWalker.Gui.Views
open Avalonia.Styling

type App() =
    inherit Application()

    override this.Initialize() =
        this.RequestedThemeVariant <- ThemeVariant.Default
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match box this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop -> desktop.MainWindow <- MainWindow()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

