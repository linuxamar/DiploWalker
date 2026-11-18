namespace DiploWalker.Gui

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.ReactiveUI
open Avalonia.Themes.Fluent
open Avalonia.Fonts.Inter
open DiploWalker.Gui.Views
open DiploWalker.Gui.ViewModels

/// Application Avalonia principale (100 % F#, sans XAML).
type App() as this =
    inherit Application()

    do
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let mainWindow = MainWindow()
            let viewModel = MainWindowViewModel()
            mainWindow.DataContext <- viewModel
            desktop.MainWindow <- mainWindow

            // Initialiser les ViewModels
            viewModel.Initialize()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()