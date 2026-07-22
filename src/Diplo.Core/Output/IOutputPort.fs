namespace Diplo.Core.Output

open System
open System.Collections.Generic

/// Abstraction de sortie pour decoupler l'affichage du metier.
/// Le CLI ecrit dans la console Spectre, le GUI lie au DataGrid/TextBox.
type IOutputPort =
    abstract WriteLine: text: string -> unit
    abstract WriteError: text: string -> unit
    abstract WriteSuccess: text: string -> unit
    abstract WriteWarning: text: string -> unit
    abstract WriteTable: items: IReadOnlyList<'T> * columns: string[] * selector: Func<'T, string[]> -> unit
