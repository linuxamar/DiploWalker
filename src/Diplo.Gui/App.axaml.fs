namespace Diplo.Gui

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Diplo.Gui.Views

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match box this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()
