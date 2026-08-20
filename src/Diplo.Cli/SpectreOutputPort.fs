namespace Diplo.Cli

open System.Collections.Generic
open Spectre.Console

type SpectreOutputPort() =

    interface Diplo.Core.Output.IOutputPort with

        member _.WriteLine(text: string) =
            AnsiConsole.WriteLine(Markup.Escape(text))

        member _.WriteError(text: string) =
            AnsiConsole.MarkupLine("[red]" + Markup.Escape(text) + "[/]")

        member _.WriteSuccess(text: string) =
            AnsiConsole.MarkupLine("[green]" + Markup.Escape(text) + "[/]")

        member _.WriteWarning(text: string) =
            AnsiConsole.MarkupLine("[yellow]" + Markup.Escape(text) + "[/]")

        member _.WriteTable(items: IReadOnlyList<'T>, columns: string[], selector: 'T -> string[]) =
            let table = Table().Border(TableBorder.Rounded)
            for col in columns do
                table.AddColumn(col) |> ignore
            for item in items do
                let cells = selector item
                table.AddRow(cells) |> ignore
            AnsiConsole.Write(table)
