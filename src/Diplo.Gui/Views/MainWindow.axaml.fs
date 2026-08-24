namespace Diplo.Gui.Views

open System
open System.IO
open System.Reflection
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Markup.Xaml
open AvaloniaEdit.TextMate
open Diplo.Gui.ViewModels

type MainWindow() as this =
    inherit Window()

    let viewModel = MainWindowViewModel()

    do
        this.DataContext <- viewModel
        let assembly = typeof<MainWindow>.Assembly
        let baseDir = AppContext.BaseDirectory

        for dll in Directory.GetFiles(baseDir, "Avalonia*.dll") do
            try
                Assembly.LoadFrom(dll) |> ignore
            with ex ->
                Serilog.Log.Debug(ex, "Chargement de l'assembly {Dll} ignoré", Path.GetFileName(dll))

        use stream = assembly.GetManifestResourceStream("Diplo.Gui.Views.MainWindow.axaml")
        AvaloniaRuntimeXamlLoader.Load(stream, assembly, this) |> ignore
        use iconStream = assembly.GetManifestResourceStream("Diplo.Gui.Diplo.ico")

        if not (isNull iconStream) then
            this.Icon <- WindowIcon(iconStream)

        viewModel.VolumeTab.SetStorageProvider(this.StorageProvider)
        viewModel.ComposeTab.SetStorageProvider(this.StorageProvider)
        this.setUpComposeEditor ()
        let aboutItem = this.FindControl<MenuItem>("AboutMenuItem")
        aboutItem.Command <- Diplo.Gui.ViewModels.RelayCommand(Action(fun () -> this.OnAbout(null, RoutedEventArgs())))

    member private this.setUpComposeEditor() =
        let host = this.FindControl<Panel>("ComposeEditorHost")

        let editor =
            AvaloniaEdit.TextEditor(
                Document = viewModel.ComposeTab.ComposeEditor.Document,
                FontFamily = Avalonia.Media.FontFamily("Cascadia Code, Consolas"),
                FontSize = 13.0,
                ShowLineNumbers = true,
                Background = Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1e1e1e")),
                Foreground = Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#d4d4d4"))
            )

        let registry =
            TextMateSharp.Grammars.RegistryOptions(TextMateSharp.Grammars.ThemeName.DarkPlus)

        let installation = editor.InstallTextMate(registry)
        let yamlLang = registry.GetLanguageByExtension(".yaml")
        installation.SetGrammar(registry.GetScopeByLanguageId(yamlLang.Id))
        host.Children.Add(editor) |> ignore

    member private _.OnQuit(_sender: obj, _e: RoutedEventArgs) = this.Close()

    member private _.OnAbout(_sender: obj, _e: RoutedEventArgs) =
        let dialog = Window()
        dialog.Title <- "À propos de Diplo"
        dialog.Width <- 400.0
        dialog.Height <- 200.0
        dialog.WindowStartupLocation <- WindowStartupLocation.CenterOwner

        dialog.Content <-
            TextBlock(
                Text = "Diplo GUI v0.1.0\nGestion de conteneurs Docker via gRPC\n\nBasé sur Diplo CLI (Spectre.Console)",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = Avalonia.Thickness(20.0),
                FontSize = 14.0
            )

        dialog.ShowDialog(this) |> ignore
