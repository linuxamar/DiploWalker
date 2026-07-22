namespace Diplo.Gui.Views

open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Markup.Xaml
open Diplo.Gui.ViewModels

type MainWindow() as this =
    inherit Window()

    let viewModel = MainWindowViewModel()

    do
        this.DataContext <- viewModel
        AvaloniaXamlLoader.Load(this)

    member private _.OnQuit(_sender: obj, _e: RoutedEventArgs) =
        this.Close()

    member private _.OnAbout(_sender: obj, _e: RoutedEventArgs) =
        let dialog = Window()
        dialog.Title <- "À propos de Diplo"
        dialog.Width <- 400.0
        dialog.Height <- 200.0
        dialog.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        dialog.Content <- TextBlock(
            Text = "Diplo GUI v0.1.0\nGestion de conteneurs Docker via gRPC\n\nBasé sur Diplo CLI (Spectre.Console)",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = Avalonia.Thickness(20.0),
            FontSize = 14.0
        )
        dialog.ShowDialog(this) |> ignore
