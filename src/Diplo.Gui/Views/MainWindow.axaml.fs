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

    let viewModel = new MainWindowViewModel()

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
        aboutItem.Command <- Diplo.Gui.ViewModels.RelayCommand(Action(fun () -> this.OnAbout(null, null)))

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
        dialog.Width <- 520.0
        dialog.Height <- 420.0
        dialog.WindowStartupLocation <- WindowStartupLocation.CenterOwner

        let stack = StackPanel(Margin = Avalonia.Thickness(20.0), Spacing = 6.0)

        let header =
            TextBlock(Text = "Diplo GUI v0.1.0", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 16.0)

        stack.Children.Add(header) |> ignore

        let desc =
            TextBlock(
                Text = "Gestion de conteneurs Docker via gRPC\nBasé sur Diplo CLI (Spectre.Console)",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 13.0,
                Opacity = 0.8
            )

        stack.Children.Add(desc) |> ignore

        let sep1 = Separator(Margin = Avalonia.Thickness(0.0, 8.0))
        stack.Children.Add(sep1) |> ignore

        let licTitle =
            TextBlock(
                Text = "Licences des dépendances",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                FontSize = 13.0
            )

        stack.Children.Add(licTitle) |> ignore

        let licenses =
            [ "MIT (22) : Avalonia, FSharp.Core, DiscUtils, Microsoft, Spectre, YamlDotNet, ZstdSharp"
              "Apache-2.0 (13) : gRPC, protobuf-net, Serilog, xunit"
              "BSD-3-Clause (1) : Google.Protobuf"
              "LGPL-3.0+ (1) : Hawkynt.FileFormats.FileSystems" ]

        for line in licenses do
            let t =
                TextBlock(
                    Text = "  • " + line,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 12.0,
                    Opacity = 0.75
                )

            stack.Children.Add(t) |> ignore

        let sep2 = Separator(Margin = Avalonia.Thickness(0.0, 8.0))
        stack.Children.Add(sep2) |> ignore

        let notice =
            TextBlock(
                Text = "Voir THIRD-PARTY-NOTICES.txt pour le texte intégral des licences.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 11.0,
                Opacity = 0.6,
                FontStyle = Avalonia.Media.FontStyle.Italic
            )

        stack.Children.Add(notice) |> ignore

        let outer = StackPanel()

        outer.Children.Add(
            ScrollViewer(
                Content = stack,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            )
        )
        |> ignore

        dialog.Content <- outer

        dialog.ShowDialog(this) |> ignore
